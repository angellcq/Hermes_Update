using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace HermesInstaller.Core
{
    /// <summary>
    /// E14：SSH 代理的进程级单例。负责「按需起隧道 + 起本地 HTTP 桥 + 把代理灌进
    /// 进程默认代理与子进程环境变量」，并在操作结束时统一收摊。
    ///
    /// 一个实例的生命周期 = 一次安装/升级操作。跨操作保留隧道没有意义：
    /// 端口占用、跳板机会话超时、用户换了跳板机都会变成幽灵状态。
    /// </summary>
    public static class ProxyRuntime
    {
        private static readonly object Sync = new object();
        private static SshProxyTunnel _tunnel;
        private static LocalSocksHttpProxy _bridge;
        private static string _httpProxyUrl;
        private static int _socksPort;
        private static IWebProxy _previousProxy;
        private static string _lastError = string.Empty;

        // E19-P2-6：收摊动作要留痕。原先 Stop() 一行日志都不写，离线读日志只能看到
        // 「操作到 bootstrap 附录就结束了」，代理何时撤、有没有撤全都无从判断。
        // 由 EnsureStartedAsync 记录本次操作的 Logger，Stop() 用完即清。
        private static Logger _logger;

        public static bool IsActive
        {
            get
            {
                lock (Sync)
                {
                    return _bridge != null && _bridge.IsRunning;
                }
            }
        }

        public static string HttpProxyUrl
        {
            get
            {
                lock (Sync)
                {
                    return _httpProxyUrl;
                }
            }
        }

        public static int SocksPort
        {
            get
            {
                lock (Sync)
                {
                    return _socksPort;
                }
            }
        }

        public static string LastError
        {
            get
            {
                lock (Sync)
                {
                    return _lastError;
                }
            }
        }

        /// <summary>
        /// 按配置起代理。未启用返回 false（不视为错误）；启用但起不来返回 false 并写 LastError。
        /// 已启动则直接返回 true（幂等）。
        /// </summary>
        public static async Task<bool> EnsureStartedAsync(
            Settings settings, Logger logger, CancellationToken cancellationToken)
        {
            if (settings == null || !settings.SshProxyEnabled)
            {
                return false;
            }
            lock (Sync)
            {
                if (_bridge != null && _bridge.IsRunning)
                {
                    return true;
                }
            }

            var tunnel = new SshProxyTunnel(settings, logger);
            bool started;
            try
            {
                started = await tunnel.StartAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                tunnel.Dispose();
                throw;
            }
            catch (Exception ex)
            {
                tunnel.Dispose();
                lock (Sync)
                {
                    _lastError = "SSH 隧道启动异常: " + ex.Message;
                }
                return false;
            }

            if (!started)
            {
                string reason = tunnel.LastError;
                tunnel.Dispose();
                lock (Sync)
                {
                    _lastError = string.IsNullOrEmpty(reason) ? "SSH 隧道未能就绪" : reason;
                }
                return false;
            }

            var bridge = new LocalSocksHttpProxy(logger);
            int httpPort = NormalizePort(settings.SshHttpPort, 1081);
            if (!bridge.Start("127.0.0.1", tunnel.SocksPort, httpPort))
            {
                // 端口被占（例如上一轮残留）时退回系统分配，保证链路可用优先。
                if (!bridge.Start("127.0.0.1", tunnel.SocksPort, 0))
                {
                    tunnel.Dispose();
                    lock (Sync)
                    {
                        _lastError = "本地 HTTP 代理启动失败（端口 " + httpPort + " 及随机端口均不可用）";
                    }
                    return false;
                }
            }

            lock (Sync)
            {
                _tunnel = tunnel;
                _bridge = bridge;
                _httpProxyUrl = "http://127.0.0.1:" + bridge.Port;
                _socksPort = tunnel.SocksPort;
                _logger = logger;
                _lastError = string.Empty;
            }

            ApplyProcessDefaults();
            if (logger != null)
            {
                logger.Success(string.Format(
                    "[PROXY] SSH 代理已启用：SOCKS5 127.0.0.1:{0}，HTTP {1}（git/npm/pip/electron 子进程同步生效）",
                    tunnel.SocksPort, _httpProxyUrl));
            }
            return true;
        }

        /// <summary>把代理写进给子进程的环境变量块（BuildEnvironment 调用）。</summary>
        public static void ApplyToEnvironment(IDictionary<string, string> env)
        {
            if (env == null)
            {
                return;
            }
            string http = HttpProxyUrl;
            if (string.IsNullOrEmpty(http))
            {
                return;
            }

            env["HTTP_PROXY"] = http;
            env["HTTPS_PROXY"] = http;
            env["http_proxy"] = http;
            env["https_proxy"] = http;
            // E19-P0-2：**不再注入 ALL_PROXY/all_proxy**（2026-11-26 09:43 实锤）。
            // 官方清理只认 _ADDED_KEYS = HTTPS_PROXY/https_proxy/HTTP_PROXY/http_proxy/
            // NO_PROXY/no_proxy（hermes_cli/update_proxy_env.py:25），ALL_PROXY 不在其中，
            // 所以标记里写上它也不会被丢弃；长驻 gateway 于是带着
            // ALL_PROXY=socks5h://127.0.0.1:<端口> 活下去，httpx 的 trust_env 认它，
            // 桥一停就报 `Missing dependencies for SOCKS support.`（Lark 通道连不上）。
            // 而我们灌给子进程的本来就只有 http:// 方案（HTTP 桥），HTTP(S)_PROXY 已覆盖
            // git/npm/pip/electron 的全部出网路径，ALL_PROXY 是纯冗余——删掉，注入集与
            // 官方清理集对齐，不留收不回的变量。
            env["NO_PROXY"] = "localhost,127.0.0.1,::1";
            env["no_proxy"] = env["NO_PROXY"];
            env["npm_config_proxy"] = http;
            env["npm_config_https_proxy"] = http;
            // @electron/get 走 global-agent：必须显式打开，否则只读 GLOBAL_AGENT_*。
            env["ELECTRON_GET_USE_PROXY"] = "1";
            env["GLOBAL_AGENT_HTTP_PROXY"] = http;
            env["GLOBAL_AGENT_HTTPS_PROXY"] = http;

            // E17：这些代理变量只对本次操作有效。hermes update 会重启长驻 gateway，
            // 它继承整个环境块；一键收摊（Stop）把 1081 桥停掉后，gateway 仍拿着
            // HTTP(S)_PROXY=http://127.0.0.1:1081，之后所有出网请求都是
            // httpcore ConnectError [Errno 10061]，Nous OAuth 登录即报「服务连不上」。
            // 官方 Desktop 用 HERMES_UPDATE_SYSTEM_PROXY 标记同一批键，后端启动时
            // （hermes_cli/update_proxy_env.py 的 restore_user_proxy_env）会按标记丢弃；
            // 这里必须同样标记，否则升级一次就把登录弄坏一次。
            // E19-P0-2 补充：标记的键集必须是我们注入键的**超集**，注入侧每加一个代理键
            // 都要同步考虑官方清理侧认不认——所以本块只允许出现 _ADDED_KEYS 里的变量。
            env["HERMES_UPDATE_SYSTEM_PROXY"] =
                "HTTPS_PROXY,https_proxy,HTTP_PROXY,http_proxy,NO_PROXY,no_proxy";
        }

        /// <summary>
        /// 让进程内所有 HttpClient / HttpWebRequest 走本地 HTTP 桥。
        /// .NET Framework 的 HttpClientHandler 在 Proxy 为 null 时读 WebRequest.DefaultWebProxy，
        /// 所以改这一处就能覆盖 NetworkProbe / DependencyPrefetcher / install.ps1 下载。
        /// </summary>
        private static void ApplyProcessDefaults()
        {
            try
            {
                string url;
                lock (Sync)
                {
                    url = _httpProxyUrl;
                }
                if (string.IsNullOrEmpty(url))
                {
                    return;
                }
                if (_previousProxy == null)
                {
                    _previousProxy = WebRequest.DefaultWebProxy;
                }
                var proxy = new WebProxy(new Uri(url))
                {
                    BypassProxyOnLocal = false
                };
                WebRequest.DefaultWebProxy = proxy;
            }
            catch
            {
                // 默认代理设置失败不影响子进程环境变量那条路。
            }
        }

        /// <summary>收摊：停桥、停隧道、恢复默认代理。可重复调用。</summary>
        public static void Stop()
        {
            LocalSocksHttpProxy bridge;
            SshProxyTunnel tunnel;
            IWebProxy previous;
            Logger logger;
            int socksPort;
            string httpUrl;
            lock (Sync)
            {
                bridge = _bridge;
                tunnel = _tunnel;
                previous = _previousProxy;
                logger = _logger;
                socksPort = _socksPort;
                httpUrl = _httpProxyUrl;
                _bridge = null;
                _tunnel = null;
                _httpProxyUrl = null;
                _socksPort = 0;
                _previousProxy = null;
                _logger = null;
            }

            try
            {
                WebRequest.DefaultWebProxy = previous;
            }
            catch { }

            if (bridge != null)
            {
                try { bridge.Stop(); }
                catch { }
            }
            if (tunnel != null)
            {
                try { tunnel.Dispose(); }
                catch { }
            }

            // E19-P2-6：只在真的有过东西可撤时写这行，未启用代理的常规操作不产噪音。
            if (logger != null && (bridge != null || tunnel != null))
            {
                logger.Info(string.Format(
                    "[PROXY] 代理已收摊：HTTP 桥 {0} 与 SSH 隧道（SOCKS5 127.0.0.1:{1}）已停止，进程默认代理已恢复。",
                    string.IsNullOrEmpty(httpUrl) ? "127.0.0.1" : httpUrl,
                    socksPort > 0 ? socksPort.ToString() : "0"));
            }
        }

        /// <summary>
        /// 连通性快测：经本地 HTTP 桥取一个能回显出口 IP 的地址。
        /// 返回字符串形如 "出口 IP: 1.2.3.4（耗时 850ms）"；失败抛异常，由调用方展示。
        /// </summary>
        public static string TestExitIp()
        {
            if (!IsActive)
            {
                throw new InvalidOperationException("代理未启动。");
            }
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            using (var handler = new HttpClientHandler { UseProxy = true })
            using (var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) })
            {
                string ip = client.GetStringAsync("https://api.ipify.org").GetAwaiter().GetResult();
                stopwatch.Stop();
                return string.Format("出口 IP: {0}（耗时 {1}ms）", (ip ?? string.Empty).Trim(), stopwatch.ElapsedMilliseconds);
            }
        }

        private static int NormalizePort(int value, int fallback)
        {
            return value > 0 && value < 65536 ? value : fallback;
        }
    }
}
