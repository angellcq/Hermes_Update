using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Runtime.InteropServices;

namespace HermesInstaller.Core
{
    /// <summary>
    /// E7（2026-10-25 升级日志回放）：升级预热。把本轮升级会用到的 Electron 运行时 zip
    /// 在启动 hermes update 之前提前下载进 @electron/get 的本地缓存
    /// （%LOCALAPPDATA%\electron\Cache），使 update 中的 electron install 环节直接命中
    /// 缓存，不再中途从 GitHub / 镜像拉大文件。
    /// 实测依据：@electron/get 2.x Cache.getCacheDirectory 的缓存目录名 =
    /// sha256(镜像 base + "v" + 版本)（UTF-8），文件名保留原名。已在本机对上
    /// 0a74c7b0…(npmmirror) 与 902aaaf9…(github) 两个键。2026-10-25 的失败正是
    /// 「昨天从 npmmirror（L1）下到的缓存，今天 planner 切回 L0 导致键变化、
    /// 缓存未命中，现场直连 GitHub 404」。
    /// 校验源优先级：本机 node_modules\electron\checksums.json（electron 包自带，
    /// 版本一致时最可信）→ 镜像 SHASUMS256.txt；两者都拿不到时按「未校验」放入
    /// 缓存并在日志注明（@electron/get 命中时会再用包内 checksums.json 自校验）。
    /// 本类是尽力而为（best-effort）：任何失败只 Warn 落日志，不阻塞升级主流程。
    /// </summary>
    public sealed class DependencyPrefetcher
    {
        /// <summary>@electron/get artifact-utils.js 的默认 BASE_URL。</summary>
        private const string GithubElectronBase = "https://github.com/electron/electron/releases/download/";

        /// <summary>下载总量预算：electron zip 有 150+ MB，比 RetryPolicy.FileDownload
        /// 的 90s 放宽到 15 分钟；E12-③ 起再加「无进展看门狗」兜底（见 DownloadAndPlaceAsync）。</summary>
        private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(15);

        /// <summary>E12-③（2026-10-25 14:10 事故）：单次读取无进展就判连接僵死。
        /// 当时 GitHub 直连 40 分钟只挪了 62MB 且中途能长时间 0 字节，而 HttpClient.Timeout
        /// 配 ResponseHeadersRead 时只覆盖到响应头，body 流不受约束 —— 必须自己看门狗。</summary>
        private static readonly TimeSpan NoProgressTimeout = TimeSpan.FromSeconds(60);

        /// <summary>E12-③：下载进度日志间隔（长时间静默是这次事故最难排查的一点）。</summary>
        private static readonly TimeSpan ProgressLogInterval = TimeSpan.FromSeconds(15);

        private readonly Logger _logger;
        private readonly NetworkPreparer _network;
        private readonly ProcessRunner _runner;
        private readonly NetworkPlanner _planner;

        public DependencyPrefetcher(Logger logger, NetworkPreparer network, ProcessRunner runner)
            : this(logger, network, runner, null)
        {
        }

        public DependencyPrefetcher(Logger logger, NetworkPreparer network, ProcessRunner runner, NetworkPlanner planner)
        {
            _logger = logger;
            _network = network;
            _runner = runner;
            _planner = planner;
        }

        /// <summary>GetCacheDirectory 的目录名：sha256(base + "v" + version) 的十六进制。</summary>
        public static string CacheDirectoryName(string baseUrl, string version)
        {
            byte[] hash;
            using (var sha256 = SHA256.Create())
            {
                hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(
                    EnsureTrailingSlash(baseUrl) + "v" + version.TrimStart('v', 'V')));
            }
            return Sha256Hex(hash);
        }

        /// <summary>
        /// 预热入口。返回一句话摘要（已就绪 / 已缓存 / 失败明细），供进度回调展示。
        /// 任何异常都收敛在内部，只 Warn 落日志。
        /// </summary>
        public async Task<string> WarmAsync(CancellationToken cancellationToken)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                // 1) 镜像 base：与本次升级子进程将拿到的 ELECTRON_MIRROR 同一解析路径，
                //    保证预热的缓存键 == update 实际查询的键。
                string electronBase = ResolveElectronBase();

                // 2) 版本候选：升级后将从 upstream fast-forward，所以 upstream/main 的
                //    锁定版本排第一；本机当前版本兜底（update 不 bump 时也要命中）。
                //    （ProbeUpcoming 内部的 git fetch upstream main 同时新鲜化了
                //    refs/remotes/upstream/main，供下面的 fork 预推送使用。）
                var versions = new List<string>();
                AddVersion(versions, await ProbeUpcomingElectronVersionAsync(cancellationToken).ConfigureAwait(false));
                AddVersion(versions, FirstQuotedValue(
                    TryReadFile(Path.Combine(HermesPaths.InstallDir, "package-lock.json")) ?? string.Empty,
                    null, "\"node_modules/electron\""));
                AddVersion(versions, FirstQuotedValue(
                    TryReadFile(Path.Combine(HermesPaths.InstallDir, "node_modules", "electron", "package.json")) ?? string.Empty,
                    "version", null));

                if (versions.Count == 0)
                {
                    _logger.Warn("[PREFETCH] 未解析到任何 Electron 版本（无 package-lock / git 探测失败），跳过预热。");
                    return "预热：未找到 Electron 版本信息，已跳过";
                }

                // 3) fork 预同步（E7-2，2026-10-25 08:44 升级事故回放）：hermes update
                //    收尾时会把 upstream/main push 回 origin fork（update_cmd_git.py
                //    `_push_synced_fork` → `git push origin main --force-with-lease`）。
                //    这个推送在网络慢时可以静默挂数分钟——那次升级正是被安装器的
                //    180s 无进展预算杀死。update 的推跳过条件 = HEAD == origin/main，
                //    所以这里抢先把 upstream/main 推上 origin，update 的同名步骤直接跳过。
                var notes = new List<string>();
                string forkNote = await PreflightForkSyncAsync(cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(forkNote))
                {
                    notes.Add(forkNote);
                }

