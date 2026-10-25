using System;
using System.Collections.Generic;
using System.IO;

namespace HermesInstaller.Core
{
    /// <summary>
    /// Builds the environment block handed to install.ps1 / hermes update so that
    /// git, npm, PyPI and the Electron build all use reachable mirrors.
    /// M2（DD-M2 §2.2 G4）：镜像值跟随 NetworkPlanner 各端点组的当前级；
    /// 用户在 Settings 里手填的镜像（异于内置默认值）必须生效，不被自动选级覆盖。
    /// 另注入 uv / npm 缓存目录（FR-1.4），并在 GitDeps 组降级时用 GIT_CONFIG_*
    /// 把 github.com 依赖改写到镜像（FR-1.5）——只动环境变量，不写用户全局配置。
    /// 本方法可重复调用、除 CreateDirectory 外零副作用，切级编排每轮都会调它。
    /// </summary>
    public sealed class NetworkPreparer
    {
        private readonly Settings _settings;
        private readonly Logger _logger;
        private readonly NetworkPlanner _planner;

        public NetworkPreparer(Settings settings, Logger logger)
            : this(settings, logger, null)
        {
        }

        public NetworkPreparer(Settings settings, Logger logger, NetworkPlanner planner)
        {
            _settings = settings ?? new Settings();
            _logger = logger;
            _planner = planner != null ? planner : new NetworkPlanner(_logger, _settings);
        }

        public Dictionary<string, string> BuildEnvironment()
        {
            var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // 取值优先级：Settings 显式非空（异于内置默认）> planner 当前级端点 > 内置默认。
            string repo = Resolve(EndpointGroup.Repo, _settings.RepoMirror, Mirrors.RepoCandidates[0]);
            string electron = Resolve(EndpointGroup.Electron, _settings.ElectronMirror, Mirrors.ElectronMirror);
            string builder = Resolve(EndpointGroup.Builder, _settings.BuilderMirror, Mirrors.BuilderMirror);
            string npm = Resolve(EndpointGroup.Npm, _settings.NpmRegistry, Mirrors.NpmRegistry);
            string pip = Resolve(EndpointGroup.Pip, _settings.PipIndex, Mirrors.PipIndex);

            string uvCache = Path.Combine(HermesPaths.CacheDir, "uv");
            string npmCache = Path.Combine(HermesPaths.CacheDir, "npm");

            env["HERMES_REPO_URL"] = repo;
            env["ELECTRON_MIRROR"] = electron;
            env["ELECTRON_BUILDER_BINARIES_MIRROR"] = builder;
            env["ELECTRON_BUILDER_CACHE"] = Path.Combine(HermesPaths.CacheDir, "electron-builder");
            env["UV_CACHE_DIR"] = uvCache;
            env["npm_config_cache"] = npmCache;
            env["NPM_CONFIG_CACHE"] = npmCache;
            env["npm_config_registry"] = npm;
            env["NPM_CONFIG_REGISTRY"] = npm;
            env["PIP_INDEX_URL"] = pip;
            env["PIP_TRUSTED_HOST"] = HostOf(pip);
            env["UV_DEFAULT_INDEX"] = pip;
            env["UV_INDEX_URL"] = pip;
            env["GIT_TERMINAL_PROMPT"] = "0";
            env["GCM_INTERACTIVE"] = "never";
            env["GIT_ASKPASS"] = "echo";
            // E7-2（2026-10-25 08:44 升级事故）：hermes update 收尾的 fork 同步推送
            // （push origin main --force-with-lease）在本机为无凭证链接，网络 stall 时
            // 会静默挂起，直到被安装器的 180s 无进展预算按失败终止。加上低速率中止
            // 后，卡死连接 30 秒内自弃；推送失败本是 hermes 预期的非致命路径
            // （打印 "couldn't push to fork" 后完成更新）。
            env["GIT_HTTP_LOW_SPEED_LIMIT"] = "1000";
            env["GIT_HTTP_LOW_SPEED_TIME"] = "30";
            env["PYTHONUTF8"] = "1";
            env["PYTHONIOENCODING"] = "utf-8";

            ApplyGitDepsRewrite(env);

            try
            {
                Directory.CreateDirectory(env["ELECTRON_BUILDER_CACHE"]);
                Directory.CreateDirectory(env["UV_CACHE_DIR"]);
                Directory.CreateDirectory(env["npm_config_cache"]);
            }
            catch { }

            LogEnvironment();

            return env;
        }

        /// <summary>
        /// 升级子进程将实际使用的 ELECTRON_MIRROR 基址：与 BuildEnvironment 第 49 行
        /// `env["ELECTRON_MIRROR"] = electron` 完全同一解析路径（Electron 组）。
        /// DependencyPrefetcher 用它保证预热写入的缓存键
        /// （sha256(base + "v" + 版本)）== update 现场查询的键。
        /// </summary>
        public string ResolveElectronBase()
        {
            return Resolve(EndpointGroup.Electron, _settings.ElectronMirror, Mirrors.ElectronMirror);
        }

