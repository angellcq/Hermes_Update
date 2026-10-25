using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace HermesInstaller.Core
{
    /// <summary>
    /// 端点组（DD-M2 §2.2 G1，数值为冻结设计值，勿调序勿改号）。
    /// 与 Mirrors.BuildEndpoints() 的 Id 前缀一一对应（见 Mirrors.GroupsFor）。
    /// </summary>
    public enum EndpointGroup
    {
        Installer = 0,
        ReleaseApi = 1,
        Repo = 2,
        GitDeps = 3,
        Electron = 4,
        Builder = 5,
        Npm = 6,
        Pip = 7,
        Chromium = 8
    }

    /// <summary>单组网络状态：当前级 + 连败计数 + 冷却截止 + 最近一次失败样本（FR-4.2 判定依据）。</summary>
    public sealed class GroupState
    {
        public EndpointGroup Group { get; set; }
        public int Level { get; set; }
        public int ConsecutiveFailures { get; set; }
        public DateTime CooldownUntilUtc { get; set; }
        public string LastReason { get; set; }
        public long LastRateKbps { get; set; }

        public GroupState()
        {
            Group = EndpointGroup.Installer;
            Level = 0;
            ConsecutiveFailures = 0;
            CooldownUntilUtc = DateTime.MinValue;
            LastReason = string.Empty;
            LastRateKbps = -1;
        }

        public GroupState(EndpointGroup group) : this()
        {
            Group = group;
        }

        public GroupState Clone()
        {
            GroupState copy = new GroupState();
            copy.Group = Group;
            copy.Level = Level;
            copy.ConsecutiveFailures = ConsecutiveFailures;
            copy.CooldownUntilUtc = CooldownUntilUtc;
            copy.LastReason = LastReason;
            copy.LastRateKbps = LastRateKbps;
            return copy;
        }
    }

    /// <summary>network-cooldown.json 的 schema（DD-M2 §3）。时间统一 UTC 文本，便于跨实例比较。</summary>
    [DataContract]
    public sealed class NetworkCooldownRecord
    {
        [DataMember(Name = "schemaVersion")]
        public int SchemaVersion { get; set; }

        [DataMember(Name = "savedAt")]
        public string SavedAt { get; set; }

        [DataMember(Name = "groups")]
        public List<NetworkCooldownGroup> Groups { get; set; }
    }

    [DataContract]
    public sealed class NetworkCooldownGroup
    {
        [DataMember(Name = "group")]
        public string Group { get; set; }

        [DataMember(Name = "level")]
        public int Level { get; set; }

        [DataMember(Name = "failures")]
        public int Failures { get; set; }

        [DataMember(Name = "cooldownUntil")]
        public string CooldownUntil { get; set; }

        [DataMember(Name = "reason")]
        public string Reason { get; set; }

        [DataMember(Name = "rateKbps")]
        public long RateKbps { get; set; }
    }

    /// <summary>
    /// 分级网络源调度器（M2 G1，FR-4.1/4.2 的判定核心）：维护九个端点组的当前级，
    /// 按「连败 ≥2」或「实测速率低于 100KB/s 且预估传输 &gt; 5MB」判降级，降级后冷却
    /// 300s 不回试，冷却过后由 Recover 只回一级。跳号组（Npm/Pip/Chromium 无 L1）的
    /// 升降都按「实际存在的级」取最近值，不做 Level±1。
    /// 冷却状态落盘 state\network-cooldown.json 跨实例生效；读失败一律视为无记录 +
    /// Warn，绝不抛异常。组内状态变更全部走 _gate 串行。
    /// [NET] 行格式收口在 FormatNetLine（FR-4.3），由调用方取行后走 Logger.Info 落日志。
    /// </summary>
    public sealed class NetworkPlanner
    {
        private const int CooldownSeconds = 300;
        private const int FailureThreshold = 2;
        private const long RateFloorKbps = 100;
        private const long UnstableBytesThreshold = 5L * 1024 * 1024;

        /// <summary>E12-②：探针判「慢」的阈值。L0 可用但延迟超过它，而更高级端点更快时，
        /// 直接落到更高级（GitHub 慢 → 走国内镜像）。</summary>
        private const long SlowLatencyMs = 2000;
        private const string CooldownFileName = "network-cooldown.json";

        /// <summary>枚举序 = 落盘与 Snapshot 的固定顺序，保证输出稳定可 diff。</summary>
        private static readonly EndpointGroup[] GroupOrder =
        {
            EndpointGroup.Installer, EndpointGroup.ReleaseApi, EndpointGroup.Repo,
            EndpointGroup.GitDeps, EndpointGroup.Electron, EndpointGroup.Builder,
            EndpointGroup.Npm, EndpointGroup.Pip, EndpointGroup.Chromium
        };

        private readonly Logger _logger;
        private readonly Settings _settings;
        private readonly object _gate = new object();
        private readonly Dictionary<EndpointGroup, GroupState> _states;
        private long _expectedBytes;

        public NetworkPlanner(Logger logger, Settings settings)
        {
            _logger = logger;
            _settings = settings;
            _states = new Dictionary<EndpointGroup, GroupState>();
            for (int i = 0; i < GroupOrder.Length; i++)
            {
                _states[GroupOrder[i]] = new GroupState(GroupOrder[i]);
            }
        }

        /// <summary>冷却状态文件路径（本工具自己的 state 目录，随 HermesPaths.StateDir）。</summary>
        public static string CooldownPath
        {
            get { return Path.Combine(HermesPaths.StateDir, CooldownFileName); }
        }

        /// <summary>调用方在操作开始前声明本次预估传输量；只有超过 5MB 才参与速率判定（R-M2-06）。</summary>
        public void SetExpectedTransferSize(long bytes)
        {
            lock (_gate)
            {
                _expectedBytes = bytes > 0 ? bytes : 0;
            }
        }

        /// <summary>
        /// E12-②（2026-10-25 14:10 事故）：把操作开工前探针的结果折算成各组当前级。
        /// 规则：
        ///   • 组内端点全部不可用 → 记一次失败，够阈值就降级（落到组内实际存在的下一级）；
        ///   • 当前级可用且不慢（≤ SlowLatencyMs）→ 记成功，级不变；
        ///   • 当前级不可用或慢、而更高级可用 → 直接落到该级（不做 Level±1 逐级爬）。
        /// 这样「GitHub 慢/连不上」在升级开工前就被判成「走镜像」，而不是等传输时才发现。
        /// 只改内存级；LoadCooldowns / SaveCooldowns 由调用方负责。
        /// </summary>
        public void ApplyProbeResults(IEnumerable<NetworkResult> results, long expectedBytes)
        {
            if (results == null)
            {
                return;
            }
            var snapshot = new List<NetworkResult>(results);
            lock (_gate)
            {
                if (expectedBytes > 0)
                {
                    _expectedBytes = expectedBytes;
                }
                for (int i = 0; i < GroupOrder.Length; i++)
                {
                    EndpointGroup group = GroupOrder[i];
                    NetworkResult chosen = PickProbeWinner(group, snapshot);
                    if (chosen == null)
                    {
                        RecordFailure(group, "probe: 组内端点全部不可用", -1);
                        if (ShouldDowngrade(group))
                        {
                            Downgrade(group, "probe: 组内端点全部不可用");
                        }
                        continue;
                    }
                    if (chosen.Endpoint.Level != _states[group].Level)
                    {
                        SetLevel(group, chosen.Endpoint.Level, string.Format(
                            "probe: 选 {0}（{1}ms）", chosen.Endpoint.Id, chosen.LatencyMs));
                    }
                    else
                    {
                        RecordSuccess(group);
                    }
                }
            }
        }

        /// <summary>E13：外部探测（git 传输层）给出的强制级。只改内存，调用方负责 SaveCooldowns。</summary>
        public void ForceLevel(EndpointGroup group, int level, string reason)
        {
            lock (_gate)
            {
                SetLevel(group, level, reason);
            }
        }

        /// <summary>
        /// 组内探针优胜者：先按「级最低、同级延迟最低」取官方优先的结果；若该结果超过
        /// SlowLatencyMs 且存在可用的更高级端点，则改取最快的高级端点（GitHub 慢 → 镜像）。
        /// </summary>
        private static NetworkResult PickProbeWinner(EndpointGroup group, List<NetworkResult> results)
        {
            List<NetworkEndpoint> endpoints = Mirrors.GroupsFor(group);
            if (endpoints.Count == 0)
            {
                return null;
            }
            NetworkResult best = null;
            NetworkResult fastestHigher = null;
            for (int i = 0; i < results.Count; i++)
            {
                NetworkResult result = results[i];
                if (result == null || !result.Ok || result.Endpoint == null || result.Endpoint.Id == null)
                {
                    continue;
                }
                if (!BelongsTo(endpoints, result.Endpoint.Id))
                {
                    continue;
                }
                if (best == null
                    || result.Endpoint.Level < best.Endpoint.Level
                    || (result.Endpoint.Level == best.Endpoint.Level && result.LatencyMs < best.LatencyMs))
                {
                    best = result;
                }
                if (result.Endpoint.Level > 0
                    && (fastestHigher == null || result.LatencyMs < fastestHigher.LatencyMs))
                {
                    fastestHigher = result;
                }
            }
            if (best == null)
            {
                return null;
            }
            // E12 实测修正：L0 只是「慢」（>SlowLatencyMs）时，只有更高级端点**确实更快**
            // 才切过去。首版没比较延迟，出现 repo-0 2116ms → 切到 repo-2 4278ms 的倒挂
            // （还顺手把 HERMES_REPO_URL 改成了 ghproxy）。
            if (best.Endpoint.Level == 0 && best.LatencyMs > SlowLatencyMs
                && fastestHigher != null && fastestHigher.LatencyMs < best.LatencyMs)
            {
                return fastestHigher;
            }
            return best;
        }

        private static bool BelongsTo(List<NetworkEndpoint> endpoints, string id)
        {
            for (int i = 0; i < endpoints.Count; i++)
            {
                if (string.Equals(endpoints[i].Id, id, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>直接设级（探针结论，不逐级爬），冷却 300s 并立即落盘。</summary>
        private void SetLevel(EndpointGroup group, int level, string reason)
        {
            GroupState state = _states[group];
            state.Level = level;
            state.CooldownUntilUtc = DateTime.UtcNow.AddSeconds(CooldownSeconds);
            state.ConsecutiveFailures = 0;
            state.LastReason = string.IsNullOrEmpty(reason) ? "probe" : reason;
            state.LastRateKbps = -1;
            SaveCooldownsCore();
        }

        /// <summary>读取冷却状态；文件缺失 / 解析失败 / schema 超版本 → 视为无记录，绝不抛。</summary>
        public void LoadCooldowns()
        {
            try
            {
                lock (_gate)
                {
                    string path = CooldownPath;
                    if (!File.Exists(path))
                    {
                        return;
                    }

                    NetworkCooldownRecord record;
                    using (var stream = File.OpenRead(path))
                    {
                        var serializer = new DataContractJsonSerializer(typeof(NetworkCooldownRecord));
                        record = serializer.ReadObject(stream) as NetworkCooldownRecord;
                    }

                    if (record == null)
                    {
                        Warn("[NET] 冷却状态文件解析结果为空，按无记录处理: " + path);
                        return;
                    }
                    if (record.SchemaVersion > 1)
                    {
                        Warn("[NET] network-cooldown.json schemaVersion=" + record.SchemaVersion
                            + " 高于当前理解（1），按无记录处理。");
                        return;
                    }
                    if (record.Groups == null)
                    {
                        return;
                    }

                    for (int i = 0; i < record.Groups.Count; i++)
                    {
                        ApplySavedGroup(record.Groups[i]);
                    }
                }
            }
            catch (Exception ex)
            {
                Warn("[NET] 读取冷却状态失败，按无记录处理: " + ex.Message);
            }
        }

        /// <summary>落盘当前各组状态（先写 .tmp 再替换，失败只 Warn，绝不抛）。</summary>
        public void SaveCooldowns()
        {
            lock (_gate)
            {
                SaveCooldownsCore();
            }
        }

        /// <summary>组当前级（0=官方直连）。冷却是否已过不影响本值，回级由 Recover 显式触发。</summary>
        public int CurrentLevel(EndpointGroup group)
        {
            lock (_gate)
            {
                return _states[group].Level;
            }
        }

        /// <summary>该组从当前级起的候选端点，按级升序（同级按 Id 排序保证稳定）。</summary>
        public List<NetworkEndpoint> CandidatesFor(EndpointGroup group)
        {
            int currentLevel;
            lock (_gate)
            {
                currentLevel = _states[group].Level;
            }
            List<NetworkEndpoint> all = Mirrors.GroupsFor(group);
            all.Sort(CompareByLevel);
            List<NetworkEndpoint> result = new List<NetworkEndpoint>();
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].Level >= currentLevel)
                {
                    result.Add(all[i]);
                }
            }
            return result;
        }

        /// <summary>本轮成功：连败计数清零，级不变；旧速率样本一并作废，避免脏样本驱动后续判定。</summary>
        public void RecordSuccess(EndpointGroup group)
        {
            lock (_gate)
            {
                GroupState state = _states[group];
                state.ConsecutiveFailures = 0;
                state.LastRateKbps = -1;
            }
        }

        /// <summary>记一次失败：计数 +1 并留存原因与速率样本（rateKbps 传 -1 表示无速率样本，如 git 端点）。</summary>
        public void RecordFailure(EndpointGroup group, string reason, long rateKbps)
        {
            lock (_gate)
            {
                GroupState state = _states[group];
                state.ConsecutiveFailures = state.ConsecutiveFailures + 1;
                state.LastReason = string.IsNullOrEmpty(reason) ? "unknown" : reason;
                state.LastRateKbps = rateKbps;
            }
        }

        /// <summary>
        /// 是否该降级（FR-4.2）：已在最低级 → 否；连败 ≥2 → 是；
        /// 真实测到速率（≥0）低于 100KB/s 且预估传输 &gt; 5MB → 是；其余否。
        /// </summary>
        public bool ShouldDowngrade(EndpointGroup group)
        {
            lock (_gate)
            {
                GroupState state = _states[group];
                if (MaxLevelOf(group) <= state.Level)
                {
                    return false;
                }
                if (state.ConsecutiveFailures >= FailureThreshold)
                {
                    return true;
                }
                if (state.LastRateKbps >= 0 && state.LastRateKbps < RateFloorKbps
                    && _expectedBytes > UnstableBytesThreshold)
                {
                    return true;
                }
                return false;
            }
        }

        /// <summary>可否回级：冷却已过且存在更低一级（按实际存在的级）。</summary>
        public bool CanRecover(EndpointGroup group)
        {
            lock (_gate)
            {
                GroupState state = _states[group];
                if (state.Level <= 0 || DateTime.UtcNow < state.CooldownUntilUtc)
                {
                    return false;
                }
                return PrevLevelOf(group, state.Level) >= 0;
            }
        }

        /// <summary>
        /// 降一级：落到「实际存在的下一级最小值」（Npm 从 0 直接到 2），冷却 +300s，
        /// 连败清零并立即落盘。已在最低级时不做任何事。
        /// </summary>
        public void Downgrade(EndpointGroup group, string reason)
        {
            lock (_gate)
            {
                GroupState state = _states[group];
                int next = NextLevelOf(group, state.Level);
                if (next == int.MaxValue || next <= state.Level)
                {
                    return;
                }
                state.Level = next;
                state.CooldownUntilUtc = DateTime.UtcNow.AddSeconds(CooldownSeconds);
                state.ConsecutiveFailures = 0;
                if (!string.IsNullOrEmpty(reason))
                {
                    state.LastReason = reason;
                }
                SaveCooldownsCore();
            }
        }

        /// <summary>回一级（只回一级，落到实际存在的上一级最大值）：冷却未过或已在 L0 时不做任何事。</summary>
        public void Recover(EndpointGroup group)
        {
            lock (_gate)
            {
                GroupState state = _states[group];
                if (state.Level <= 0 || DateTime.UtcNow < state.CooldownUntilUtc)
                {
                    return;
                }
                int prev = PrevLevelOf(group, state.Level);
                if (prev < 0)
                {
                    return;
                }
                state.Level = prev;
                state.ConsecutiveFailures = 0;
                SaveCooldownsCore();
            }
        }

        /// <summary>九组状态的只读快照（深拷贝，外部改动不影响内部状态）。</summary>
        public List<GroupState> Snapshot()
        {
            var copy = new List<GroupState>();
            lock (_gate)
            {
                for (int i = 0; i < GroupOrder.Length; i++)
                {
                    copy.Add(_states[GroupOrder[i]].Clone());
                }
            }
            return copy;
        }

        /// <summary>
        /// [NET] 结构化行（FR-4.3，格式冻结）：九个字段一个都不能省，rate=-1 输出 rate=-。
        /// 空字符串字段输出占位 "-"，保证行始终可按 " key=" 切分解析。
        /// </summary>
        public string FormatNetLine(string kind, EndpointGroup group, string reason,
                                    string op, string target, int attempt,
                                    long elapsedMs, long rateKbps, string action)
        {
            string rate;
            if (rateKbps < 0)
            {
                rate = "-";
            }
            else
            {
                rate = rateKbps.ToString(CultureInfo.InvariantCulture) + "KB/s";
            }
            return "[NET] kind=" + NullSafe(kind)
                + " group=" + group.ToString()
                + " reason=" + NullSafe(reason)
                + " op=" + NullSafe(op)
                + " target=" + NullSafe(target)
                + " attempt=" + attempt.ToString(CultureInfo.InvariantCulture)
                + " elapsed=" + elapsedMs.ToString(CultureInfo.InvariantCulture) + "ms"
                + " rate=" + rate
                + " action=" + NullSafe(action);
        }

        /// <summary>把落盘的一条组记录恢复进内存（仅接受本组实际存在的级，防手改文件破坏不变量）。</summary>
        private void ApplySavedGroup(NetworkCooldownGroup saved)
        {
            if (saved == null)
            {
                return;
            }
            EndpointGroup group;
            if (!TryParseGroup(saved.Group, out group))
            {
                return;
            }
            if (!LevelExists(group, saved.Level))
            {
                Warn("[NET] 冷却状态里 " + saved.Group + " 的 level=" + saved.Level
                    + " 不是该组实际存在的级，跳过恢复。");
                return;
            }
            GroupState state = _states[group];
            state.Level = saved.Level;
            state.ConsecutiveFailures = saved.Failures > 0 ? saved.Failures : 0;
            DateTime cooldown;
            if (TryParseStamp(saved.CooldownUntil, out cooldown))
            {
                state.CooldownUntilUtc = cooldown;
            }
            state.LastReason = saved.Reason != null ? saved.Reason : string.Empty;
            state.LastRateKbps = saved.RateKbps;
        }

        /// <summary>SaveCooldowns 的实现体，调用方必须已持有 _gate。任何异常只 Warn。</summary>
        private void SaveCooldownsCore()
        {
            try
            {
                NetworkCooldownRecord record = new NetworkCooldownRecord();
                record.SchemaVersion = 1;
                record.SavedAt = StampText(DateTime.UtcNow);
                record.Groups = new List<NetworkCooldownGroup>();
                for (int i = 0; i < GroupOrder.Length; i++)
                {
                    GroupState state = _states[GroupOrder[i]];
                    // 全空态（L0、零连败、无冷却）不落盘，保持文件只讲有故事的组。
                    if (state.Level == 0 && state.ConsecutiveFailures == 0
                        && state.CooldownUntilUtc == DateTime.MinValue)
                    {
                        continue;
                    }
                    NetworkCooldownGroup saved = new NetworkCooldownGroup();
                    saved.Group = GroupOrder[i].ToString();
                    saved.Level = state.Level;
                    saved.Failures = state.ConsecutiveFailures;
                    saved.CooldownUntil = state.CooldownUntilUtc == DateTime.MinValue
                        ? string.Empty
                        : StampText(state.CooldownUntilUtc);
                    saved.Reason = state.LastReason;
                    saved.RateKbps = state.LastRateKbps;
                    record.Groups.Add(saved);
                }

                HermesPaths.EnsureAppData();
                string path = CooldownPath;
                string temp = path + ".tmp";
                using (var stream = File.Create(temp))
                {
                    var serializer = new DataContractJsonSerializer(typeof(NetworkCooldownRecord));
                    serializer.WriteObject(stream, record);
                }
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
                File.Move(temp, path);
            }
            catch (Exception ex)
            {
                Warn("[NET] 保存冷却状态失败: " + ex.Message);
            }
        }

        /// <summary>组内实际存在的最高级；组为空时返回 int.MinValue（使「已在最低级」恒成立）。</summary>
        private static int MaxLevelOf(EndpointGroup group)
        {
            int max = int.MinValue;
            List<NetworkEndpoint> endpoints = Mirrors.GroupsFor(group);
            for (int i = 0; i < endpoints.Count; i++)
            {
                if (endpoints[i].Level > max)
                {
                    max = endpoints[i].Level;
                }
            }
            return max;
        }

        /// <summary>实际存在的上一级最大值（比当前级小的级里取最大的）；不存在返回 -1。</summary>
        private static int PrevLevelOf(EndpointGroup group, int currentLevel)
        {
            int prev = -1;
            List<NetworkEndpoint> endpoints = Mirrors.GroupsFor(group);
            for (int i = 0; i < endpoints.Count; i++)
            {
                if (endpoints[i].Level < currentLevel && endpoints[i].Level > prev)
                {
                    prev = endpoints[i].Level;
                }
            }
            return prev;
        }

        /// <summary>实际存在的下一级最小值（比当前级大的级里取最小的）；不存在返回 int.MaxValue。</summary>
        private static int NextLevelOf(EndpointGroup group, int currentLevel)
        {
            int next = int.MaxValue;
            List<NetworkEndpoint> endpoints = Mirrors.GroupsFor(group);
            for (int i = 0; i < endpoints.Count; i++)
            {
                if (endpoints[i].Level > currentLevel && endpoints[i].Level < next)
                {
                    next = endpoints[i].Level;
                }
            }
            return next;
        }

        private static bool LevelExists(EndpointGroup group, int level)
        {
            List<NetworkEndpoint> endpoints = Mirrors.GroupsFor(group);
            for (int i = 0; i < endpoints.Count; i++)
            {
                if (endpoints[i].Level == level)
                {
                    return true;
                }
            }
            return false;
        }

        private static int CompareByLevel(NetworkEndpoint left, NetworkEndpoint right)
        {
            int byLevel = left.Level.CompareTo(right.Level);
            if (byLevel != 0)
            {
                return byLevel;
            }
            return string.Compare(left.Id, right.Id, StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryParseGroup(string text, out EndpointGroup group)
        {
            group = EndpointGroup.Installer;
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }
            string trimmed = text.Trim();
            for (int i = 0; i < GroupOrder.Length; i++)
            {
                if (string.Equals(GroupOrder[i].ToString(), trimmed, StringComparison.OrdinalIgnoreCase))
                {
                    group = GroupOrder[i];
                    return true;
                }
            }
            return false;
        }

        /// <summary>时间统一 "yyyy-MM-dd HH:mm:ss"（UTC 墙钟）；Kind 不参与比较，按 tick 判先后即可。</summary>
        private static string StampText(DateTime value)
        {
            return value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        private static bool TryParseStamp(string text, out DateTime value)
        {
            value = DateTime.MinValue;
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }
            return DateTime.TryParseExact(text, "yyyy-MM-dd HH:mm:ss",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out value)
                || DateTime.TryParse(text, out value);
        }

        private static string NullSafe(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "-";
            }
            return text.Trim();
        }

        private void Warn(string message)
        {
            if (_logger != null)
            {
                _logger.Warn(message);
            }
        }
    }
}
