using System;
using System.IO;
using System.Text;

namespace HermesInstaller.Core
{
    public enum LogLevel
    {
        Info,
        Warn,
        Error,
        Success,
        Stage
    }

    public sealed class LogEntry
    {
        public DateTime Time { get; set; }
        public LogLevel Level { get; set; }
        public string Message { get; set; }
    }

    public sealed class Logger : IDisposable
    {
        private readonly object _gate = new object();
        private StreamWriter _writer;
        private bool _disposed;

        public event Action<LogEntry> Logged;

        public string LogDirectory { get; private set; }
        public string LogPath { get; private set; }

        public Logger()
        {
            LogDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "HermesInstaller",
                "logs");
            Directory.CreateDirectory(LogDirectory);
        }

        public void Start(string operationName)
        {
            lock (_gate)
            {
                CloseWriter();
                string safeName = string.IsNullOrWhiteSpace(operationName) ? "session" : Sanitize(operationName);
                LogPath = Path.Combine(LogDirectory, string.Format("{0}-{1:yyyyMMdd-HHmmss}.log", safeName, DateTime.Now));
                _writer = new StreamWriter(LogPath, false, new UTF8Encoding(false))
                {
                    AutoFlush = true
                };
            }
            Info("日志文件: " + LogPath);
        }

        public void Info(string message) { Write(LogLevel.Info, message); }
        public void Warn(string message) { Write(LogLevel.Warn, message); }
        public void Error(string message) { Write(LogLevel.Error, message); }
        public void Success(string message) { Write(LogLevel.Success, message); }
        public void Stage(string message) { Write(LogLevel.Stage, message); }

        public void Write(LogLevel level, string message)
        {
            if (message == null)
            {
                return;
            }

            string safe = Redactor.Redact(message);
            var entry = new LogEntry { Time = DateTime.Now, Level = level, Message = safe };

            lock (_gate)
            {
                if (_writer != null)
                {
                    try
                    {
                        _writer.WriteLine("[{0:HH:mm:ss}] [{1}] {2}", entry.Time, LevelTag(level), safe);
                    }
                    catch
                    {
                        // Never let logging take down an operation.
                    }
                }
            }

            Action<LogEntry> handler = Logged;
            if (handler != null)
            {
                try { handler(entry); } catch { }
            }
        }

        /// <summary>Appends the tail of an external Hermes log, redacted, as a bracketed block.</summary>
        public void AppendExternalLog(string title, string path, int maxLines = 200)
        {
            AppendExternalLog(title, path, DateTime.MinValue, maxLines);
        }

        /// <summary>
        /// E13：只附录 <paramref name="sinceUtc"/> 之后的尾部行。Hermes 的 bootstrap.log
        /// 是跨月累积的（2026-10-25 的事故日志里贴进了 09-29 的 SOCKS 报错，纯误导），
        /// 所以默认按本轮操作时间窗过滤；文件 mtime 早于时间窗时整体跳过并注明。
        /// </summary>
        public void AppendExternalLog(string title, string path, DateTime sinceUtc, int maxLines)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return;
            }

            try
            {
                var fileInfo = new FileInfo(path);
                if (sinceUtc > DateTime.MinValue && fileInfo.LastWriteTimeUtc < sinceUtc.AddMinutes(-2))
                {
                    Write(LogLevel.Info, string.Format(
                        "----- {0} ({1}) 本轮无新增，已跳过（最后写入 {2:yyyy-MM-dd HH:mm:ss}）-----",
                        title, path, fileInfo.LastWriteTime));
                    return;
                }

                string[] lines = File.ReadAllLines(path);
                int start = Math.Max(0, lines.Length - maxLines);
                Write(LogLevel.Info, "----- " + title + " (" + path + ") -----");
                int appended = 0;
                for (int i = start; i < lines.Length; i++)
                {
                    if (!string.IsNullOrEmpty(lines[i]))
                    {
                        Write(LogLevel.Info, lines[i]);
                        appended++;
                    }
                }
                if (appended == 0)
                {
                    Write(LogLevel.Info, "(空)");
                }
                Write(LogLevel.Info, "----- " + title + " end -----");
            }
            catch (Exception ex)
            {
                Warn("读取外部日志失败: " + ex.Message);
            }
        }

        private void CloseWriter()
        {
            if (_writer != null)
            {
                try { _writer.Dispose(); } catch { }
                _writer = null;
            }
        }

        private static string LevelTag(LogLevel level)
        {
            switch (level)
            {
                case LogLevel.Warn: return "WARN";
                case LogLevel.Error: return "ERR ";
                case LogLevel.Success: return "OK  ";
                case LogLevel.Stage: return "STEP";
                default: return "INFO";
            }
        }

        private static string Sanitize(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }
            return name.Replace(' ', '_');
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            lock (_gate)
            {
                CloseWriter();
            }
        }
    }
}