        /// <summary>
        /// E10-③（2026-10-25）：升级子进程将实际使用的 ELECTRON_BUILDER_BINARIES_MIRROR
        /// 基址（与 BuildEnvironment 第 50 行 `env["ELECTRON_BUILDER_BINARIES_MIRROR"] =
        /// builder` 完全同一解析路径，Builder 组）。DependencyPrefetcher 用它预热
        /// electron-builder 工具集（winCodeSign / 7zip）的 archive cache，
        /// 保证缓存放置的 URL 路径 == builder 现场要请求的 URL。
        /// ENG-E10-3 考古实锤：app-builder-lib 26.18 electronGet.js
        /// `getBinariesMirrorUrl` 确认读取 ELECTRON_BUILDER_BINARIES_MIRROR（前序
        /// 「代码无引用」结论源于安装位 node_modules 残缺，作废）。
        /// </summary>
        public string ResolveBuilderBase()
        {
            return Resolve(EndpointGroup.Builder, _settings.BuilderMirror, Mirrors.BuilderMirror);
        }

        /// <summary>
        /// FR-1.5：GitDeps 组当前级 ≥ 1 时，注入一组源内联 git 配置，把
        /// https://github.com/ 前缀改写到该组当前级端点（如
        /// https://ghproxy.net/https://github.com/）。级为 0 时一个键都不注入，
        /// 让 git 直连。只进环境变量，绝不碰 .gitconfig。
        /// </summary>
        private void ApplyGitDepsRewrite(Dictionary<string, string> env)
        {
            int level = _planner.CurrentLevel(EndpointGroup.GitDeps);
            string mirror = LevelEndpointUrl(EndpointGroup.GitDeps);
            if (level < 1 || string.IsNullOrWhiteSpace(mirror))
            {
                return;
            }
            env["GIT_CONFIG_COUNT"] = "1";
            env["GIT_CONFIG_KEY_0"] = "url." + mirror + ".insteadOf";
            env["GIT_CONFIG_VALUE_0"] = "https://github.com/";
        }

        /// <summary>
        /// 单组取值（E12-①，2026-10-25 14:10 事故）：
        ///   1) planner 当前级 &gt; 0（探针/传输已判定要降级）→ 用该级端点；
        ///   2) Settings 里非空 → 直接采纳。E12 之前这里还要求「异于内置默认」才算用户手填，
        ///      而探针 AutoPick 选出的结果恰好就是内置默认值（npmmirror），于是被判成
        ///      「没设置」→ 落回 planner 的 L0 = GitHub 官方直连，探针结论被自己丢掉。
        ///   3) 空值 → planner 当前级（含 L0）端点；
        ///   4) 组内没有该级端点 → 内置默认。
        /// </summary>
        private string Resolve(EndpointGroup group, string settingValue, string fallback)
        {
            if (_planner.CurrentLevel(group) > 0)
            {
                string downgraded = LevelEndpointUrl(group);
                if (!string.IsNullOrWhiteSpace(downgraded))
                {
                    return downgraded;
                }
            }
            if (!string.IsNullOrWhiteSpace(settingValue))
            {
                return settingValue.Trim();
            }
            string byLevel = LevelEndpointUrl(group);
            if (!string.IsNullOrWhiteSpace(byLevel))
            {
                return byLevel;
            }
            return fallback;
        }

        /// <summary>
        /// 组当前级对应端点的 Url（组内第一条 Level == 当前级的端点，L0 在前的组内
        /// 顺序由 Mirrors.GroupsFor 保证）；该级无端点或无法判定时返回 null。
        /// </summary>
        private string LevelEndpointUrl(EndpointGroup group)
        {
            int level = _planner.CurrentLevel(group);
            List<NetworkEndpoint> endpoints = Mirrors.GroupsFor(group);
            for (int i = 0; i < endpoints.Count; i++)
            {
                if (endpoints[i].Level == level && !string.IsNullOrWhiteSpace(endpoints[i].Url))
                {
                    return endpoints[i].Url;
                }
            }
            return null;
        }

        /// <summary>从 Url 解析 host（给 PIP_TRUSTED_HOST）；解析失败回空串。</summary>
        private static string HostOf(string url)
        {
            try
            {
                return new Uri(url).Host;
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// 每次 BuildEnvironment 都记一行 [NET] 摘要（级会变，缓存会骗人），
        /// 概要带上各组当前级。经 Logger.Info 走统一脱敏出口。
        /// </summary>
        private void LogEnvironment()
        {
            if (_logger == null)
            {
                return;
            }
            string summary = "Repo=L" + _planner.CurrentLevel(EndpointGroup.Repo)
                + ",GitDeps=L" + _planner.CurrentLevel(EndpointGroup.GitDeps)
                + ",Electron=L" + _planner.CurrentLevel(EndpointGroup.Electron)
                + ",Builder=L" + _planner.CurrentLevel(EndpointGroup.Builder)
                + ",Npm=L" + _planner.CurrentLevel(EndpointGroup.Npm)
                + ",Pip=L" + _planner.CurrentLevel(EndpointGroup.Pip);
            _logger.Info(string.Format(
                "[NET] kind=env group=all reason=build op=env target={0} attempt=1 elapsed=0ms rate=- action=use",
                summary));
        }
    }
}
