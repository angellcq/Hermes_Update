using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Threading;

namespace HermesInstaller.Core
{
    /// <summary>run.json 的 schema（DD §3.2）：当前实例的运行状态 + 模式版本。</summary>
    [DataContract]
    public sealed class RunStateRecord
    {
        [DataMember(Name = "schemaVersion")] public int SchemaVersion { get; set; }
        [DataMember(Name = "pid")] public int Pid { get; set; }
        [DataMember(Name = "operation")] public string Operation { get; set; }
        [DataMember(Name = "stageName")] public string StageName { get; set; }
        [DataMember(Name = "startedAt")] public string StartedAt { get; set; }
        [DataMember(Name = "heartbeatAt")] public string HeartbeatAt { get; set; }
    }

    /// <summary>
    /// 运行状态落盘（DD §2.2 G6，T-HU-DEV-4）：state\run.json 记录当前实例正在执行的操作与阶段，
    /// 供第二个实例启动时给出「对方在干什么、进行多久」的提示（Program.cs 锁占位分支）。
    /// 生命周期：Begin（写入当前 pid）→ UpdateStage（阶段切换即刷新）/ Heartbeat（内置 60s
    /// 定时心跳，防长阶段串行时误判陈旧）→ Clear（操作正常结束即删文件；进程崩溃留下陈旧
    /// 文件由「心跳超龄」+「pid 不存活」双重判定兜住，不会误报）。
    /// 所有读写 try/catch + 记 Warn，绝不抛给调用方（DD §2.3：状态文件失败一律功能降级）。
    /// schemaVersion &gt; 1 的文件一律忽略且不报错（向前兼容）。
    /// </summary>
    public sealed class RunStateStore
    {
        private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(60);

        private readonly Logger _logger;
        private readonly object _gate = new object();
        private RunStateRecord _current;
        private Timer _timer;

        public RunStateStore() : this(null)
        {
        }

        public RunStateStore(Logger logger)
        {
            _logger = logger;
        }

        /// <summary>操作开始：登记当前进程 pid 并落盘，同时启动周期心跳。</summary>
        public void Begin(string operation)
        {
            try
            {
                var record = new RunStateRecord();
                record.SchemaVersion = 1;
                record.Pid = Process.GetCurrentProcess().Id;
                record.Operation = string.IsNullOrWhiteSpace(operation) ? "未知操作" : operation.Trim();
                record.StageName = "准备";
                record.StartedAt = NowText();
                record.HeartbeatAt = record.StartedAt;

                lock (_gate)
                {
                    _current = record;
                    StopHeartbeatNoLock();
                    _timer = new Timer(delegate(object state) { Heartbeat(); },
                        null, HeartbeatInterval, HeartbeatInterval);
                    WriteFile(record);
                }
            }
            catch (Exception ex)
            {
                Warn("开始记录运行状态失败: " + ex.Message);
            }
        }

        /// <summary>阶段切换：更新 stageName 并顺带刷新心跳（长阶段之间也保持新鲜）。</summary>
        public void UpdateStage(string stageName)
        {
            try
            {
                lock (_gate)
                {
                    if (_current == null)
                    {
                        return;
                    }
                    if (!string.IsNullOrWhiteSpace(stageName))
                    {
                        _current.StageName = stageName.Trim();
                    }
                    _current.HeartbeatAt = NowText();
                    WriteFile(_current);
                }
            }
            catch (Exception ex)
            {
                Warn("更新运行状态阶段失败: " + ex.Message);
            }
        }

        /// <summary>心跳：仅刷新 heartbeatAt（由内置定时器与调用方共同驱动）。</summary>
        public void Heartbeat()
        {
            try
            {
                lock (_gate)
                {
                    if (_current == null)
                    {
                        return;
                    }
                    _current.HeartbeatAt = NowText();
                    WriteFile(_current);
                }
            }
            catch (Exception ex)
            {
                Warn("刷新运行状态心跳失败: " + ex.Message);
            }
        }

