using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HermesInstaller.Core
{
    public sealed class ProcessResult
    {
        public int ExitCode { get; set; }
        public string StandardOutput { get; set; }
        public string StandardError { get; set; }
        public bool Cancelled { get; set; }
        public bool TimedOut { get; set; }
        public string TimeoutKind { get; set; }     // "no-progress" | "total" | null
        public long ElapsedMs { get; set; }
        public long LastOutputAgoMs { get; set; }
    }

    /// <summary>
    /// Runs child processes with streamed output. PowerShell wrappers are written with a
    /// UTF-8 BOM and switch the console encodings so Chinese/Unicode output survives.
    /// When timeouts are requested via <see cref="ProcessRunOptions"/>, the runner polls
    /// and enforces total / no-progress budgets, killing the tree on breach (DD §2.2 G7).
    /// </summary>
    public sealed class ProcessRunner
    {
        private readonly string _runDirectory;
        private readonly Logger _logger;

        // 时间戳（Stopwatch.GetTimestamp）由输出事件线程更新，轮询线程读取（DD §2.2 G7 Emit 改造）。
        private long _lastOutputStamp;

        public ProcessRunner() : this(null)
        {
        }

        public ProcessRunner(Logger logger)
        {
            _logger = logger;
            _runDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "HermesInstaller",
                "run");
            Directory.CreateDirectory(_runDirectory);
        }

        public string RunDirectory { get { return _runDirectory; } }

        /// <summary>Quotes a value for use inside PowerShell script code.</summary>
        public static string Quote(string value)
        {
            if (value == null)
            {
                return "''";
            }
            return "'" + value.Replace("'", "''") + "'";
        }

        /// <summary>Quotes a value for a Windows process command line (not PowerShell code).</summary>
        public static string QuoteArg(string value)
        {
            if (value == null)
            {
                return "\"\"";
            }
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        /// <summary>
        /// Writes a PowerShell wrapper that forces UTF-8 and invokes <paramref name="invocation"/>,
        /// then runs it with Windows PowerShell 5.1.
        /// </summary>
        public Task<ProcessResult> RunPowerShellAsync(
            string invocation,
            string workingDirectory,
            IDictionary<string, string> environment,
            Action<string, LogLevel> onLine,
            CancellationToken cancellationToken)
        {
            // P3 不静默：旧签名调用点按 Generic(1800s) 兜底，并在日志里暴露。
            WarnNoTimeoutPolicy("powershell.exe");
            return RunPowerShellAsync(invocation, workingDirectory, environment, onLine, cancellationToken, null);
        }

        /// <summary>
        /// Writes a PowerShell wrapper that forces UTF-8 and invokes <paramref name="invocation"/>,
        /// then runs it with Windows PowerShell 5.1 under an explicit timeout policy.
        /// </summary>
        public Task<ProcessResult> RunPowerShellAsync(
            string invocation,
            string workingDirectory,
            IDictionary<string, string> environment,
            Action<string, LogLevel> onLine,
            CancellationToken cancellationToken,
            ProcessRunOptions options)
        {
            string wrapperPath = Path.Combine(_runDirectory, "wrap-" + Guid.NewGuid().ToString("N") + ".ps1");
            string wrapper =
                "$ErrorActionPreference = 'Continue'\r\n" +
                "try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }\r\n" +
                "try { $OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }\r\n" +
                "try { $env:PYTHONUTF8 = '1'; $env:PYTHONIOENCODING = 'utf-8' } catch { }\r\n" +
                invocation + "\r\n" +
                "exit $LASTEXITCODE\r\n";
            File.WriteAllText(wrapperPath, wrapper, new UTF8Encoding(true));

            string args = "-NoProfile -NoLogo -NonInteractive -ExecutionPolicy Bypass -File " + QuoteArg(wrapperPath);
            return RunProcessAsync("powershell.exe", args, workingDirectory, environment, onLine, cancellationToken, wrapperPath, options);
        }

        /// <summary>Legacy overload — no explicit timeout policy (Generic 1800s fallback, logged).</summary>
        public Task<ProcessResult> RunProcessAsync(
            string fileName,
            string arguments,
            string workingDirectory,
            IDictionary<string, string> environment,
            Action<string, LogLevel> onLine,
            CancellationToken cancellationToken,
            string cleanupFile)
        {
            // P3 不静默：忘记传超时策略的调用点必须在日志里暴露。
            WarnNoTimeoutPolicy(fileName);
            return RunProcessAsync(
                fileName, arguments, workingDirectory, environment,
                onLine, cancellationToken, cleanupFile, null);
        }

        /// <summary>
        /// Runs a child process with streamed output under an explicit timeout policy.
        /// </summary>
        public async Task<ProcessResult> RunProcessAsync(
            string fileName,
            string arguments,
            string workingDirectory,
            IDictionary<string, string> environment,
            Action<string, LogLevel> onLine,
            CancellationToken cancellationToken,
            string cleanupFile,
            ProcessRunOptions options)
        {
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            bool cancelled = false;
            bool timedOut = false;
            string timeoutKind = null;

            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = string.IsNullOrEmpty(workingDirectory) ? Environment.CurrentDirectory : workingDirectory
            };

            psi.EnvironmentVariables["PYTHONUTF8"] = "1";
            psi.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
            psi.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";
            psi.EnvironmentVariables["GCM_INTERACTIVE"] = "never";
            if (environment != null)
            {
                foreach (KeyValuePair<string, string> pair in environment)
                {
                    if (pair.Key == null)
                    {
                        continue;
                    }
                    psi.EnvironmentVariables[pair.Key] = pair.Value ?? string.Empty;
                }
            }

            using (var process = new Process { StartInfo = psi, EnableRaisingEvents = true })
            {
                DataReceivedEventHandler outHandler = (sender, e) =>
                {
                    if (e.Data == null)
                    {
                        return;
                    }
                    lock (stdout) { stdout.AppendLine(e.Data); }
                    Emit(onLine, e.Data, LogLevel.Info);
                };
                DataReceivedEventHandler errHandler = (sender, e) =>
                {
                    if (e.Data == null)
                    {
                        return;
                    }
                    lock (stderr) { stderr.AppendLine(e.Data); }
                    Emit(onLine, e.Data, LogLevel.Warn);
                };

                process.OutputDataReceived += outHandler;
                process.ErrorDataReceived += errHandler;

                try
                {
                    if (!process.Start())
                    {
                        return new ProcessResult { ExitCode = -1, StandardOutput = string.Empty, StandardError = "无法启动进程" };
                    }
                }
                catch (Exception ex)
                {
                    return new ProcessResult { ExitCode = -1, StandardOutput = string.Empty, StandardError = ex.Message };
                }

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                // 无进展计时的起点：进程启动即视为「最后一次有输出」。
                Interlocked.Exchange(ref _lastOutputStamp, Stopwatch.GetTimestamp());

                var sw = Stopwatch.StartNew();
                long limitMs = 0;
                long lastOutputAgoAtBreach = 0;

                using (cancellationToken.Register(() =>
                {
                    cancelled = true;
                    KillTree(process);
                }))
                {
                    await Task.Run(() =>
                    {
                        int poll = (options != null && options.PollIntervalMs > 0) ? options.PollIntervalMs : 500;
                        while (true)
                        {
                            bool exited = false;
                            try { exited = process.WaitForExit(poll); }
                            catch { break; }
                            if (exited)
                            {
                                break;
                            }

                            if (options == null)
                            {
                                continue;
                            }

                            if (options.TotalTimeout.HasValue && sw.ElapsedMilliseconds >= (long)options.TotalTimeout.Value.TotalMilliseconds)
                            {
                                timedOut = true;
                                timeoutKind = "total";
                                limitMs = (long)options.TotalTimeout.Value.TotalMilliseconds;
                                lastOutputAgoAtBreach = MsSince(Volatile.Read(ref _lastOutputStamp));
                                LogTimeout(timeoutKind, limitMs, sw.ElapsedMilliseconds, lastOutputAgoAtBreach, options.Class, fileName);
                                KillTree(process);
                                break;
                            }

                            if (options.NoProgressTimeout.HasValue)
                            {
                                long idleMs = MsSince(Volatile.Read(ref _lastOutputStamp));
                                if (idleMs >= (long)options.NoProgressTimeout.Value.TotalMilliseconds)
                                {
                                    timedOut = true;
                                    timeoutKind = "no-progress";
                                    limitMs = (long)options.NoProgressTimeout.Value.TotalMilliseconds;
                                    lastOutputAgoAtBreach = idleMs;
                                    LogTimeout(timeoutKind, limitMs, sw.ElapsedMilliseconds, lastOutputAgoAtBreach, options.Class, fileName);
                                    KillTree(process);
                                    break;
                                }
                            }
                        }
                        // R-05：超时被杀后必须再调无参 WaitForExit，把异步输出缓冲清空后才允许读 ExitCode，
                        // 否则 StandardOutput/StandardError 会丢尾部，错误归因失真。正常退出时这一调用
                        // 顺带保证流式输出事件全部送达（官方推荐用法），行为与旧实现的等价收敛点相同。
                        try { process.WaitForExit(); } catch { }
                    }).ConfigureAwait(false);
                }

                int exitCode;
                try { exitCode = process.ExitCode; }
                catch { exitCode = cancelled ? -2 : -1; }

                if (cleanupFile != null)
                {
                    TryDelete(cleanupFile);
                }

                return new ProcessResult
                {
                    ExitCode = cancelled ? -2 : exitCode,
                    StandardOutput = stdout.ToString(),
                    StandardError = stderr.ToString(),
                    Cancelled = cancelled,
                    TimedOut = timedOut,
                    TimeoutKind = timedOut ? timeoutKind : null,
                    ElapsedMs = sw.ElapsedMilliseconds,
                    LastOutputAgoMs = MsSince(Volatile.Read(ref _lastOutputStamp))
                };
            }
        }

        /// <summary>P3 不静默：未指定超时策略的调用点记一条 Warn（_logger 为 null 时跳过）。</summary>
        private void WarnNoTimeoutPolicy(string fileName)
        {
            if (_logger == null)
            {
                return;
            }
            _logger.Warn("[TIMEOUT] 调用未指定超时策略，按 Generic(1800s) 处理: " + (fileName ?? string.Empty));
        }

        /// <summary>超时日志格式冻结于 DD §2.2 G7，勿改字段顺序。</summary>
        private void LogTimeout(string kind, long limitMs, long elapsedMs, long lastOutputAgoMs, OperationClass cls, string target)
        {
            if (_logger == null)
            {
                return;
            }
            string message =
                "[TIMEOUT] kind=" + kind +
                " limit=" + limitMs.ToString(CultureInfo.InvariantCulture) + "ms" +
                " elapsed=" + elapsedMs.ToString(CultureInfo.InvariantCulture) + "ms" +
                " lastOutputAgo=" + lastOutputAgoMs.ToString(CultureInfo.InvariantCulture) + "ms" +
                " op=" + cls.ToString() +
                " target=" + (target ?? string.Empty) +
                " action=kill-tree";
            _logger.Warn(message);
        }

        private static long MsSince(long stamp)
        {
            return (long)((Stopwatch.GetTimestamp() - stamp) * 1000.0 / Stopwatch.Frequency);
        }

        private void Emit(Action<string, LogLevel> onLine, string line, LogLevel level)
        {
            // 无进展判据：任何一行 stdout/stderr 到达即刷新时间戳（与 onLine 是否为空无关）。
            Interlocked.Exchange(ref _lastOutputStamp, Stopwatch.GetTimestamp());
            if (onLine == null)
            {
                return;
            }
            try { onLine(line, level); } catch { }
        }

        public static void KillTree(Process process)
        {
            if (process == null)
            {
                return;
            }

            int pid;
            try { pid = process.Id; } catch { return; }

            try
            {
                var killer = Process.Start(new ProcessStartInfo
                {
                    FileName = "taskkill.exe",
                    Arguments = "/PID " + pid + " /T /F",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                });
                if (killer != null)
                {
                    try { killer.WaitForExit(5000); } catch { }
                    try { killer.Dispose(); } catch { }
                }
            }
            catch
            {
                try { process.Kill(); } catch { }
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch { }
        }
    }
}
