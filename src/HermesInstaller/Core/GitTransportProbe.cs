using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace HermesInstaller.Core
{
    /// <summary>
    /// 一次 git 传输探测的结论。
    /// </summary>
    public sealed class GitTransportResult
    {
        /// <summary>该仓库地址是否真的能被 git 访问到（ls-remote 成功）。</summary>
        public bool Ok { get; set; }

        /// <summary>
        /// 生效的传输方式：as-configured（本机配置原样可用）/ direct（清掉该主机的代理）/
        /// schannel（改用 Windows 证书库）/ direct+schannel。
        /// </summary>
        public string Mode { get; set; }

        /// <summary>需要注入子进程的 git 配置项（config key → value），空表表示无需修复。</summary>
        public Dictionary<string, string> Overrides { get; set; }

        public long LatencyMs { get; set; }

        /// <summary>结论摘要（成功时说明修复了什么；失败时带 stderr 首行）。</summary>
        public string Diagnostic { get; set; }

        public GitTransportResult()
        {
            Overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Mode = "as-configured";
        }
    }

    /// <summary>
    /// E13（2026-10-25 15:36 升级失败）：git 传输层的真实可达性探测与自愈。
    ///
    /// 事故：探针用 .NET HttpClient 访问 <c>https://github.com/…/info/refs</c> 得到 200，
    /// 判「GitHub 主站可用」；而 <c>hermes update</c> 实际执行的是 git 子进程，它读的是
    /// git 自己的配置（例如按域名残留的 http.&lt;host&gt;.proxy 指向一个已关闭的本地端口，
    /// 或 http.sslcainfo 指向一个已不存在的证书文件）→ <c>git ls-remote</c> exit 128 →
    /// 「No published stable release could be verified」。
    /// 两者代理/证书来源不同（.NET 读 WinINET，git 读 config 与环境变量），所以 HTTP
    /// 探针**不能**代替 git 传输探测。
    ///
    /// 本类不假设任何具体地址、端口或证书路径，只做通用阶梯：
    ///   1) as-configured —— 尊重本机既有代理/证书配置，能用就一个字都不改；
    ///   2) direct —— 只在失败信息属于传输层（connect/proxy/certificate/SSL/TLS/timeout）
    ///      时，才把「该主机」的代理清空（通用 http.proxy 与 http.&lt;scheme://host&gt;.proxy 一起清）；
    ///   3) schannel —— 改用 Windows 证书库，绕过 openssl CA 路径配错/文件缺失；
    ///   4) direct+schannel —— 两者叠加。
    /// 命中即停，并把「需要注入子进程的配置」返回给调用方。**绝不写用户全局 git 配置**，
    /// 只作用于本工具派生的子进程（与 FR-1.5「只动环境变量」一致）。
    /// </summary>
    public sealed class GitTransportProbe
    {
        private readonly Logger _logger;
        private readonly ProcessRunner _runner;

        public GitTransportProbe(Logger logger, ProcessRunner runner)
        {
            _logger = logger;
            _runner = runner;
        }

        /// <summary>探测某个仓库地址的 git 可达性；逐级尝试通用修复，返回第一个成功的方式。</summary>
        public async Task<GitTransportResult> ProbeAsync(string repoUrl, CancellationToken cancellationToken)
        {
            var result = new GitTransportResult();
            if (string.IsNullOrWhiteSpace(repoUrl))
            {
                result.Diagnostic = "仓库地址为空";
                return result;
            }

            List<Attempt> attempts = BuildLadder(repoUrl);
            var total = Stopwatch.StartNew();
            string lastError = null;

            for (int i = 0; i < attempts.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Attempt attempt = attempts[i];
                var stopwatch = Stopwatch.StartNew();
                ProcessResult run = await RunLsRemoteAsync(repoUrl, attempt.Overrides, attempt.TimeoutSeconds, cancellationToken)
                    .ConfigureAwait(false);
                stopwatch.Stop();

                if (run != null && run.ExitCode == 0)
                {
                    result.Ok = true;
                    result.Mode = attempt.Name;
                    result.Overrides = attempt.Overrides;
                    result.LatencyMs = total.ElapsedMilliseconds;
                    result.Diagnostic = i == 0
                        ? "as-configured 可用"
                        : string.Format("第 {0} 级修复生效（{1}），原配置：{2}", i + 1, attempt.Name, lastError);
                    if (_logger != null)
                    {
                        if (i == 0)
                        {
                            _logger.Info(string.Format("[GIT] 传输探测通过（as-configured，{0}ms）: {1}",
                                stopwatch.ElapsedMilliseconds, repoUrl));
                        }
                        else
                        {
                            _logger.Warn(string.Format(
                                "[GIT] 传输探测通过（{0}，{1}ms）—— 已自动绕过不可用的传输配置。原失败：{2}",
                                attempt.Name, stopwatch.ElapsedMilliseconds, lastError));
                        }
                    }
                    return result;
                }

                lastError = FirstLine(run != null ? run.StandardError : null, run != null ? run.ExitCode : -1);
                if (_logger != null)
                {
                    _logger.Info(string.Format("[GIT] 传输探测第 {0} 级（{1}）失败: {2}",
                        i + 1, attempt.Name, lastError));
                }

                // 非传输层失败（仓库不存在 / 鉴权 / 引用不存在）再做修复也没用，直接收敛。
                if (!LooksTransportLevel(lastError))
                {
                    break;
                }
            }

            result.Ok = false;
            result.LatencyMs = total.ElapsedMilliseconds;
            result.Diagnostic = lastError;
            return result;
        }

        /// <summary>按通用阶梯构造尝试表；代理键由仓库 URL 推导，不含任何硬编码主机。</summary>
        private static List<Attempt> BuildLadder(string repoUrl)
        {
            string proxyKey = ProxyKeyFor(repoUrl);
            var direct = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            direct["http.proxy"] = string.Empty;
            if (proxyKey != null)
            {
                direct[proxyKey] = string.Empty;
            }
            var ssl = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            ssl["http.sslbackend"] = "schannel";
            var both = new Dictionary<string, string>(direct, StringComparer.OrdinalIgnoreCase);
            both["http.sslbackend"] = "schannel";

            var attempts = new List<Attempt>();
            attempts.Add(new Attempt("as-configured", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), 25));
            attempts.Add(new Attempt("direct", direct, 25));
            attempts.Add(new Attempt("schannel", ssl, 25));
            attempts.Add(new Attempt("direct+schannel", both, 25));
            return attempts;
        }

        /// <summary>从 URL 推导 host 级代理键：http.&lt;scheme&gt;://&lt;host&gt;.proxy（git 用 http.* 段管 https 代理）。</summary>
        private static string ProxyKeyFor(string repoUrl)
        {
            try
            {
                var uri = new Uri(repoUrl);
                string hostPart = uri.IsDefaultPort ? uri.Host : uri.Host + ":" + uri.Port;
                return "http." + uri.Scheme + "://" + hostPart + ".proxy";
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 判断失败是否属于「传输层」——只有这类才值得换证书后端/清代理。
        /// 关键词按 git/libcurl 的实际输出取值；命中任一即认为可修复。
        /// </summary>
        private static bool LooksTransportLevel(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return false;
            }
            string[] markers =
            {
                "proxy", "connect", "certificate", "ssl", "tls", "handshake",
                "timed out", "timeout", "could not resolve host", "network is unreachable",
                "connection reset", "unable to access"
            };
            string lower = message.ToLowerInvariant();
            for (int i = 0; i < markers.Length; i++)
            {
                if (lower.IndexOf(markers[i], StringComparison.Ordinal) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        private async Task<ProcessResult> RunLsRemoteAsync(
            string repoUrl, IDictionary<string, string> overrides, int timeoutSeconds, CancellationToken cancellationToken)
        {
            var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            GitConfig.Apply(env, ToEntries(overrides));
            var options = new ProcessRunOptions
            {
                Class = OperationClass.EndpointProbe,
                NoProgressTimeout = TimeSpan.FromSeconds(timeoutSeconds),
                TotalTimeout = TimeSpan.FromSeconds(timeoutSeconds)
            };
            try
            {
                return await _runner.RunProcessAsync(
                    "git.exe",
                    "ls-remote --heads " + ProcessRunner.QuoteArg(repoUrl) + " main",
                    null,
                    env,
                    null,
                    cancellationToken,
                    null,
                    options).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                var failed = new ProcessResult { ExitCode = -1 };
                failed.StandardError = ex.Message;
                return failed;
            }
        }

        private static List<KeyValuePair<string, string>> ToEntries(IDictionary<string, string> overrides)
        {
            var entries = new List<KeyValuePair<string, string>>();
            if (overrides == null)
            {
                return entries;
            }
            foreach (KeyValuePair<string, string> pair in overrides)
            {
                entries.Add(pair);
            }
            return entries;
        }

        private static string FirstLine(string text, int exitCode)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return "退出码 " + exitCode + "（无 stderr）";
            }
            string trimmed = text.Trim();
            int newline = trimmed.IndexOfAny(new[] { '\r', '\n' });
            string first = newline > 0 ? trimmed.Substring(0, newline) : trimmed;
            return first.Length > 220 ? first.Substring(0, 220) + "…" : first;
        }

        private sealed class Attempt
        {
            public string Name { get; private set; }
            public Dictionary<string, string> Overrides { get; private set; }
            public int TimeoutSeconds { get; private set; }

            public Attempt(string name, Dictionary<string, string> overrides, int timeoutSeconds)
            {
                Name = name;
                Overrides = overrides;
                TimeoutSeconds = timeoutSeconds;
            }
        }
    }

    /// <summary>
    /// E13：把一组 git 配置项写进子进程环境（GIT_CONFIG_COUNT/KEY_n/VALUE_n）。
    /// 与 `git -c key=value` 等价，只影响本进程派生的 git，不改用户全局/仓库配置。
    /// 调用方必须一次给全（含 insteadOf 之类的既有条目），本方法会覆盖旧的 GIT_CONFIG_*。
    /// </summary>
    public static class GitConfig
    {
        public static void Apply(IDictionary<string, string> env, IList<KeyValuePair<string, string>> entries)
        {
            if (env == null)
            {
                return;
            }
            // 先清掉可能残留的编号键，避免 KEY_3 之类的幽灵条目参与解析。
            for (int i = 0; i < 32; i++)
            {
                env.Remove("GIT_CONFIG_KEY_" + i);
                env.Remove("GIT_CONFIG_VALUE_" + i);
            }

            if (entries == null || entries.Count == 0)
            {
                env.Remove("GIT_CONFIG_COUNT");
                return;
            }

            env["GIT_CONFIG_COUNT"] = entries.Count.ToString();
            for (int i = 0; i < entries.Count; i++)
            {
                env["GIT_CONFIG_KEY_" + i] = entries[i].Key;
                env["GIT_CONFIG_VALUE_" + i] = entries[i].Value ?? string.Empty;
            }
        }
    }
}