                foreach (string version in versions)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string note = await WarmOneAsync(electronBase, version, cancellationToken).ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(note))
                    {
                        notes.Add(note);
                    }
                }

                // E10-③：electron-builder 工具集（winCodeSign / 7zip）archive cache 预热。
                // best-effort：失败只记摘要句，不阻塞升级。
                string builderNote = await WarmBuilderToolsetsAsync(cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(builderNote))
                {
                    notes.Add(builderNote);
                }

                if (notes.Count == 0)
                {
                    return "预热：Electron 运行时缓存已就绪";
                }
                return "预热：" + string.Join("；", notes.ToArray());
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.Warn("[PREFETCH] 预热失败（不影响升级，hermes update 将按原路径重试下载）: " + ex.Message);
                return "预热失败：" + ex.Message;
            }
        }

        /// <summary>预热单个版本。返回差异化的摘要句，全源失败返回 null（明细已落日志）。</summary>
        private async Task<string> WarmOneAsync(
            string electronBase, string version, CancellationToken cancellationToken)
        {
            string fileName = ArtifactFileName(version);
            string keyDir = Path.Combine(ResolveCacheRoot(), CacheDirectoryName(electronBase, version));
            string target = Path.Combine(keyDir, fileName);
            string expected = await ResolveExpectedHashAsync(version, fileName, cancellationToken).ConfigureAwait(false);

            if (File.Exists(target))
            {
                if (expected == null)
                {
                    _logger.Info(string.Format("[PREFETCH] electron {0} 已有缓存（无可用校验源，保留）: {1}", version, keyDir));
                    SeedAlternateBaseKey(electronBase, version, target, cancellationToken);
                    return version + " 已缓存（未校验）";
                }
                string actual = Sha256File(target);
                if (string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.Success(string.Format("[PREFETCH] electron {0} 缓存已存在且校验通过: {1}", version, keyDir));
                    SeedAlternateBaseKey(electronBase, version, target, cancellationToken);
                    return version + " 已就绪";
                }
                _logger.Warn(string.Format("[PREFETCH] electron {0} 缓存校验失败，删除后重新预热: {1}", version, keyDir));
                try { File.Delete(target); }
                catch (Exception ex)
                {
                    _logger.Warn("[PREFETCH] 删除坏缓存失败: " + ex.Message);
                    return version + " 缓存损坏且无法删除";
                }
            }

            // E12-④（2026-10-25 14:10 事故）：目标键没有，但「另一个基址键」可能已有同一份
            // 产物（本机就是 npmmirror 键里有 151MB、GitHub 键里空 → 白下 151MB）。
            // 产物与键无关（@electron/get 只按键找文件，命中后再用包内 checksums.json 自校验），
            // 所以先做一次跨键复用：校验通过就硬链接到目标键，零重复下载。
            string adopted = TryAdoptFromAlternateKey(electronBase, version, fileName, expected);
            if (adopted != null)
            {
                _logger.Success(string.Format("[PREFETCH] electron {0} 复用已有缓存（{1} → {2}）",
                    version, adopted, keyDir));
                SeedAlternateBaseKey(electronBase, version, target, cancellationToken);
                return version + " 复用已有缓存";
            }

            // 下载源优先级：升级将直连的那一个 base（保证缓存键一致）→ npmmirror → GitHub 官方。
            // 键始终按升级将用的 electronBase 落位——内容一致即可，@electron/get 只看键。
            var sources = new List<string>();
            AddSource(sources, electronBase);
            AddSource(sources, Mirrors.ElectronMirror);
            AddSource(sources, GithubElectronBase);

            Exception lastError = null;
            foreach (string sourceBase in sources)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException(cancellationToken);
                }
                try
                {
                    string url = EnsureTrailingSlash(sourceBase) + "v" + version + "/" + fileName;
                    long rateKbps = await DownloadAndPlaceAsync(url, keyDir, fileName, expected, cancellationToken).ConfigureAwait(false);
                    ReportPlanner(true, string.Format("源 {0} 下载完成", DescribeSource(sourceBase)), rateKbps);
                    _logger.Success(string.Format(
                        "[PREFETCH] electron {0} 预热完成（源 {1}，缓存键 {2}）。校验: {3}",
                        version, DescribeSource(sourceBase), CacheDirectoryName(electronBase, version),
                        expected != null ? "通过" : "未校验（无 SHASUMS / checksums.json 匹配）"));
                    // E10-③ 双键种子：update 子进程的 env 流转不保证（11:15 那轮 install.js
                    // 疑似在无镜像 env 下直连 GitHub），所以除「升级将用 base」的键外，
                    // 再给 @electron/get 默认 GitHub 基址也落一份键。硬链接同卷 0 冗余磁盘。
                    SeedAlternateBaseKey(electronBase, version, Path.Combine(keyDir, fileName), cancellationToken);
                    return version + " 预热完成（" + DescribeSource(sourceBase) + "）";
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    ReportPlanner(false, string.Format("源 {0} 失败: {1}", DescribeSource(sourceBase), ex.Message), -1);
                    _logger.Warn(string.Format("[PREFETCH] 从 {0} 下载 electron {1} 失败: {2}", sourceBase, version, ex.Message));
                }
            }

            _logger.Warn(string.Format("[PREFETCH] electron {0} 所有源都失败，放弃本轮预热（升级中途仍会重试）: {1}",
                version, lastError != null ? lastError.Message : "未知"));
            return null;
        }

        /// <summary>
        /// E12-②：把预热的真实结果反馈给 planner —— 慢/失败累积到阈值后降级，
        /// 随后的 BuildEnvironment（hermes update 用）就会切到镜像，而不是继续直连。
        /// </summary>
        private void ReportPlanner(bool ok, string reason, long rateKbps)
        {
            if (_planner == null)
            {
                return;
            }
            try
            {
                if (ok)
                {
                    _planner.RecordSuccess(EndpointGroup.Electron);
                    return;
                }
                _planner.RecordFailure(EndpointGroup.Electron, reason, rateKbps);
                if (_planner.ShouldDowngrade(EndpointGroup.Electron))
                {
                    _planner.Downgrade(EndpointGroup.Electron, reason);
                    _logger.Warn("[PREFETCH] electron 组已降级（后续 hermes update 改用镜像）: " + reason);
                }
            }
            catch (Exception ex)
            {
                _logger.Warn("[PREFETCH] 分级状态登记失败（不影响预热）: " + ex.Message);
            }
        }

        /// <summary>
        /// E12-④：跨键复用。目标键没文件时，检查同一版本在其它基址键（本次 base / npmmirror /
        /// GitHub 官方）下是否已有成品；有且校验通过就硬链接到目标键（失败退化为复制）。
        /// 返回被复用的源文件路径，无可用返回 null。
        /// </summary>
        private string TryAdoptFromAlternateKey(
            string electronBase, string version, string fileName, string expected)
        {
            try
            {
                string target = Path.Combine(
                    Path.Combine(ResolveCacheRoot(), CacheDirectoryName(electronBase, version)), fileName);
                var bases = new List<string>();
                AddSource(bases, electronBase);
                AddSource(bases, Mirrors.ElectronMirror);
                AddSource(bases, GithubElectronBase);

                string root = ResolveCacheRoot();
                foreach (string baseUrl in bases)
                {
                    string candidate = Path.Combine(
                        Path.Combine(root, CacheDirectoryName(baseUrl, version)), fileName);
                    if (string.Equals(Path.GetFullPath(candidate), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    if (!File.Exists(candidate))
                    {
                        continue;
                    }
                    if (expected != null)
                    {
                        string actual = Sha256File(candidate);
                        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                        {
                            _logger.Warn(string.Format(
                                "[PREFETCH] 跨键复用候选校验不通过，忽略: {0}", candidate));
                            continue;
                        }
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    string link = TryCreateHardLink(target, candidate);
                    if (link == null)
                    {
                        File.Copy(candidate, target, true);
                    }
                    _logger.Info(string.Format("[PREFETCH] 跨键复用 {0}（源 {1}，校验: {2}）",
                        fileName, DescribeSource(baseUrl), expected != null ? "通过" : "未校验"));
                    return candidate;
                }
            }
            catch (Exception ex)
            {
                _logger.Warn("[PREFETCH] 跨键复用失败（继续按原路径下载）: " + ex.Message);
            }
            return null;
        }

        /// <summary>
        /// E10-③ 双键种子：把已就位的 electron zip 再种到「GitHub 官方基址」的缓存键下
        /// （主键 = 升级将用的 base，比如 npmmirror）。@electron/get 只看键名不看内容，
        /// 同卷硬链接不占额外磁盘；硬链接不可用时退化为复制。子进程 env 流转不可靠
        /// （2026-10-25 11:15 那轮 install.js 疑似没拿到注入的镜像 env 而直连 GitHub），
        /// 双包键保证两种流向下都命中。best-effort：失败只 Warn。
        /// </summary>
        private void SeedAlternateBaseKey(string electronBase, string version, string canonicalFile, CancellationToken cancellationToken)
        {
            // stale 时间宽容：本地预热瞬间的取消不打到升级主链路
            try
            {
                string altBase = GithubElectronBase;
                if (string.Equals(EnsureTrailingSlash(electronBase), EnsureTrailingSlash(altBase), StringComparison.OrdinalIgnoreCase))
                {
                    return; // 主键就是 GitHub 官方键，无需双包
                }
                string altKeyDir = Path.Combine(ResolveCacheRoot(), CacheDirectoryName(altBase, version));
                string altTarget = Path.Combine(altKeyDir, Path.GetFileName(canonicalFile));
                if (string.Equals(Path.GetFullPath(altTarget), Path.GetFullPath(canonicalFile), StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
                if (File.Exists(altTarget))
                {
                    return; // 已有同名并默认可信（其来源要么是官方缓存要么是早前种子）
                }
                Directory.CreateDirectory(altKeyDir);
                string viaHardlink = TryCreateHardLink(altTarget, canonicalFile);
                if (viaHardlink != null)
                {
                    _logger.Info("[PREFETCH] 双键种子（硬链接）: " + viaHardlink);
                    return;
                }
                File.Copy(canonicalFile, altTarget, true);
                _logger.Info("[PREFETCH] 双键种子（复制，硬链接不可用）: " + altTarget);
            }
            catch (Exception ex)
            {
                _logger.Warn("[PREFETCH] 双键种子失败（不影响升级）: " + ex.Message);
            }
        }

        /// <summary>KERNEL32 CreateHardLink。返回实际创建的链接路径，失败返回 null。</summary>
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CreateHardLink(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);

        private static string TryCreateHardLink(string linkPath, string existingFile)
        {
            try
            {
                if (File.Exists(linkPath))
                {
                    return null;
                }
                return CreateHardLink(linkPath, existingFile, IntPtr.Zero) ? linkPath : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// E10-③：electron-builder 工具集预热（winCodeSign 2.6.0 / 7zip 1.0.0）。
        /// 依据（2026-10-25 npm tarball 考古实锤，app-builder-lib 26.18.0）：
        ///   • downloadBuilderToolset 的 archive cache = &lt;ELECTRON_BUILDER_CACHE 或
        ///     %LOCALAPPDATA%\electron-builder\Cache&gt;\&lt;releaseName&gt;\&lt;filenameWithExt&gt;，
        ///     resolveFromArchiveCache 命中后离线解压并写 cacheState，一次网络不会发生。
        ///   • winCodeSign 走 legacy bin：releaseName "winCodeSign-2.6.0"、
        ///     文件 "winCodeSign-2.6.0.7z"（非旧 .zip）、官方 sha256 见常量。
        ///   • 解压 .7z 需要 7zip 工具集：releaseName "7zip@1.0.0"、
        ///     文件 "7zip-win-x64.tar.gz"。两个常量在 26.x 全域稳定（best-effort 前提）。
        /// seed 双根：① env 覆盖目标（NetworkPreparer 默认注入 HermesPaths.CacheDir\electron-builder）
        /// ② @electron/get 默认根（env 流转断掉时的兜底，11:15 教训）。
        /// </summary>
        private const string BuilderWinCodeSignRelease = "winCodeSign-2.6.0";
        private const string BuilderWinCodeSignFile = "winCodeSign-2.6.0.7z";
        private const string BuilderWinCodeSignSha256 = "cdaec7154dda7cc31f88d886e2489379a0625a737d610b5ae7f62a12f16743a4";
        private const string Builder7ZipRelease = "7zip@1.0.0";
        private const string Builder7ZipFile = "7zip-win-x64.tar.gz";
        private const string Builder7ZipSha256 = "be071f15bd6da2f78fe81c6ddef2009b0c4d8a51f36b780cb806c7e6df95e1b3";
        private const string GithubBuilderBase = "https://github.com/electron-userland/electron-builder-binaries/releases/download/";

        /// <summary>工具集单源下载预算：官方源慢（13:01 实测 winCodeSign 5.6MB 走了 6.5 分钟）
        /// 时快速切到 npmmirror，避免把升级开工时刻整体拖后。</summary>
        private static readonly TimeSpan BuilderToolsetTimeout = TimeSpan.FromMinutes(3);

        /// <summary>builder 工具集预热摘要句，无数需要时返回 null。</summary>
        private async Task<string> WarmBuilderToolsetsAsync(CancellationToken cancellationToken)
        {
            string builderBase;
            try
            {
                builderBase = EnsureTrailingSlash(_network != null ? _network.ResolveBuilderBase() : Mirrors.BuilderMirror);
            }
            catch (Exception ex)
            {
                _logger.Warn("[PREFETCH-BUILDER] 解析镜像基址失败，按 npmmirror 处理: " + ex.Message);
                builderBase = (Mirrors.BuilderMirror ?? "https://npmmirror.com/mirrors/electron-builder-binaries/");
                if (!builderBase.EndsWith("/", StringComparison.Ordinal))
                {
                    builderBase += "/";
                }
            }

            var roots = new List<string>();
            // ① env 覆盖目标：与 NetworkPreparer.BuildEnvironment 第 51 行同一推导
            roots.Add(Path.Combine(HermesPaths.CacheDir, "electron-builder"));
            // ② @electron/get 默认根（getCacheDirectory allowEnvVarOverride 落空时）
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string defaultRoot = Path.Combine(localAppData, "electron-builder", "Cache");
            if (!RootsContain(roots, defaultRoot))
            {
                roots.Add(defaultRoot);
            }

            var done = new List<string>();
            var notes = new List<string>();
            // E10-③ 补强（13:01 实测：L0 官方源 5.6MB 走了 6.5 分钟）：
            // 工具集内容有官方 sha256 校验、与来源无关，所以按「快源优先」排序——
            // npmmirror 打头，再试与 env 同路的解析基址，最后 GitHub 官方。
            var sources = new List<string>();
            AddSource(sources, Mirrors.BuilderMirror);
            AddSource(sources, builderBase);
            AddSource(sources, GithubBuilderBase);
            WarmBuilderToolset(sources, roots, BuilderWinCodeSignRelease, BuilderWinCodeSignFile,
                BuilderWinCodeSignSha256, done, notes, cancellationToken);
            WarmBuilderToolset(sources, roots, Builder7ZipRelease, Builder7ZipFile,
                Builder7ZipSha256, done, notes, cancellationToken);

            if (done.Count == 0 && notes.Count == 0)
            {
                return null;
            }
            return done.Count > 0
                ? "工具集就绪×" + done.Count + (notes.Count > 0 ? "；" + notes[0] : string.Empty)
                : notes[0];
        }

        /// <summary>单工具集：任一根命中即完成；否则按来源优先级下载并双根落位（同卷硬链接优先）。</summary>
        private void WarmBuilderToolset(
            List<string> sources, List<string> roots, string releaseName, string fileName, string expectedSha256,
            List<string> done, List<string> notes, CancellationToken cancellationToken)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                string canonical = null;
                // 1) 命中检查（任一根）
                foreach (string root in roots)
                {
                    string candidate = Path.Combine(root, releaseName, fileName);
                    if (File.Exists(candidate))
                    {
                        _logger.Info(string.Format("[PREFETCH-BUILDER] {0} 命中缓存 {1}", fileName, candidate));
                        canonical = candidate;
                        break;
                    }
                }

                // 2) 未命中 → 按快源优先逐个尝试下载到第一根（env 目标），随后向其余根种子。
                // URL 为字面拼接：builder 侧 = base + releaseName + "/" + filename，@ 不转义。
                if (canonical == null)
                {
                    string primaryRoot = roots[0];
                    string primaryDir = Path.Combine(primaryRoot, releaseName);
                    Exception lastError = null;
                    foreach (string source in sources)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        string url = source + releaseName + "/" + fileName;
                        try
                        {
                            DownloadAndPlaceAsync(url, primaryDir, fileName, expectedSha256,
                                cancellationToken, BuilderToolsetTimeout).GetAwaiter().GetResult();
                            canonical = Path.Combine(primaryDir, fileName);
                            _logger.Success(string.Format("[PREFETCH-BUILDER] {0} 预热完成（源 {1}，写入 {2}）",
                                fileName, DescribeSource(source), canonical));
                            break;
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            lastError = ex;
                            _logger.Warn(string.Format("[PREFETCH-BUILDER] 从 {0} 下载 {1} 失败: {2}",
                                DescribeSource(source), fileName, ex.Message));
                        }
                    }
                    if (canonical == null)
                    {
                        throw new InstallerException("所有来源都失败: "
                            + (lastError != null ? lastError.Message : "未知"));
                    }
                }

                // 3) 双根种子（同卷硬链接优先；远处根复制兜底）
                foreach (string root in roots)
                {
                    string target = Path.Combine(root, releaseName, fileName);
                    if (string.Equals(Path.GetFullPath(target), Path.GetFullPath(canonical), StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    if (File.Exists(target))
                    {
                        continue;
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    string link = TryCreateHardLink(target, canonical);
                    if (link == null)
                    {
                        File.Copy(canonical, target, true);
                    }
                }

                done.Add(fileName);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                notes.Add(fileName + " 预热失败: " + ex.Message);
                _logger.Warn("[PREFETCH-BUILDER] " + fileName + " 预热失败（不影响升级）: " + ex.Message);
            }
        }

        private static bool RootsContain(List<string> roots, string candidate)
        {
            foreach (string root in roots)
            {
                if (string.Equals(Path.GetFullPath(root), Path.GetFullPath(candidate), StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 下载 zip → 校验 → 原子落位到 keyDir/fileName。校验不一致抛 InstallerException。
        /// 返回本次实际平均速率（KB/s，未知返回 -1）。
        /// E12-③：body 流改为「分块读 + 无进展看门狗 + 总预算 + 进度日志」——
        /// HttpClient.Timeout 在 ResponseHeadersRead 下只覆盖到响应头，body 读不到数据时会
        /// 无限等（2026-10-25 14:10 那轮 151MB 卡了 40 分钟、日志 40 分钟没一行）。
        /// </summary>
        private async Task<long> DownloadAndPlaceAsync(
            string url, string keyDir, string fileName, string expected, CancellationToken cancellationToken,
            TimeSpan? timeout = null)
        {
            Directory.CreateDirectory(keyDir);
            string temp = Path.Combine(keyDir, fileName + ".prefetch-" + Guid.NewGuid().ToString("N") + ".tmp");
            bool placed = false;
            TimeSpan budget = timeout.HasValue ? timeout.Value : DownloadTimeout;
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            long received = 0;
            long totalBytes = -1;
            long rateKbps = -1;

            try
            {
                using (var client = new HttpClient { Timeout = budget })
                {
                    using (var response = await client
                        .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                        .ConfigureAwait(false))
                    {
                        response.EnsureSuccessStatusCode();
                        if (response.Content.Headers.ContentLength.HasValue)
                        {
                            totalBytes = response.Content.Headers.ContentLength.Value;
                        }
                        using (var source = await response.Content
                            .ReadAsStreamAsync().ConfigureAwait(false))
                        {
                            using (var output = File.Create(temp))
                            {
                                received = await CopyWithWatchdogAsync(
                                    source, output, fileName, totalBytes, budget, stopwatch, cancellationToken)
                                    .ConfigureAwait(false);
                                output.Flush();
                            }
                        }
                    }
                }

                stopwatch.Stop();
                rateKbps = ComputeRateKbps(received, stopwatch.Elapsed);

                if (expected != null)
                {
                    string actual = Sha256File(temp);
                    if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InstallerException(string.Format(
                            "SHA256 不匹配（期望 {0}，实际 {1}）", expected, actual));
                    }
                }

                string final = Path.Combine(keyDir, fileName);
                if (File.Exists(final))
                {
                    File.Delete(final);
                }
                File.Move(temp, final);
                placed = true;
            }
            finally
            {
                if (!placed && File.Exists(temp))
                {
                    try { File.Delete(temp); }
                    catch { }
                }
            }
            return rateKbps;
        }

        /// <summary>
        /// E12-③：分块拷贝 + 无进展看门狗 + 总预算 + 进度日志。返回收到的字节数。
        /// 任一次读在 NoProgressTimeout 内没完成（连接僵死/镜像假活）或总耗时超预算，
        /// 立刻抛异常，让上层换下一个源，而不是无限等。
        /// </summary>
        private async Task<long> CopyWithWatchdogAsync(
            Stream source, Stream output, string label, long totalBytes, TimeSpan budget,
            System.Diagnostics.Stopwatch stopwatch, CancellationToken cancellationToken)
        {
            var buffer = new byte[1024 * 1024];
            long received = 0;
            DateTime lastLogUtc = DateTime.UtcNow;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (stopwatch.Elapsed > budget)
                {
                    throw new TimeoutException(string.Format(
                        "{0} 下载超预算（{1} 分钟，已收 {2}）",
                        label, (int)budget.TotalMinutes, FormatMegabytes(received)));
                }

                Task<int> readTask = source.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                Task finished = await Task.WhenAny(readTask, Task.Delay(NoProgressTimeout, cancellationToken))
                    .ConfigureAwait(false);
                if (finished != readTask)
                {
                    _logger.Warn(string.Format(
                        "[PREFETCH] {0} 超过 {1} 秒无任何数据（已收 {2}{3}），判定连接僵死，放弃该源。",
                        label, (int)NoProgressTimeout.TotalSeconds, FormatMegabytes(received),
                        totalBytes > 0 ? " / " + FormatMegabytes(totalBytes) : string.Empty));
                    throw new TimeoutException(string.Format("{0} 下载无进展超过 {1} 秒",
                        label, (int)NoProgressTimeout.TotalSeconds));
                }

                int read = await readTask.ConfigureAwait(false);
                if (read <= 0)
                {
                    break;
                }
                await output.WriteAsync(buffer, 0, read, cancellationToken).ConfigureAwait(false);
                received += read;

                if ((DateTime.UtcNow - lastLogUtc) >= ProgressLogInterval)
                {
                    lastLogUtc = DateTime.UtcNow;
                    long kbps = ComputeRateKbps(received, stopwatch.Elapsed);
                    _logger.Info(string.Format("[PREFETCH] {0} 下载中 {1}{2}  平均 {3} KB/s",
                        label, FormatMegabytes(received),
                        totalBytes > 0 ? " / " + FormatMegabytes(totalBytes)
                            + "（" + (received * 100 / Math.Max(totalBytes, 1)) + "%）" : string.Empty,
                        kbps));
                }
            }
            return received;
        }

        private static long ComputeRateKbps(long bytes, TimeSpan elapsed)
        {
            if (bytes <= 0 || elapsed.TotalSeconds <= 0.5)
            {
                return -1;
            }
            return (long)(bytes / 1024.0 / elapsed.TotalSeconds);
        }

        private static string FormatMegabytes(long bytes)
        {
            return (bytes / 1024.0 / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " MB";
        }

        /// <summary>
        /// 期望 sha256：1) 与本机 node_modules\electron\package.json version 一致时优先读
        /// 该包自带的 checksums.json；2) 否则从 npmmirror 拉 SHASUMS256.txt。都失败返回 null。
        /// </summary>
        private async Task<string> ResolveExpectedHashAsync(
            string version, string fileName, CancellationToken cancellationToken)
        {
            string installedVersion = FirstQuotedValue(
                TryReadFile(Path.Combine(HermesPaths.InstallDir, "node_modules", "electron", "package.json")) ?? string.Empty,
                "version", null);
            if (!string.IsNullOrEmpty(installedVersion) &&
                string.Equals(installedVersion.TrimStart('v', 'V'), version.TrimStart('v', 'V'), StringComparison.OrdinalIgnoreCase))
            {
                string bundled = MapChecksumsJson(
                    Path.Combine(HermesPaths.InstallDir, "node_modules", "electron", "checksums.json"), fileName);
                if (bundled != null)
                {
                    _logger.Info("[PREFETCH] 校验源: 本机 electron 包 checksums.json");
                    return bundled;
                }
            }

            try
            {
                string shasums = await GetStringAsync(
                    EnsureTrailingSlash(Mirrors.ElectronMirror) + "v" + version + "/SHASUMS256.txt",
                    cancellationToken).ConfigureAwait(false);
                string mapped = ParseShasumsLine(shasums, fileName);
                if (mapped != null)
                {
                    _logger.Info("[PREFETCH] 校验源: 镜像 SHASUMS256.txt");
                }
                return mapped;
            }
            catch (Exception ex)
            {
                _logger.Info("[PREFETCH] SHASUMS256.txt 获取失败，按「未校验」降级: " + ex.Message);
            }
            return null;
        }

        private async Task<string> GetStringAsync(string url, CancellationToken cancellationToken)
        {
            using (var client = new HttpClient { Timeout = DownloadTimeout })
            {
                return await client.GetStringAsync(url).ConfigureAwait(false);
            }
        }

        /// <summary>从 checksums.json 取对应文件的期望 sha256；读不到或没该条目返回 null。</summary>
        private static string MapChecksumsJson(string path, string fileName)
        {
            try
            {
                string text = TryReadFile(path);
                if (text == null)
                {
                    return null;
                }
                var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                var dict = serializer.Deserialize<Dictionary<string, object>>(text);
                if (dict != null && dict.ContainsKey(fileName) && dict[fileName] != null)
                {
                    return Convert.ToString(dict[fileName]);
                }
            }
            catch
            {
                // 返回 null，上层会退回 SHASUMS 校验。
            }
            return null;
        }

        /// <summary>SHASUMS256.txt 行格式：&lt;hash&gt;  &lt;文件名&gt;。命中返回 hash，否则 null。</summary>
        private static string ParseShasumsLine(string shasums, string fileName)
        {
            if (string.IsNullOrEmpty(shasums))
            {
                return null;
            }
            foreach (string rawLine in shasums.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string line = rawLine.Trim();
                int split = line.IndexOf(' ');
                if (split <= 0)
                {
                    continue;
                }
                string rest = line.Substring(split).TrimStart(' ', '*');
                if (string.Equals(rest, fileName, StringComparison.OrdinalIgnoreCase))
                {
                    return line.Substring(0, split);
                }
            }
            return null;
        }

        // ------------------------------------------------------------- 本地工具

        private static string Sha256File(string path)
        {
            using (var sha256 = SHA256.Create())
            {
                using (var stream = File.OpenRead(path))
                {
                    return Sha256Hex(sha256.ComputeHash(stream));
                }
            }
        }

        private static string Sha256Hex(byte[] bytes)
        {
            var text = new StringBuilder(bytes.Length * 2);
            for (int i = 0; i < bytes.Length; i++)
            {
                text.Append(bytes[i].ToString("x2", CultureInfo.InvariantCulture));
            }
            return text.ToString();
        }

        private static string TryReadFile(string path)
        {
            try
            {
                return File.Exists(path) ? File.ReadAllText(path) : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>目标 artifact 文件名（platform/arch 先看 env 覆盖，再按本机默认）。</summary>
        private static string ArtifactFileName(string version)
        {
            string platform = FirstNonEmpty(
                SafeEnv("ELECTRON_INSTALL_PLATFORM"),
                SafeEnv("npm_config_platform"),
                "win32");
            string arch = FirstNonEmpty(
                SafeEnv("ELECTRON_INSTALL_ARCH"),
                SafeEnv("npm_config_arch"),
                HostArch());
            return "electron-v" + version.TrimStart('v', 'V') + "-" + platform + "-" + arch + ".zip";
        }

        private static string HostArch()
        {
            if (string.Equals(SafeEnv("PROCESSOR_ARCHITECTURE"), "ARM64", StringComparison.OrdinalIgnoreCase))
            {
                return "arm64";
            }
            return Environment.Is64BitOperatingSystem ? "x64" : "x86";
        }

        private static string SafeEnv(string name)
        {
            try { return Environment.GetEnvironmentVariable(name); }
            catch { return null; }
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (string value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }
            return null;
        }

        /// <summary>@electron/get：electron_config_cache / ELECTRON_CACHE 优先，默认 envPaths('electron').cache。</summary>
        private static string ResolveCacheRoot()
        {
            string custom = FirstNonEmpty(SafeEnv("electron_config_cache"), SafeEnv("ELECTRON_CACHE"));
            if (custom != null)
            {
                return custom;
            }
            // E10-③（2026-10-25 考古实锤）：此前用 HermesPaths.LocalAppDataRoot（= 盘根，
            // 如 C:\），@electron/get 实际默认根是 %LOCALAPPDATA%\electron\Cache
            // （本机上 `%LOCALAPPDATA%\electron\Cache\<key>` 7:45 已有官方基址键的 158MB zip、
            // 08:14 已有 npmmirror 键），预热一直写到 C:\electron\Cache 的"平行宇宙"里
            // 从未被命中；换根后 dedup 命中率提升且不再产生多余 158MB 副本。
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(localAppData, "electron", "Cache");
        }

        /// <summary>升级子进程会用的 ELECTRON_MIRROR（与 NetworkPreparer 同路解析）。</summary>
        private string ResolveElectronBase()
        {
            string resolved = null;
            if (_network != null)
            {
                try { resolved = _network.ResolveElectronBase(); }
                catch (Exception ex) { _logger.Warn("[PREFETCH] 解析 ELECTRON_MIRROR 失败，按 GitHub 官方处理: " + ex.Message); }
            }
            return EnsureTrailingSlash(string.IsNullOrWhiteSpace(resolved) ? GithubElectronBase : resolved);
        }

        private static string EnsureTrailingSlash(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                return GithubElectronBase;
            }
            return url.EndsWith("/", StringComparison.Ordinal) ? url : url + "/";
        }

        private static void AddSource(List<string> sources, string candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return;
            }
            string normalized = EnsureTrailingSlash(candidate.Trim());
            foreach (string seen in sources)
            {
                if (string.Equals(seen, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }
            sources.Add(normalized);
        }

        private static void AddVersion(List<string> versions, string candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate) || !LooksLikeVersion(candidate))
            {
                return;
            }
            string normalized = candidate.Trim().TrimStart('v', 'V');
            foreach (string seen in versions)
            {
                if (string.Equals(seen, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }
            versions.Add(normalized);
        }

        private static bool LooksLikeVersion(string text)
        {
            string v = text.Trim().TrimStart('v', 'V');
            string head = v.Split('.')[0];
            return head.Length > 0 && char.IsDigit(head[0]);
        }

        private static string DescribeSource(string url)
        {
            if (url == null)
            {
                return string.Empty;
            }
            if (url.IndexOf("npmmirror", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "npmmirror";
            }
            if (url.IndexOf("github", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "GitHub";
            }
            return url;
        }

        /// <summary>
        /// 通用取值：在文本里找 '"<paramref name="needle"/>"'（如 '"node_modules/electron"'）
        /// 之后的首个 "<paramref name="name"/>" 字段值；needle 为 null 时从全文找首个 name 字段。
        /// npm lockfile 的条目正文先于其他字段只有 name（可缺席）与 version，2KB 窗口足够。
        /// </summary>
        private static string FirstQuotedValue(string text, string name, string needle)
        {
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }
            int at = needle == null ? 0 : text.IndexOf(needle, StringComparison.Ordinal);
            if (at < 0 || needle != null && at + needle.Length >= text.Length)
            {
                return null;
            }
            string segment = text.Substring(at, Math.Min(2048, text.Length - at));
            string quoted = "\"" + name + "\"";
            int valueAt = segment.IndexOf(quoted, StringComparison.Ordinal);
            if (valueAt < 0)
            {
                return null;
            }
            int colon = segment.IndexOf(':', valueAt + quoted.Length);
            if (colon < 0)
            {
                return null;
            }
            int value = segment.IndexOf('"', colon + 1);
            if (value < 0)
            {
                return null;
            }
            int end = segment.IndexOf('"', value + 1);
            return end > value ? segment.Substring(value + 1, end - value - 1) : null;
        }

        /// <summary>
        /// E7-2 fork 预同步：把 refs/remotes/upstream/main 推上 origin/main，命令与
        /// hermes update 的收尾推送完全一致（update_cmd_git.py `_sync_fork_with_upstream`
        /// 即 `git push origin main --force-with-lease`），并沿用其安全护栏
        /// （merge-base --is-ancestor：origin/main 是 upstream/main 的祖先才推，分叉绝不 force）。
        /// 成功后 update 内部的推送因 `HEAD == origin` 直接跳过，宿主机网络再慢也不会
        /// 再触发 180s 无进展预算。失败只落日志（= update 收尾时的原有兜底行为）。
        /// </summary>
        private async Task<string> PreflightForkSyncAsync(CancellationToken cancellationToken)
        {
            string repo = HermesPaths.InstallDir;
            if (_runner == null || !Directory.Exists(Path.Combine(repo, ".git")))
            {
                return null;
            }
            try
            {
                string upstream = await GitStdOutAsync(
                    repo, " rev-parse -q --verify refs/remotes/upstream/main^{commit}", cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrEmpty(upstream))
                {
                    _logger.Info("[PREFETCH-FORK] 无 upstream/main 引用（fetch 失败或无 upstream 远端），跳过预同步。");
                    return null;
                }
                string origin = await GitStdOutAsync(
                    repo, " rev-parse -q --verify refs/remotes/origin/main^{commit}", cancellationToken).ConfigureAwait(false);
                if (string.Equals(origin, upstream, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.Info("[PREFETCH-FORK] origin/main 已与 upstream/main 一致，无需预同步。");
                    return null;
                }
                if (!string.IsNullOrEmpty(origin))
                {
                    ProcessResult ancestor = await _runner.RunProcessAsync(
                        "git.exe",
                        "-C " + ProcessRunner.QuoteArg(repo) +
                        " merge-base --is-ancestor refs/remotes/origin/main refs/remotes/upstream/main",
                        repo, null, null, cancellationToken, null, RetryPolicy.For(OperationClass.GitFetch)).ConfigureAwait(false);
                    if (ancestor.ExitCode != 0)
                    {
                        // 与 hermes 同口径：分叉时不 pre-force，让 update 自行处理。
                        _logger.Info("[PREFETCH-FORK] origin/main 与 upstream/main 分叉（非祖先），跳过预同步。");
                        return null;
                    }
                }

                _logger.Info(string.Format("[PREFETCH-FORK] 预推送 origin/main {0} → {1}（进度流式记入日志）…",
                    origin != null ? origin.Substring(0, Math.Min(10, origin.Length)) : "nil", upstream.Substring(0, Math.Min(10, upstream.Length))));

                // 防挂死（E8-3，2026-10-25 事故：update 内部的同名推送被 credential.manager
                // 的凭证等待挂住 3+ 分钟）：禁终端/askpass 提示 + 低速率中止，让该推送
                // 以"GCM 默默无凭证 → Repository Not Found → 快速失败"预期路径收场
                // （推送失败对 update 是非致命旁路，hermes 自行兜底）。
                var pushEnv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                pushEnv["GIT_TERMINAL_PROMPT"] = "0";
                pushEnv["GCM_INTERACTIVE"] = "never";
                pushEnv["GIT_ASKPASS"] = "echo";
                pushEnv["GIT_HTTP_LOW_SPEED_LIMIT"] = "1000";
                pushEnv["GIT_HTTP_LOW_SPEED_TIME"] = "30";

                var pushOptions = new ProcessRunOptions
                {
                    Class = OperationClass.GitFetch,
                    NoProgressTimeout = TimeSpan.FromMinutes(2),
                    TotalTimeout = TimeSpan.FromMinutes(10),
                    PollIntervalMs = 1000,
                };
                ProcessResult push = await _runner.RunProcessAsync(
                    "git.exe",
                    "-C " + ProcessRunner.QuoteArg(repo) +
                    " push origin refs/remotes/upstream/main:main --force-with-lease",
                    repo, pushEnv,
                    (line, level) => _logger.Write(level, "[PREFETCH-FORK] " + line),
                    cancellationToken, null, pushOptions).ConfigureAwait(false);
                if (push.Cancelled)
                {
                    throw new OperationCanceledException(cancellationToken);
                }
                if (push.ExitCode != 0)
                {
                    _logger.Warn(string.Format("[PREFETCH-FORK] fork 预同步失败（退出码 {0}），update 收尾推送保持原有兜底行为: {1}",
                        push.ExitCode, TruncateText(push.StandardError, 200)));
                    return null;
                }
                _logger.Success("[PREFETCH-FORK] fork 预同步完成，hermes update 的 Syncing fork 步骤将直接跳过。");
                return "fork 已预同步";
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.Warn("[PREFETCH-FORK] fork 预同步异常（不阻塞升级）: " + ex.Message);
                return null;
            }
        }

        /// <summary>跑一条只取 stdout 的 git 命令；非零退出或空输出返回 null。</summary>
        private async Task<string> GitStdOutAsync(string repo, string args, CancellationToken cancellationToken)
        {
            // 2026-10-25 10:35 GUI 冒烟事故（E9）：QuoteArg(repo) 与 args.Trim() 粘连
            // （少一个空格），`-C "路径"rev-parse …` 整条命令必然失败，造成
            // 「无 upstream/main 引用 → 跳过预同步」的假日志。upstream 明明存在。
            ProcessResult result = await _runner.RunProcessAsync(
                "git.exe",
                "-C " + ProcessRunner.QuoteArg(repo) + " " + args.Trim(),
                repo, null, null, cancellationToken, null, RetryPolicy.For(OperationClass.GitFetch)).ConfigureAwait(false);
            return result.ExitCode == 0 && !string.IsNullOrEmpty(result.StandardOutput)
                ? result.StandardOutput.Trim()
                : null;
        }

        private static string TruncateText(string text, int max)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= max)
            {
                return text;
            }
            return text.Substring(0, max) + "…";
        }

        /// <summary>
        /// 预读升级目标版本：git fetch upstream（失败回退 origin）→ git show &lt;remote&gt;/main:package-lock.json。
        /// 升级后的代码 = upstream 分支 fast-forward，所以目标 electron 版本取自 upstream/main。
        /// 全部失败返回 null（只能预热本机当前版本）。
        /// </summary>
        private async Task<string> ProbeUpcomingElectronVersionAsync(CancellationToken cancellationToken)
        {
            string repo = HermesPaths.InstallDir;
            if (!Directory.Exists(Path.Combine(repo, ".git")))
            {
                return null;
            }
            var options = RetryPolicy.For(OperationClass.GitFetch);
            foreach (string remote in new[] { "upstream", "origin" })
            {
                try
                {
                    ProcessResult fetch = await _runner.RunProcessAsync(
                        "git.exe",
                        "-C " + ProcessRunner.QuoteArg(repo) + " fetch --quiet --no-tags " + remote + " main",
                        repo, null, null, cancellationToken, null, options).ConfigureAwait(false);
                    if (fetch.ExitCode != 0)
                    {
                        _logger.Info(string.Format("[PREFETCH] git fetch {0} main 失败（退出码 {1}），换下一个来源。",
                            remote, fetch.ExitCode));
                        continue;
                    }

                    ProcessResult show = await _runner.RunProcessAsync(
                        "git.exe",
                        "-C " + ProcessRunner.QuoteArg(repo) + " show " + remote + "/main:package-lock.json",
                        repo, null, null, cancellationToken, null, options).ConfigureAwait(false);
                    if (show.ExitCode != 0 || string.IsNullOrEmpty(show.StandardOutput))
                    {
                        continue;
                    }
                    string version = FirstQuotedValue(show.StandardOutput, "version", "\"node_modules/electron\"");
                    if (LooksLikeVersion(version ?? string.Empty))
                    {
                        _logger.Info(string.Format("[PREFETCH] 远端 {0}/main 将带入的 Electron 版本: {1}", remote, version));
                        return version;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.Info("[PREFETCH] git 探测远端版本失败: " + ex.Message);
                }
            }
            return null;
        }
    }
}