        /// <summary>读另一实例的状态记录；读不到 / schema 超版本 / 是自己的 pid → null。</summary>
        public RunStateRecord ReadOther()
        {
            try
            {
                RunStateRecord record = ReadFile();
                if (record == null)
                {
                    return null;
                }
                if (record.SchemaVersion > 1)
                {
                    Warn("run.json schemaVersion=" + record.SchemaVersion + " 高于当前理解（1），忽略。");
                    return null;
                }
                if (record.Pid == Process.GetCurrentProcess().Id)
                {
                    return null;
                }
                return record;
            }
            catch (Exception ex)
            {
                Warn("读取 run.json 失败: " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// 判断「另一实例是否正经在执行」：心跳未超龄且对方 pid 仍存活才返回 true。
        /// 命名按 DD §2.2 G6 冻结：心跳超龄（或文件缺失 / schema 超版本 / pid 已死）→ false。
        /// out 参数为读到的记录（可能为 null，或可用于排障的陈旧记录）。
        /// </summary>
        public bool TryReadStale(TimeSpan maxAge, out RunStateRecord rec)
        {
            rec = null;
            try
            {
                RunStateRecord parsed = ReadFile();
                rec = parsed;
                if (parsed == null || parsed.SchemaVersion > 1)
                {
                    return false;
                }
                if (parsed.Pid == Process.GetCurrentProcess().Id)
                {
                    return false;
                }

                DateTime heartbeat;
                if (!TryParseStamp(parsed.HeartbeatAt, out heartbeat))
                {
                    return false;
                }
                if (DateTime.Now - heartbeat > maxAge)
                {
                    return false;
                }
                if (!IsPidAlive(parsed.Pid))
                {
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                Warn("检查 run.json 是否陈旧失败: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Program.cs 锁占位分支专用（DD §2.2 G10）：30 分钟心跳阈值一次性判断
        /// 「另一实例是否正经在执行」。任何异常/陈旧/己方记录都返回 null，绝不打扰弹窗路径。
        /// </summary>
        public static RunStateRecord ReadOtherStaleTolerant()
        {
            RunStateRecord record;
            TryReadStaleStatic(TimeSpan.FromMinutes(30), out record);
            return record;
        }

        /// <summary>静态版 TryReadStale（不在实例心跳生命周期内的只读检查）。</summary>
        public static bool TryReadStaleStatic(TimeSpan maxAge, out RunStateRecord rec)
        {
            rec = null;
            try
            {
                if (!File.Exists(HermesPaths.RunStatePath))
                {
                    return false;
                }
                RunStateRecord parsed;
                using (var stream = File.OpenRead(HermesPaths.RunStatePath))
                {
                    var serializer = new DataContractJsonSerializer(typeof(RunStateRecord));
                    parsed = serializer.ReadObject(stream) as RunStateRecord;
                }
                rec = parsed;
                if (parsed == null || parsed.SchemaVersion > 1)
                {
                    return false;
                }
                if (parsed.Pid == Process.GetCurrentProcess().Id)
                {
                    return false;
                }

                DateTime heartbeat;
                if (string.IsNullOrEmpty(parsed.HeartbeatAt)
                    || !DateTime.TryParseExact(parsed.HeartbeatAt, "yyyy-MM-dd HH:mm:ss",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out heartbeat))
                {
                    return false;
                }
                if (DateTime.Now - heartbeat > maxAge)
                {
                    WarnStatic("[STATE] 下另一实例 run.json 心跳超龄（> "
                        + ((int)maxAge.TotalMinutes) + " 分），按陈旧状态忽略，不弹「正在执行」。");
                    return false;
                }
                if (!IsPidAlive(parsed.Pid))
                {
                    WarnStatic("[STATE] 下另一实例 pid=" + parsed.Pid + " 已不存活，按陈旧状态忽略。");
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                WarnStatic("[STATE] 读取 run.json 异常，按无活动实例处理: " + ex.Message);
                return false;
            }
        }

        /// <summary>静态路径（TryReadStaleStatic）的日志出口。失败静默降级，不影响弹窗主路径。</summary>
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
                // 日志器不可用时只能放弃；弹窗路径不因日志失败受阻。
            }
        }

        /// <summary>操作结束即删除 run.json（含失败/取消的正常退出路径）；心跳定时器一并停掉。</summary>
        public void Clear()
        {
            try
            {
                lock (_gate)
                {
                    StopHeartbeatNoLock();
                    _current = null;
                    if (File.Exists(HermesPaths.RunStatePath))
                    {
                        File.Delete(HermesPaths.RunStatePath);
                    }
                }
            }
            catch (Exception ex)
            {
                Warn("清理运行状态失败: " + ex.Message);
            }
        }

        private void WriteFile(RunStateRecord record)
        {
            HermesPaths.EnsureAppData();
            string temp = HermesPaths.RunStatePath + ".tmp";
            using (var stream = File.Create(temp))
            {
                var serializer = new DataContractJsonSerializer(typeof(RunStateRecord));
                serializer.WriteObject(stream, record);
            }
            if (File.Exists(HermesPaths.RunStatePath))
            {
                File.Delete(HermesPaths.RunStatePath);
            }
            File.Move(temp, HermesPaths.RunStatePath);
        }

        private RunStateRecord ReadFile()
        {
            if (!File.Exists(HermesPaths.RunStatePath))
            {
                return null;
            }
            using (var stream = File.OpenRead(HermesPaths.RunStatePath))
            {
                var serializer = new DataContractJsonSerializer(typeof(RunStateRecord));
                return serializer.ReadObject(stream) as RunStateRecord;
            }
        }

        private void StopHeartbeatNoLock()
        {
            if (_timer != null)
            {
                _timer.Dispose();
                _timer = null;
            }
        }

        /// <summary>pid 存活判定：GetProcessById 对已退出的 pid 抛异常属常态，不算索引坏信号。</summary>
        private static bool IsPidAlive(int pid)
        {
            if (pid <= 0)
            {
                return false;
            }
            try
            {
                using (Process process = Process.GetProcessById(pid))
                {
                    return !process.HasExited;
                }
            }
            catch
            {
                return false;
            }
        }

        private static bool TryParseStamp(string text, out DateTime value)
        {
            if (string.IsNullOrEmpty(text))
            {
                value = DateTime.MinValue;
                return false;
            }
            return DateTime.TryParseExact(text, "yyyy-MM-dd HH:mm:ss",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out value)
                || DateTime.TryParse(text, out value);
        }

        private static string NowText()
        {
            return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        private void Warn(string message)
        {
            if (_logger != null)
            {
                _logger.Warn("[STATE] " + message);
            }
        }
    }
}
