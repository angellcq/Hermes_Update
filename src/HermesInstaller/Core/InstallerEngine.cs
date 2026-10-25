using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace HermesInstaller.Core
{
    public sealed class InstallerException : Exception
    {
        public InstallerException(string message) : base(message) { }
    }

    /// <summary>
    /// Drives the official install.ps1 stage protocol and the installed CLI.
    /// All network work goes through the mirror environment built by NetworkPreparer.
    /// </summary>
    public sealed class InstallerEngine
    {
        private readonly Settings _settings;
        private readonly Logger _logger;
        private readonly ProcessRunner _runner;
        private readonly NetworkPreparer _network;
        private readonly ResultVerifier _verifier;

        /// <summary>E10-①：本轮 update 输出的 follow-up 欠账标记（"Update follow-up '…' did not finish…"），
        /// 供 A5 断言可见化。跨两轮 update（正常 + D-16 补偿复跑）累计；VerifyAsync 后不再保留。</summary>
        private readonly List<string> _owedMarkers = new List<string>();

        /// <summary>欠账标记判据：update 输出里的 follow-up 未完成话术（hermes update_cmd follow-up 机制）。</summary>
        private static bool IsOwedMarker(string line)
        {
            if (string.IsNullOrEmpty(line))
            {
                return false;
            }
            return line.IndexOf("Update follow-up '", StringComparison.OrdinalIgnoreCase) >= 0
                && line.IndexOf("did not finish", StringComparison.OrdinalIgnoreCase) >= 0;
        }
        private readonly ServiceStateGuard _serviceGuard;
        private readonly RunStateStore _runState;

        public InstallerEngine(Settings settings, Logger logger)
        {
            _settings = settings ?? new Settings();
            _logger = logger;
            _runner = new ProcessRunner();
            _network = new NetworkPreparer(_settings, logger);
            _verifier = new ResultVerifier(logger, this);
            _serviceGuard = new ServiceStateGuard(logger, _runner);
            _runState = new RunStateStore(logger);
        }

        public string InstallerScriptPath { get { return HermesPaths.CachedInstallerScript; } }

        /// <summary>
        /// G6（T-HU-DEV-4）：沿用既有的「阶段名」口径写 run.json 的 stageName。
        /// RunOperationAsync 的 Report(progress,...) 已带标题；这里只做状态登记的镜像。
        /// </summary>
        public void ReportRunStage(string stageName)
        {
            try { _runState.UpdateStage(stageName); }
            catch (Exception ex) { _logger.Warn("[STATE] 阶段状态登记失败: " + ex.Message); }
        }

        /// <summary>
        /// G6（T-HU-DEV-4）：静态清理收口。MainForm 侧无论引擎是否被构造都强制清，
        /// 兜住 Engine 有崩溃未执行自身 finally 的情形。文件不存在则无事发生，异常按 Warn 落日志、不抛。
        /// </summary>
        public static void ClearRunState()
        {
            try
            {
                if (File.Exists(HermesPaths.RunStatePath))
                {
                    File.Delete(HermesPaths.RunStatePath);
                }
            }
            catch (Exception ex)
            {
                WarnStatic("[STATE] 清理 run.json 失败: " + ex.Message);
            }
        }

        /// <summary>静态路径（ClearRunState / ReadOtherStaleTolerant）的日志出口（R-03 不静默红线）。</summary>
        private static void WarnStatic(string message)
        {
            try
            {
                using (var logger = new Logger())
                {
                    logger.Warn(message);
                }
            }
            catch
            {
                // 日志器本身不可用时只能放弃，不影响调用方主流程。
            }
        }

        /// <summary>Downloads the official script and returns its stage manifest (used by self-test).</summary>
        public async Task<InstallManifest> ProbeManifestAsync(CancellationToken cancellationToken)
        {
            await DownloadInstallerScriptAsync(cancellationToken).ConfigureAwait(false);
            return await GetManifestAsync(true, cancellationToken).ConfigureAwait(false);
        }

        // ---------------------------------------------------------------- install

        public async Task<OperationResult> RunInstallAsync(
            bool includeDesktop,
            OperationKind kind,
            IProgress<ProgressInfo> progress,
            CancellationToken cancellationToken)
        {
            try
            {
                DateTime startedAt = DateTime.Now;
                try { _runState.Begin("安装"); }
                catch (Exception ex) { _logger.Warn("[STATE] run.json 初始化失败: " + ex.Message); }
                try { _runState.UpdateStage("获取安装脚本"); }
                catch (Exception ex) { _logger.Warn("[STATE] 阶段状态登记失败: " + ex.Message); }

                Report(progress, 0, 1, "准备", "正在获取官方安装脚本…", false);
                await DownloadInstallerScriptAsync(cancellationToken).ConfigureAwait(false);

                // A2 前值预检（冻结口径，DD 偏差 D-4）：在阶段执行（可能重装 python）之前采样。
                bool? preCheckOk = await _verifier.PreCheckDependenciesAsync(cancellationToken).ConfigureAwait(false);

                _logger.Stage("读取安装阶段清单…");
                InstallManifest manifest = await GetManifestAsync(includeDesktop, cancellationToken).ConfigureAwait(false);
                if (manifest.Stages.Count == 0)
                {
                    throw new InstallerException("安装脚本没有返回任何阶段。");
                }

                int total = manifest.Stages.Count;
                for (int i = 0; i < manifest.Stages.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ManifestStage stage = manifest.Stages[i];
                    Report(progress, i + 1, total, stage.Title, "正在执行: " + stage.Title, true);
                    _logger.Stage(string.Format("[{0}/{1}] {2}", i + 1, total, stage.Title));

                    // G6（T-HU-DEV-4）：阶段切换即刷新 run.json 的 stageName（顺带刷新心跳）。
                    try { _runState.UpdateStage(stage.Title); }
                    catch (Exception ex) { _logger.Warn("[STATE] 阶段状态登记失败: " + ex.Message); }

                    StageResult result = await RunStageAsync(stage.Name, includeDesktop, cancellationToken).ConfigureAwait(false);
                    if (!result.Ok)
                    {
                        string reason = string.IsNullOrWhiteSpace(result.Reason) ? "未知错误" : result.Reason;
                        throw new InstallerException(string.Format("阶段「{0}」失败: {1}", stage.Title, reason));
                    }

                    Report(progress, i + 1, total, stage.Title,
                        result.Skipped ? "已跳过: " + stage.Title : "完成: " + stage.Title, false);
                }

                VerificationReport report = await _verifier.VerifyAsync(
                    includeDesktop, null, null, startedAt, cancellationToken, preCheckOk).ConfigureAwait(false);
                Report(progress, 1, 1, "自检", report.Summary, false);

                return ConcludeResult(report, "安装", _logger.LogPath);
            }
            catch (OperationCanceledException)
            {
                _logger.Warn("操作已取消。");
                return OperationResult.Error("操作已取消。", _logger.LogPath);
            }
            catch (Exception ex)
            {
                _logger.Error(ex.Message);
                return OperationResult.Error(ex.Message, _logger.LogPath);
            }
            finally
            {
                _runState.Clear();
                AppendHermesLogs();
            }
        }

        private async Task DownloadInstallerScriptAsync(CancellationToken cancellationToken)
        {
            HermesPaths.EnsureAppData();
            string target = HermesPaths.CachedInstallerScript;
            Exception lastError = null;

            _logger.Info("下载官方安装脚本: " + Mirrors.OfficialInstaller);
            try
            {
                using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(90) })
                {
                    byte[] bytes = await client.GetByteArrayAsync(Mirrors.OfficialInstaller).ConfigureAwait(false);
                    if (bytes == null || bytes.Length < 10000)
                    {
                        throw new InstallerException("下载到的 install.ps1 内容异常。");
                    }
                    File.WriteAllBytes(target, bytes);
                    _logger.Success(string.Format("安装脚本已保存（{0:0.0} KB）。", bytes.Length / 1024.0));
                    return;
                }
            }
            catch (Exception ex)
            {
                lastError = ex;
                _logger.Warn("在线获取安装脚本失败: " + ex.Message);
            }

            if (File.Exists(target))
            {
                _logger.Warn("使用缓存的安装脚本: " + target);
                return;
            }

            throw new InstallerException("无法获取官方安装脚本，且没有缓存副本。" +
                (lastError != null ? " (" + lastError.Message + ")" : string.Empty));
        }

        private async Task<InstallManifest> GetManifestAsync(bool includeDesktop, CancellationToken cancellationToken)
        {
            var env = _network.BuildEnvironment();
            string invocation = "& " + ProcessRunner.Quote(InstallerScriptPath) + " -Manifest" +
                                (includeDesktop ? " -IncludeDesktop" : string.Empty);

            ProcessResult result = await _runner.RunPowerShellAsync(
                invocation,
                HermesPaths.HermesHome,
                env,
                (line, level) => { if (level == LogLevel.Error) _logger.Error(line); },
                cancellationToken).ConfigureAwait(false);

            string json = ExtractJsonLine(result.StandardOutput, "\"stages\"");
            if (json == null)
            {
                throw new InstallerException("安装脚本未返回阶段清单。输出: " + Truncate(result.StandardOutput, 500));
            }

            var manifest = new InstallManifest();
            var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            var root = serializer.Deserialize<Dictionary<string, object>>(json);
            if (root == null || !root.ContainsKey("stages"))
            {
                throw new InstallerException("阶段清单格式无法识别。");
            }

            if (root.ContainsKey("protocol_version"))
            {
                manifest.ProtocolVersion = Convert.ToInt32(root["protocol_version"]);
            }

            var stages = root["stages"] as ArrayList;
            if (stages != null)
            {
                foreach (object entry in stages)
                {
                    var dict = entry as Dictionary<string, object>;
                    if (dict == null)
                    {
                        continue;
                    }
                    manifest.Stages.Add(new ManifestStage
                    {
                        Name = GetString(dict, "name"),
                        Title = GetString(dict, "title"),
                        Category = GetString(dict, "category"),
                        NeedsUserInput = GetBool(dict, "needs_user_input")
                    });
                }
            }

            _logger.Info(string.Format("安装阶段: {0}", string.Join(" → ",
                manifest.Stages.ConvertAll(s => s.Title).ToArray())));
            return manifest;
        }

        private async Task<StageResult> RunStageAsync(string stage, bool includeDesktop, CancellationToken cancellationToken)
        {
            var env = _network.BuildEnvironment();
            string channel = string.IsNullOrWhiteSpace(_settings.Channel) ? "stable" : _settings.Channel;
            string invocation = "& " + ProcessRunner.Quote(InstallerScriptPath) +
                                " -Stage " + ProcessRunner.Quote(stage) +
                                " -NonInteractive -Json -Channel " + ProcessRunner.Quote(channel) +
                                (includeDesktop ? " -IncludeDesktop" : string.Empty) +
                                (_settings.SkipBrowser ? " -SkipBrowser" : string.Empty) +
                                (_settings.SkipComputerUse ? " -SkipComputerUse" : string.Empty);

            ProcessResult result = await _runner.RunPowerShellAsync(
                invocation,
                HermesPaths.HermesHome,
                env,
                (line, level) => _logger.Write(level, line),
                cancellationToken).ConfigureAwait(false);

            if (result.Cancelled)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            string json = ExtractLastJsonLine(result.StandardOutput, "\"ok\"");
            if (json != null)
            {
                try
                {
                    var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                    var dict = serializer.Deserialize<Dictionary<string, object>>(json);
                    if (dict != null)
                    {
                        return new StageResult
                        {
                            Ok = GetBool(dict, "ok"),
                            Skipped = GetBool(dict, "skipped"),
                            Reason = GetString(dict, "reason")
                        };
                    }
                }
                catch
                {
                    // Fall through to the exit code.
                }
            }

            return new StageResult
            {
                Ok = result.ExitCode == 0,
                Skipped = false,
                Reason = result.ExitCode == 0 ? null : "退出码 " + result.ExitCode + " " + Truncate(result.StandardError, 200)
            };
        }

        // ----------------------------------------------------------------- update

        public async Task<OperationResult> RunUpgradeAsync(
            bool includeDesktop,
            IProgress<ProgressInfo> progress,
            CancellationToken cancellationToken)
        {
            try
            {
                DateTime startedAt = DateTime.Now;
                try { _runState.Begin("升级"); }
                catch (Exception ex) { _logger.Warn("[STATE] run.json 初始化失败: " + ex.Message); }
                if (!HermesPaths.CliLauncherPresent)
                {
                    throw new InstallerException("未检测到 Hermes CLI，请先执行「新装」。");
                }

                // E7 依赖预热：把本轮 hermes update 将用到的 Electron 运行时 zip 提前
                // 按「升级子进程将使用的 ELECTRON_MIRROR」对应的缓存键放进 @electron/get 缓存。
                // best-effort：任何失败只落日志，不阻塞升级（update 中途仍会按原路径重试）。
                try { _runState.UpdateStage("依赖预热"); }
                catch (Exception ex) { _logger.Warn("[STATE] 阶段状态登记失败: " + ex.Message); }
                Report(progress, 0, 1, "升级", "预热升级依赖（Electron 运行时缓存）…", true);
                _logger.Stage("预热升级依赖（Electron 运行时缓存）…");
                string prefetchNote = await new DependencyPrefetcher(_logger, _network, _runner)
                    .WarmAsync(cancellationToken).ConfigureAwait(false);
                _logger.Info("[PREFETCH] 预热摘要: " + prefetchNote);

                _logger.Stage("停止运行中的 Hermes 进程…");

                // A2 前值预检（冻结口径，DD 偏差 D-4）：必须在停进程/动文件之前采样。
                bool? preCheckOk = await _verifier.PreCheckDependenciesAsync(cancellationToken).ConfigureAwait(false);

                // G5（T-HU-DEV-3）：停进程之前先把网关/桌面端状态拍成基线快照。
                // 日志证据 E2：升级停进程后网关无人拉起；基线必须在 Stop 之前采集才有意义。
                ServiceSnapshot serviceBaseline = await _serviceGuard.CaptureAsync(cancellationToken).ConfigureAwait(false);

                await StopHermesProcessesAsync(cancellationToken).ConfigureAwait(false);

                // G3（T-HU-DEV-4）：本轮操作内的防重复指纹（DD §2.2 G3，口径红线见偏差 D-1）。
                // fpBefore 在停进程后、update 前取；fpAfter 在 update 返回后取（无论是否含桌面端）；
                // IsSame 只用于本次调用内判定「update 是否产生了版本变化」，绝不跨会话使用。
                try { _runState.UpdateStage("指纹采集（升级前）"); }
                catch (Exception ex) { _logger.Warn("[STATE] 阶段状态登记失败: " + ex.Message); }
                var fingerprint = new VersionFingerprint(_logger, _runner, this);
                FingerprintRecord fpBefore = await fingerprint.CaptureAsync(cancellationToken).ConfigureAwait(false);
                fingerprint.SaveLast(fpBefore);

                try { _runState.UpdateStage("hermes update"); }
                catch (Exception ex) { _logger.Warn("[STATE] 阶段状态登记失败: " + ex.Message); }
                Report(progress, 0, 1, "升级", "正在执行 hermes update…", true);
                var env = _network.BuildEnvironment();
                string invocation = "& " + ProcessRunner.Quote(HermesPaths.HermesCmd) + " update --yes" +
                                    (_settings.BackupBeforeUpdate ? " --backup" : string.Empty);

                // E10-①：本轮开始前清欠账标记，两轮 update（含 D-16 补偿复跑）共用累计。
                _owedMarkers.Clear();

                // D-15（E8，2026-10-25 两次实测偏差）：G1 冻结表 OperationBudget 的
                // 180s 无进展预算会误杀 hermes update 处于"合法静默"的阶段——
                // ① fork 同步的静默推送（08:44 被 180s 击杀）；② source_completion
                // 的 desktop 打包（10:00 补跑里静默运行 20+ 分钟才失败/成功）。
                // update 阶段改用放宽的专用预算：无进展 15 分钟 / 总计 45 分钟。
                var updateBudget = new ProcessRunOptions
                {
                    Class = OperationClass.OperationBudget,
                    NoProgressTimeout = TimeSpan.FromMinutes(15),
                    TotalTimeout = TimeSpan.FromMinutes(45),
                    PollIntervalMs = 1000,
                };

                ProcessResult result = await _runner.RunPowerShellAsync(
                    invocation,
                    HermesPaths.HermesHome,
                    env,
                    (line, level) =>
                    {
                        if (IsOwedMarker(line))
                        {
                            _owedMarkers.Add(line);
                            _logger.Warn("[A5] 捕获 follow-up 欠账标记: " + line.Trim());
                        }
                        _logger.Write(level, line);
                    },
                    cancellationToken,
                    updateBudget).ConfigureAwait(false);

                if (result.Cancelled)
                {
                    throw new OperationCanceledException(cancellationToken);
                }
                if (result.ExitCode != 0)
                {
                    // D-16（E8，2026-10-25 实测语义）：hermes 的 update 有"欠账续作"
                    // 机制——进程退出非零但代码可能已经 fast-forward（本轮 08:44 乱局：
                    // Syncing fork 后续构建失败，HEAD 实际已到 eb1b4c5b61、网关还会被
                    // 复原；hermes 会把未完的收尾（如 desktop build）记为 owed，等下次
                    // update 自动补完）。所以退出非零 ≠ 升级失败：先采一次指纹，
                    // gitHead 已推进时补偿复跑一次 update（同 env 同预算）；仍失败才报错。
                    FingerprintRecord fpAfterFailure = await fingerprint
                        .CaptureAsync(cancellationToken).ConfigureAwait(false);
                    bool codeLanded = !string.Equals(fpBefore.GitHead, fpAfterFailure.GitHead,
                        StringComparison.OrdinalIgnoreCase);
                    if (codeLanded)
                    {
                        _logger.Warn(string.Format(
                            "[E8] update 退出码 {0}，但 gitHead 已从 {1} 推进到 {2}（hermes 记有" +
                            "欠账收尾）。补偿复跑一次 update 以完成收尾…",
                            result.ExitCode, ShortSha(fpBefore.GitHead), ShortSha(fpAfterFailure.GitHead)));
                        try { _runState.UpdateStage("欠账补偿复跑"); }
                        catch (Exception ex) { _logger.Warn("[STATE] 阶段状态登记失败: " + ex.Message); }
                        Report(progress, 0, 1, "升级", "代码已同步，补跑收尾（hermes update 重试）…", true);
                        ProcessResult second = await _runner.RunPowerShellAsync(
                            invocation,
                            HermesPaths.HermesHome,
                            env,
                            (line, level) =>
                            {
                                if (IsOwedMarker(line))
                                {
                                    _owedMarkers.Add(line);
                                    _logger.Warn("[A5] 捕获 follow-up 欠账标记（补偿复跑）: " + line.Trim());
                                }
                                _logger.Write(level, line);
                            },
                            cancellationToken,
                            updateBudget).ConfigureAwait(false);
                        if (second.Cancelled)
                        {
                            throw new OperationCanceledException(cancellationToken);
                        }
                        if (second.ExitCode == 0)
                        {
                            _logger.Info("[E8] 欠账补偿复跑成功，升级收尾完成。");
                            result = second;
                        }
                        else
                        {
                            _logger.Warn("[E8] 欠账补偿复跑仍退出 " + second.ExitCode
                                + "，按升级失败处理（hermes 记有欠账，下次升级继续补完）。");
                            throw new InstallerException("hermes update 失败（退出码 " + second.ExitCode
                                + "，含一次欠账补偿复跑）。" + Truncate(second.StandardError, 300));
                        }
                    }
                    else
                    {
                        throw new InstallerException("hermes update 失败（退出码 " + result.ExitCode + "）。" +
                            Truncate(result.StandardError, 300));
                    }
                }

                // G3 防重复判定（同一次操作内）：该判定不落盘、不保留到下一轮——
                // LoadLast 的结果在这里不会被用来提前 return，升级命令每轮都实际执行（D-1）。
                FingerprintRecord fpAfter = await fingerprint.CaptureAsync(cancellationToken).ConfigureAwait(false);
                fingerprint.SaveLast(fpAfter);
                bool versionUnchanged = VersionFingerprint.IsSame(fpBefore, fpAfter);
                _logger.Info("[FP] 指纹判等结论: unchanged=" + versionUnchanged
                    + "（IsSame 仅限本轮操作内比较，跨会话不做指纹跳过——DD 偏差 D-1）");
                _logger.Info(UiTextBuilder.FingerprintChangedText(versionUnchanged));

                // E11（2026-10-25 13:01 桌面端升级误报）：重建条件不再绑「版本有无变化」，
                // 改为「产物是否落后于当前代码」：
                //   • 产物缺失 / mtime 早于 HEAD 提交时间 → 代码比产物新，必须重建
                //     （用户点「桌面端升级」就是要产物追上代码；此前 !versionUnchanged 的闸门
                //      让「代码已最新但产物没跟上」的场景永远重建不了，A3 还反过来判失败）；
                //   • 产物晚于 HEAD 提交时间 → 已经覆盖当前代码，天然防重复
                //     （update 内部刚构建过时 mtime 必然是新的，无需再补一枪 build-only）。
                // 拿不到 HEAD 时间时退回旧判据（本轮 mtime），保证不误判、不阻塞。
                DateTime? codeSyncTime = await GetHeadCommitTimeAsync(cancellationToken).ConfigureAwait(false);
                bool desktopStale = !HermesPaths.DesktopBuilt
                    || (codeSyncTime.HasValue
                        ? !IsDesktopArtifactFreshRelativeTo(codeSyncTime.Value)
                        : !IsDesktopArtifactFresh(startedAt));
                if (includeDesktop && desktopStale)
                {
                    _logger.Warn(codeSyncTime.HasValue
                        ? "桌面端产物早于当前代码（或缺失），单独重建…"
                        : "未找到桌面端产物（或产物早于本轮开始时间），单独重建…");
                    try { _runState.UpdateStage("桌面端重建"); }
                    catch (Exception ex) { _logger.Warn("[STATE] 阶段状态登记失败: " + ex.Message); }
                    Report(progress, 0, 1, "桌面端", "正在重建桌面端…", true);
                    ProcessResult build = await _runner.RunPowerShellAsync(
                        "& " + ProcessRunner.Quote(HermesPaths.HermesCmd) + " desktop --build-only --force-build",
                        HermesPaths.HermesHome,
                        env,
                        (line, level) => _logger.Write(level, line),
                        cancellationToken).ConfigureAwait(false);
                    if (build.ExitCode != 0 || !HermesPaths.DesktopBuilt)
                    {
                        throw new InstallerException("桌面端重建失败。请查看日志。");
                    }
                }
                else if (includeDesktop)
                {
                    _logger.Info("[G3] 桌面端产物已覆盖当前代码（mtime " +
                        (HermesPaths.DesktopBuilt ? File.GetLastWriteTime(HermesPaths.DesktopExe).ToString("yyyy-MM-dd HH:mm:ss") : "(缺失)")
                        + "），本轮不重复重建。");
                }

                // G5 流程 ⑨：升级收尾按冻结顺序复原网关（ensure → start → 失败降级），
                // 双起防护保证网关已在跑时不再发起任何启动命令。
                try { _runState.UpdateStage("网关复原"); }
                catch (Exception ex) { _logger.Warn("[STATE] 阶段状态登记失败: " + ex.Message); }
                ServiceRestoreResult restore = await _serviceGuard.RestoreAsync(
                    serviceBaseline, cancellationToken).ConfigureAwait(false);
                _logger.Info("[GATEWAY] 复原结论：Restored=" + restore.Restored
                    + "，Detail=" + restore.Detail
                    + "，PidBefore=" + restore.PidBefore + "，PidAfter=" + restore.PidAfter);

                // G5 流程 ⑩（A4 联动）：真实 baseline/restore 传入，A4 不再 Skipped。
                // E10-①：update 输出聚合的 follow-up 欠账标记传给 A5（install/repair 传 null → Skipped）。
                VerificationReport report = await _verifier.VerifyAsync(
                    includeDesktop, serviceBaseline, restore,
                    startedAt, cancellationToken, preCheckOk,
                    new List<string>(_owedMarkers)).ConfigureAwait(false);
                Report(progress, 1, 1, "升级", report.Summary, false);

                return ConcludeResult(report, "升级", _logger.LogPath);
            }
            catch (OperationCanceledException)
            {
                _logger.Warn("操作已取消。");
                return OperationResult.Error("操作已取消。", _logger.LogPath);
            }
            catch (Exception ex)
            {
                _logger.Error(ex.Message);
                return OperationResult.Error(ex.Message, _logger.LogPath);
            }
            finally
            {
                _runState.Clear();
                AppendHermesLogs();
            }
        }

        public async Task<OperationResult> RunUninstallAsync(
            IProgress<ProgressInfo> progress,
            CancellationToken cancellationToken)
        {
            try
            {
                if (!HermesPaths.CliLauncherPresent)
                {
                    throw new InstallerException("未检测到 Hermes CLI。");
                }
                await StopHermesProcessesAsync(cancellationToken).ConfigureAwait(false);
                try { _runState.Begin("卸载"); }
                catch (Exception ex) { _logger.Warn("[STATE] run.json 初始化失败: " + ex.Message); }
                try { _runState.UpdateStage("正在卸载"); }
                catch (Exception ex) { _logger.Warn("[STATE] 阶段状态登记失败: " + ex.Message); }
                Report(progress, 0, 1, "卸载", "正在卸载（保留用户数据）…", true);

                ProcessResult result = await _runner.RunPowerShellAsync(
                    "& " + ProcessRunner.Quote(HermesPaths.HermesCmd) + " uninstall --yes",
                    HermesPaths.HermesHome,
                    _network.BuildEnvironment(),
                    (line, level) => _logger.Write(level, line),
                    cancellationToken).ConfigureAwait(false);

                if (result.ExitCode != 0)
                {
                    throw new InstallerException("卸载失败（退出码 " + result.ExitCode + "）。");
                }
                Report(progress, 1, 1, "卸载", "卸载完成", false);
                return OperationResult.Ok("卸载完成（用户数据已保留）。", _logger.LogPath);
            }
            catch (OperationCanceledException)
            {
                return OperationResult.Error("操作已取消。", _logger.LogPath);
            }
            catch (Exception ex)
            {
                _logger.Error(ex.Message);
                return OperationResult.Error(ex.Message, _logger.LogPath);
            }
            finally
            {
                _runState.Clear();
                AppendHermesLogs();
            }
        }

        // ---------------------------------------------------------------- helpers

        /// <summary>E11（2026-10-25 13:01 桌面端升级误报）：HEAD 提交时间缓存（一次操作取一次）。</summary>
        private DateTime? _headCommitTime;
        private bool _headCommitTimeProbed;

        /// <summary>
        /// 当前代码的「同步时刻」= git HEAD 提交时间（epoch → 本地时间）。桌面端产物是否够新，
        /// 应以「是否晚于当前代码」为判据，而不是「本轮操作是否碰过产物」——后者在版本未变、
        /// G3 跳过重建时必然为假，会把「已是最新」误报成「疑似旧产物」（13:01 事故根因）。
        /// 与 VersionFingerprint.CaptureGitHeadAsync 同款 Generic 短化实例（总预算 15s）。
        /// 取不到时返回 null，调用方退回本轮 mtime 判据（不阻塞、不误判）。
        /// </summary>
        public async Task<DateTime?> GetHeadCommitTimeAsync(CancellationToken cancellationToken)
        {
            if (_headCommitTimeProbed)
            {
                return _headCommitTime;
            }
            _headCommitTimeProbed = true;
            try
            {
                var options = new ProcessRunOptions
                {
                    Class = OperationClass.Generic,
                    NoProgressTimeout = null,
                    TotalTimeout = TimeSpan.FromSeconds(15),
                    PollIntervalMs = 500
                };
                string arguments = "-C " + ProcessRunner.QuoteArg(HermesPaths.InstallDir) + " log -1 --format=%ct";
                ProcessResult result = await _runner.RunProcessAsync(
                    "git.exe", arguments, HermesPaths.HermesHome, null, null,
                    cancellationToken, null, options).ConfigureAwait(false);
                if (result.Cancelled)
                {
                    throw new OperationCanceledException(cancellationToken);
                }
                string line = (result.StandardOutput ?? string.Empty).Trim();
                long epoch;
                if (result.ExitCode == 0 && long.TryParse(line, out epoch) && epoch > 0)
                {
                    _headCommitTime = DateTimeOffset.FromUnixTimeSeconds(epoch).LocalDateTime;
                    _logger.Info("[FP] 代码同步时刻（HEAD 提交时间）: " + _headCommitTime.Value.ToString("yyyy-MM-dd HH:mm:ss"));
                }
                else
                {
                    _logger.Warn("[FP] 读取 HEAD 提交时间失败（退出码 " + result.ExitCode + "），退用本轮 mtime 判据。");
                }
                return _headCommitTime;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.Warn("[FP] 读取 HEAD 提交时间异常（退用本轮 mtime 判据）: " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// G8 改造点 5：桌面端产物是否在本轮开始之后被更新过（mtime 粒度按文件系统 2s 内不比较）。
        /// 产物存在且 mtime 已大于 startedAt → 视为 update 刚重建过，无需再补一枪 build-only。
        /// </summary>
        private bool IsDesktopArtifactFresh(DateTime startedAt)
        {
            try
            {
                if (!HermesPaths.DesktopBuilt)
                {
                    return false;
                }
                DateTime mtime = File.GetLastWriteTime(HermesPaths.DesktopExe);
                return mtime > startedAt.AddSeconds(-2);
            }
            catch (Exception ex)
            {
                _logger.Warn("[G8] 读取桌面端产物 mtime 失败，按「产物未更新」处理: " + ex.Message);
                return false;
            }
        }

        /// <summary>E11：产物是否已覆盖指定代码时点（HEAD 提交时间；2 分钟容差吸收时钟偏差/检出耗时）。</summary>
        private bool IsDesktopArtifactFreshRelativeTo(DateTime codeSyncTime)
        {
            try
            {
                if (!HermesPaths.DesktopBuilt)
                {
                    return false;
                }
                DateTime mtime = File.GetLastWriteTime(HermesPaths.DesktopExe);
                return mtime >= codeSyncTime.AddMinutes(-2);
            }
            catch (Exception ex)
            {
                _logger.Warn("[E11] 读取桌面端产物 mtime 失败，按「需要重建」处理: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// G8 改造点 7/8 的统一收尾（流程 ⑪）：把 VerificationReport 合成三态 OperationResult。
        /// Success → Ok；Degraded → Degrade；Failed → Error。断言行已由 ResultVerifier 逐条落日志。
        /// </summary>
        private static OperationResult ConcludeResult(VerificationReport report, string operationName, string logPath)
        {
            string title = UiTextBuilder.OutcomeTitle(report.Outcome, operationName);
            string message = title + "：" + report.Summary;
            OperationResult result;
            switch (report.Outcome)
            {
                case OperationOutcome.Degraded:
                    result = OperationResult.Degrade(message, logPath);
                    break;
                case OperationOutcome.Success:
                    result = OperationResult.Ok(message, logPath);
                    break;
                default:
                    result = OperationResult.Error(message, logPath);
                    break;
            }
            result.Verification = report;
            return result;
        }

        public async Task<string> GetVersionAsync(CancellationToken cancellationToken)
        {
            if (!HermesPaths.CliLauncherPresent)
            {
                return null;
            }
            try
            {
                ProcessResult result = await _runner.RunPowerShellAsync(
                    "& " + ProcessRunner.Quote(HermesPaths.HermesCmd) + " --version",
                    HermesPaths.HermesHome,
                    _network.BuildEnvironment(),
                    null,
                    cancellationToken).ConfigureAwait(false);
                string text = (result.StandardOutput ?? string.Empty).Trim();
                if (text.Length == 0)
                {
                    return null;
                }
                string[] lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                return lines.Length > 0 ? lines[0].Trim() : null;
            }
            catch
            {
                return null;
            }
        }

        public async Task StopHermesProcessesAsync(CancellationToken cancellationToken)
        {
            // G8 改造点 9：旧行为等价——以单根构造列表转调新重载。
            var homes = new List<string>();
            homes.Add(HermesPaths.HermesHome);

            // G4（T-HU-DEV-5）E16 证据落地：停杀前把多根画像写进日志（含「选中根之外已保留运行」提示）。
            try
            {
                List<HermesHomeInfo> overview = HomeRegistry.Enumerate();
                HomeRegistry.MatchProcesses(overview);
                _logger.Info(HomeRegistry.Describe(overview));
                LogUnselectedOwnerships(overview);
            }
            catch (Exception ex)
            {
                _logger.Warn("[G4] 多根概览失败: " + ex.Message);
            }

            await StopHermesProcessesAsync(homes, true, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// G4（T-HU-DEV-5）：把「选中根之外、有运行中进程」的根逐条列出并标注「已保留运行」。
        /// 只在 selectedOnly=true（默认路径）时有意义：这些根下的进程本轮绝不会被停杀。
        /// </summary>
        private void LogUnselectedOwnerships(List<HermesHomeInfo> overview)
        {
            foreach (HermesHomeInfo home in overview)
            {
                if (home.IsSelected || home.Processes.Count == 0)
                {
                    continue;
                }
                var pids = new List<string>();
                foreach (ProcessRef process in home.Processes)
                {
                    pids.Add(process.ProcessName + " (PID " + process.Pid + ")");
                }
                _logger.Info("[G4] 选中根之外数据根 " + home.Path + " 有 "
                    + home.Processes.Count + " 个运行中进程（" + string.Join("，", pids.ToArray())
                    + "），已保留运行。");
            }
        }

        /// <summary>
        /// G8 改造点 9：按根集合停止进程。selectedOnly=true 时仅停「选中根」的进程。
        /// 保留既有正确部分：跳过自身、Kill 失败回落 KillTree、杀完延时 1500ms。
        /// </summary>
        public async Task StopHermesProcessesAsync(
            IList<string> homes, bool selectedOnly, CancellationToken cancellationToken)
        {
            // 归一化：TrimEnd + OrdinalIgnoreCase 去重（与 HomeRegistry 口径一致）。
            var roots = new List<string>();
            foreach (string home in (homes ?? new List<string>()))
            {
                if (string.IsNullOrWhiteSpace(home))
                {
                    continue;
                }
                string normalized = home.TrimEnd('\\');
                bool exists = false;
                foreach (string seen in roots)
                {
                    if (string.Equals(seen, normalized, StringComparison.OrdinalIgnoreCase))
                    {
                        exists = true;
                        break;
                    }
                }
                if (!exists)
                {
                    roots.Add(normalized);
                }
            }

            if (selectedOnly)
            {
                string selected = HermesPaths.HermesHome.TrimEnd('\\');
                roots.RemoveAll(delegate(string r)
                {
                    return !string.Equals(r, selected, StringComparison.OrdinalIgnoreCase);
                });
            }

            var killed = new List<string>();

            foreach (string name in new[] { "Hermes", "hermes", "python", "pythonw" })
            {
                foreach (Process process in Process.GetProcessesByName(name))
                {
                    try
                    {
                        string path = null;
                        try { path = process.MainModule != null ? process.MainModule.FileName : null; } catch { }
                        bool owned = false;
                        if (path != null)
                        {
                            foreach (string root in roots)
                            {
                                if (path.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase))
                                {
                                    owned = true;
                                    break;
                                }
                            }
                        }
                        if (!owned)
                        {
                            continue;
                        }
                        if (process.Id == Process.GetCurrentProcess().Id)
                        {
                            continue;
                        }
                        string label = process.ProcessName + " (PID " + process.Id + ")";
                        try
                        {
                            process.Kill();
                            process.WaitForExit(8000);
                            killed.Add(label);
                        }
                        catch
                        {
                            ProcessRunner.KillTree(process);
                            killed.Add(label);
                        }
                    }
                    catch { }
                    finally
                    {
                        try { process.Dispose(); } catch { }
                    }
                }
            }

            if (killed.Count > 0)
            {
                _logger.Warn("已停止: " + string.Join("，", killed.ToArray()));
                await Task.Delay(1500, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                _logger.Info("没有需要停止的 Hermes 进程。");
            }
        }

        public void LaunchCli()
        {
            if (!HermesPaths.CliLauncherPresent)
            {
                throw new InstallerException("未检测到 hermes 启动器。");
            }
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/k \"" + HermesPaths.HermesCmd + "\"",
                UseShellExecute = true,
                WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            });
        }

        public void LaunchDesktop()
        {
            if (!HermesPaths.DesktopBuilt)
            {
                throw new InstallerException("桌面端尚未构建，请先执行「桌面端 → 新装」。");
            }
            Process.Start(new ProcessStartInfo
            {
                FileName = HermesPaths.DesktopExe,
                WorkingDirectory = Path.GetDirectoryName(HermesPaths.DesktopExe) ?? Environment.CurrentDirectory,
                UseShellExecute = true
            });
        }

        private void AppendHermesLogs()
        {
            try
            {
                _logger.AppendExternalLog("Hermes install.log", HermesPaths.InstallLog, 120);
                _logger.AppendExternalLog("Hermes bootstrap.log", HermesPaths.BootstrapLog, 80);
            }
            catch { }
        }

        private static void Report(IProgress<ProgressInfo> progress, int index, int total, string stage, string message, bool stageChange)
        {
            if (progress != null)
            {
                try
                {
                    progress.Report(new ProgressInfo
                    {
                        Index = index,
                        Total = total,
                        StageName = stage,
                        Message = message,
                        IsStageChange = stageChange
                    });
                }
                catch { }
            }
        }

        private static string ExtractJsonLine(string output, string marker)
        {
            if (string.IsNullOrEmpty(output)) return null;
            foreach (string line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith("{") && trimmed.Contains(marker))
                {
                    return trimmed;
                }
            }
            return null;
        }

        private static string ExtractLastJsonLine(string output, string marker)
        {
            if (string.IsNullOrEmpty(output)) return null;
            string found = null;
            foreach (string line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith("{") && trimmed.Contains(marker))
                {
                    found = trimmed;
                }
            }
            return found;
        }

        private static string GetString(Dictionary<string, object> dict, string key)
        {
            if (dict != null && dict.ContainsKey(key) && dict[key] != null)
            {
                return Convert.ToString(dict[key]);
            }
            return null;
        }

        private static bool GetBool(Dictionary<string, object> dict, string key)
        {
            if (dict != null && dict.ContainsKey(key) && dict[key] != null)
            {
                try { return Convert.ToBoolean(dict[key]); } catch { }
            }
            return false;
        }

        private static string Truncate(string text, int max)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            text = text.Trim();
            return text.Length <= max ? text : text.Substring(0, max) + "…";
        }

        /// <summary>指纹日志用：给祖先节留短 SHA。</summary>
        private static string ShortSha(string sha)
        {
            return string.IsNullOrEmpty(sha) ? "null" : sha.Substring(0, Math.Min(10, sha.Length));
        }

        private sealed class StageResult
        {
            public bool Ok { get; set; }
            public bool Skipped { get; set; }
            public string Reason { get; set; }
        }
    }
}
