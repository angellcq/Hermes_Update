using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    }

    /// <summary>
    /// Runs child processes with streamed output. PowerShell wrappers are written with a
    /// UTF-8 BOM and switch the console encodings so Chinese/Unicode output survives.
    /// </summary>
    public sealed class ProcessRunner
    {
        private readonly string _runDirectory;

        public ProcessRunner()
        {
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
            string wrapperPath = Path.Combine(_runDirectory, "wrap-" + Guid.NewGuid().ToString("N") + ".ps1");
            string wrapper =
                "$ErrorActionPreference = 'Continue'\r\n" +
                "try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }\r\n" +
                "try { $OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }\r\n" +
                "try { $env:PYTHONUTF8 = '1'; $env:PYTHONIOENCODING = 'utf-8' } catch { }\r\n" +
                invocation + "\r\n" +
                "exit $LASTEXITCODE\r\n";
            File.WriteAllText(wrapperPath, wrapper, new UTF8Encoding(true));

            var args = "-NoProfile -NoLogo -NonInteractive -ExecutionPolicy Bypass -File " + QuoteArg(wrapperPath);
            return RunProcessAsync("powershell.exe", args, workingDirectory, environment, onLine, cancellationToken, wrapperPath);
        }

        public async Task<ProcessResult> RunProcessAsync(
            string fileName,
            string arguments,
            string workingDirectory,
            IDictionary<string, string> environment,
            Action<string, LogLevel> onLine,
            CancellationToken cancellationToken,
            string cleanupFile = null)
        {
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            bool cancelled = false;

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

                using (cancellationToken.Register(() =>
                {
                    cancelled = true;
                    KillTree(process);
                }))
                {
                    await Task.Run(() =>
                    {
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
                    Cancelled = cancelled
                };
            }
        }

        private static void Emit(Action<string, LogLevel> onLine, string line, LogLevel level)
        {
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
