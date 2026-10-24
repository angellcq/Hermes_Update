# DD：Hermes Agent Windows 安装/升级工具 —— M2 分级网络源与完整性校验（详细设计）

| 字段 | 内容 |
|------|------|
| 文档版本 | v1.0 |
| 关联 HLD | `doc/HLD_Hermes_Agent_Windows_升级工具_M2_v1.0.md`（v1.0）§2.3 组件 G1–G8 |
| 回溯链 | 本 DD ← HLD-M2 §2.3 G1–G8 ← PRD v1.6 §4.1/§4.2/§4.4 的 FR-1.4 / 1.5 / FR-4.1 / 4.2 / 4.3 / 4.4 |
| 设计负责人 | Hermes |
| 证据基线 | 2026-10-24 实测：`UV_CACHE_DIR`/`npm cache`/`integrity`/`GIT_CONFIG_COUNT`/`insteadOf`/`[NET]`/`Level|Weight` 全 0 处；`install.ps1` 双源 sha256 一致 `bcb7c385…`；git 2.53 `insteadOf` 注入生效 |

**版本历史**

| 版本 | 日期 | 修改人 | 修改说明 |
|---|---|---|---|
| v1.0 | 2026-10-24 | Hermes | 初稿：G1–G8 代码级规格、改造点行号、验证命令、不改清单、卡切片 |

---

## 0. 本机环境实测事实（任务卡直接引用，勿重复探测）

| 项 | 实测值 |
|---|---|
| git 版本 | **2.53.0.windows.3**（支持 `GIT_CONFIG_COUNT`，需 2.31+） |
| `insteadOf` 注入验证 | `GIT_CONFIG_COUNT=1 GIT_CONFIG_KEY_0="url.https://ghproxy.net/https://github.com/.insteadOf" GIT_CONFIG_VALUE_0="https://github.com/"` → `git ls-remote --get-url https://github.com/NousResearch/hermes-agent.git` 输出镜像地址 ✅ |
| `install.ps1` 官方源 | `hermes-agent.nousresearch.com/install.ps1`，`Content-Length: 88886`，`Etag: "6b033ae5…"`（**非内容 sha256**） |
| `install.ps1` GitHub raw | `raw.githubusercontent.com/NousResearch/hermes-agent/main/scripts/install.ps1`，`Content-Length: 88886`，ETag 与官方**完全相同** |
| 两源内容 sha256 | 均为 **`bcb7c3857e7682c06954051012fa08c48e7bf687d37cf9b4edf84ea2a0919d67`** |
| `pm/lock.json` | 20 个包，每包多架构 artifacts `{sha256, url}`；由 PM 自校（本工具只读） |
| 现有缓存目录 | 仅 `<CacheDir>\electron-builder`（`NetworkPreparer.cs:36`） |
| 网络探测现状 | `NetworkProbe.cs:110` 超时 git 22s / http 12s；`SemaphoreSlim(4)` 并发 |
| `RepoCandidates` | `Mirrors.cs:27-29` = `ghproxy.net` → `gh-proxy.com` → `github.com`（**镜像优先，M2 要改为官方优先**） |
| 本机镜像实测 | gh-proxy 355ms、GitHub API 990ms、GitHub 主站 980ms、npmmirror 91ms、tuna 207ms；`raw.githubusercontent` 12s 超时、`ghproxy.net` 22s 超时 |

---

## 1. 模块职责

### 1.1 输入 / 输出

| 方向 | 内容 | 类型 | 来源/去向 | 约束 |
|---|---|---|---|---|
| 输入 | 端点候选表 | `List<NetworkEndpoint>`（含 `Level`） | `Mirrors.BuildEndpoints()` | 每组的 `Level` 必须连续（0,1,2 不跳号） |
| 输入 | 探测结果 | `NetworkResult`（含 `Ok`/`LatencyMs`/`Detail`） | `NetworkProbe.RunAsync` | 只读 |
| 输入 | 速率样本 | `long` KB/s | `NetworkProbe.MeasureThroughputAsync` | 失败返回 -1 |
| 输入 | 命令结果 | `ProcessResult`（含 `TimedOut`/`TimeoutKind`/`ExitCode`） | M1 `ProcessRunner` | 只读 |
| 输出 | 环境块 | `Dictionary<string,string>` | 子进程 `EnvironmentVariables` | 大小写不敏感；键不重复 |
| 输出 | 降级决策 | `bool` + 状态变更 | `InstallerEngine` 的 `RunWithFailoverAsync` | 幂等（同组同级只降一次） |
| 输出 | `[NET]` 行 | string → `Logger.Write(Info)` | 日志文件 | 格式冻结见 §2.2 API-M2-06 |
| 输出 | 冷却状态 | `state\network-cooldown.json` | 磁盘 | 带 `schemaVersion`；读失败视为无记录 |

### 1.2 核心逻辑（一次升级操作的完整时序）

```
RunUpgradeAsync（M1 已有骨架）
  ├─ [M1] serviceGuard.CaptureAsync          // 服务快照
  ├─ [M1] fingerprint.CaptureAsync            // 指纹
  ├─ [M1] StopHermesProcessesAsync            // 停进程
  ├─ [M2 新增] planner.LoadCooldowns()        // 读冷却状态
  ├─ [M2 改造] RunWithFailoverAsync(           // ← 替代原来单次 RunPowerShellAsync
  │      action = env => runner.RunPowerShellAsync("& hermes.cmd update --yes", env, budget),
  │      label  = "update")
  │    ├─ for round in 1..4:
  │    │    env = preparer.BuildEnvironment()          // 按各组当前级
  │    │    result = await action(env)
  │    │    if 成功 → 全组 RecordSuccess + SaveCooldowns → return result
  │    │    else:
  │    │        affected = InferGroups(result)  or  AllGroups()
  │    │        foreach g in affected:
  │    │            planner.RecordFailure(g, reason, rate)
  │    │            if planner.ShouldDowngrade(g):
  │    │                planner.Downgrade(g, reason)
  │    │                logger.Info(FormatNetLine("downgrade", …))
  │    └─ 全部失败 → return lastResult（交 M1 三态结论判 Failed）
  ├─ [M1] fingerprint.CaptureAsync (fpAfter)
  ├─ [M1] 桌面端重建（如需，用同一 env）
  ├─ [M1] serviceGuard.RestoreAsync
  └─ [M1] verifier.VerifyAsync → 三态结论
```

---

## 2. 类 / 函数设计

### 2.1 类图

```
        ┌──────────────────┐
        │  InstallerEngine │（改造 G6）
        │  RunWithFailover │
        └───┬──────────┬───┘
            │          │
            ▼          ▼
   ┌────────────────┐ ┌──────────────────┐
   │ NetworkPlanner │ │ NetworkPreparer  │
   │   （新 G1）     │ │   （改造 G4）     │
   └───┬────────┬───┘ └────────┬─────────┘
       │        │              │
       ▼        ▼              ▼
 ┌──────────┐ ┌────────────┐ ┌──────────────┐
 │ GroupState│ │  Mirrors   │ │  Settings    │
 │  （新）   │ │ （改造 G3） │ │ （改造 G7）   │
 └──────────┘ └─────┬──────┘ └──────────────┘
                    │
                    ▼
            ┌───────────────┐  ┌──────────────────┐
            │ NetworkProbe  │  │ VerifiedDownloader│
            │  （改造 G5）   │  │   （新 G2）        │
            └───────────────┘  └──────────────────┘
```

### 2.2 关键方法签名与伪代码

#### G1 `Core/NetworkPlanner.cs`（新增，约 320 行）

```csharp
public enum EndpointGroup
{
    Installer = 0, ReleaseApi = 1, Repo = 2, GitDeps = 3,
    Electron = 4, Builder = 5, Npm = 6, Pip = 7, Chromium = 8
}

public sealed class GroupState
{
    public EndpointGroup Group { get; set; }
    public int Level { get; set; }
    public int ConsecutiveFailures { get; set; }
    public DateTime CooldownUntilUtc { get; set; }
    public string LastReason { get; set; }
    public long LastRateKbps { get; set; }
}

public sealed class NetworkPlanner
{
    public NetworkPlanner(Logger logger, Settings settings);

    public void LoadCooldowns();
    public void SaveCooldowns();

    public int CurrentLevel(EndpointGroup group);
    public List<NetworkEndpoint> CandidatesFor(EndpointGroup group);

    public void RecordSuccess(EndpointGroup group);
    public void RecordFailure(EndpointGroup group, string reason, long rateKbps);

    public bool ShouldDowngrade(EndpointGroup group);
    public bool CanRecover(EndpointGroup group);
    public void Downgrade(EndpointGroup group, string reason);
    public void Recover(EndpointGroup group);

    public List<GroupState> Snapshot();
    public string FormatNetLine(string kind, EndpointGroup group, string reason,
                                string op, string target, int attempt,
                                long elapsedMs, long rateKbps, string action);
}
```

**`CurrentLevel` 与 `CandidatesFor` 伪代码**

```
CurrentLevel(g):
    state = _states[g]
    // 冷却已过 → 允许回上级（但只在本次操作第一次调用时生效，由 Recover 显式触发）
    return state.Level

CandidatesFor(g):
    all = Mirrors.GroupsFor(g).OrderBy(e => e.Level).ToList()
    cur = CurrentLevel(g)
    return all.Where(e => e.Level >= cur).ToList()   // 从当前级起，按级升序
```

**`ShouldDowngrade` 伪代码（阈值照 PRD FR-4.2）**

```
ShouldDowngrade(g):
    st = _states[g]
    if Mirrors.GroupsFor(g).Max(e => e.Level) <= st.Level: return false   // 已在最低级，无处可降
    if st.ConsecutiveFailures >= 2: return true                            // 连续 2 次失败
    if st.LastRateKbps >= 0 && st.LastRateKbps < 100 && _expectedBytes > 5MB: return true  // 不稳定
    return false
```

> `_expectedBytes` 由调用方在开始操作前用 `SetExpectedTransferSize(long)` 设置（升级操作传 50 MB 的保守估计；小操作不设 → 速率判定不生效，避免 R-M2-06 误判）。

**`Downgrade` / `Recover` 伪代码**

```
Downgrade(g, reason):
    st = _states[g]
    next = Mirrors.GroupsFor(g).Where(e => e.Level > st.Level).Min(e => e.Level)
    st.Level = next
    st.CooldownUntilUtc = DateTime.UtcNow.AddSeconds(300)     // PRD: 降级后 300s 内不回试
    st.ConsecutiveFailures = 0
    st.LastReason = reason
    SaveCooldowns()

Recover(g):
    st = _states[g]
    if st.Level == 0: return
    if DateTime.UtcNow < st.CooldownUntilUtc: return           // 冷却中
    st.Level = st.Level - 1                                   // 只回一级，本次操作内最多调用 1 次
    st.ConsecutiveFailures = 0
    SaveCooldowns()
```

**`FormatNetLine` 输出格式（冻结，照 PRD FR-4.3）**

```
[NET] kind=downgrade group=Repo reason=timeout op=update target=ghproxy.net attempt=2 elapsed=45200ms rate=0KB/s action=fallback
[NET] kind=probe group=Npm reason=ok op=update target=registry.npmmirror.com attempt=1 elapsed=380ms rate=1800KB/s action=use
[NET] kind=verify group=Installer reason=dual-match op=install-script target=hermes-agent.nousresearch.com attempt=1 elapsed=720ms rate=- action=accept
```

**冷却落盘 schema**

```json
{"schemaVersion":1,"savedAt":"2026-10-24 21:30:00",
 "groups":[{"group":"Repo","level":1,"failures":0,"cooldownUntil":"2026-10-24 21:35:00","reason":"timeout","rateKbps":0}]}
```

#### G2 `Core/VerifiedDownloader.cs`（新增，约 180 行）

```csharp
public sealed class VerificationResult
{
    public bool Ok { get; set; }
    public string PrimarySha256 { get; set; }
    public string SecondarySha256 { get; set; }
    public long Bytes { get; set; }
    public string Detail { get; set; }
}

public sealed class VerifiedDownloader
{
    public VerifiedDownloader(Logger logger);
    public async Task<byte[]> DownloadWithCrossCheckAsync(
        string primaryUrl, string secondaryUrl, int minBytes,
        CancellationToken cancellationToken, out VerificationResult verification);
}
```

**伪代码（判定表见 HLD §4.2 API-M2-08）**

```
bytes1 = await TryGet(primaryUrl)            // 失败返回 null
bytes2 = await TryGet(secondaryUrl)

if bytes1 != null && bytes1.Length < minBytes: bytes1 = null; note "primary-too-small"
if bytes2 != null && bytes2.Length < minBytes: bytes2 = null; note "secondary-too-small"

if bytes1 != null && bytes2 != null:
    h1 = Sha256Hex(bytes1); h2 = Sha256Hex(bytes2)
    if h1 == h2:  → Ok=true,  采用 bytes1,  Detail="双源一致"
    else:         → Ok=false, 抛异常,      Detail=string.Format("双源不一致 primary={0} secondary={1}", h1, h2)
elif bytes1 != null:
    → Ok=true（降级）, 采用 bytes1, Detail="次源不可用，降级为长度校验"
elif bytes2 != null:
    → Ok=true（换源）, 采用 bytes2, Detail="主源不可用，已用次源"
else:
    → Ok=false, 抛异常, Detail="双源均不可用"
```

**关键约束**：每个 `DownloadWithCrossCheckAsync` 调用产生 **恰好一条** `[NET] kind=verify` 行（含 `result=` 与两个 sha256 前 12 位）。

#### G3 `Core/Mirrors.cs`（改造，119 → 约 190 行）

**改造点**

| # | 位置 | 改造 |
|---|---|---|
| 1 | `:25-30` `RepoCandidates` | **改为官方优先**：`github.com` → `ghproxy.net` → `gh-proxy.com`（D-11；当前是镜像优先） |
| 2 | 新增 | `GroupsFor(EndpointGroup)` 返回该组的全部端点（含 `Level`） |
| 3 | `:32-55` `BuildEndpoints()` | 每个 `NetworkEndpoint` 补 `Level`；新增 `git-deps`、`chromium`、`installer` 三个组（现无 git-deps / chromium 端点） |
| 4 | `:57-76` `PickRepoMirror` | 保留（UI 展示用），但**业务决策改为走 planner**，不再直接用它选源 |
| 5 | `:102-117` `Pick` | 同上保留 |

**`GroupsFor` 的分组依据**：`NetworkEndpoint.Id` 前缀（`repo-` / `gitdeps-` / `electron-` / `builder-` / `npm-` / `pip-` / `chromium-` / `installer` / `release-api`）。**不改 `NetworkEndpoint` 的既有 Id 命名规则**（UI 表格按 Id 过滤，改了会断）。

**新增端点（每组每级一条）**

| 组 | Id | Level | Url |
|---|---|---|---|
| Repo | `repo-0` | 0 | `https://github.com/NousResearch/hermes-agent.git` |
| Repo | `repo-1` | 1 | `https://ghproxy.net/https://github.com/NousResearch/hermes-agent.git` |
| Repo | `repo-2` | 1 | `https://gh-proxy.com/https://github.com/NousResearch/hermes-agent.git` |
| GitDeps | `gitdeps-0` | 0 | `https://github.com/` |
| GitDeps | `gitdeps-1` | 1 | `https://ghproxy.net/https://github.com/` |
| Electron | `electron-0` | 0 | `https://github.com/electron/electron/releases` |
| Electron | `electron-1` | 1 | `https://npmmirror.com/mirrors/electron/` |
| Builder | `builder-0` | 0 | `https://github.com/electron-userland/electron-builder-binaries/releases` |
| Builder | `builder-1` | 1 | `https://npmmirror.com/mirrors/electron-builder-binaries/` |
| Npm | `npm-0` | 0 | `https://registry.npmjs.org/` |
| Npm | `npm-1` | 2 | `https://registry.npmmirror.com/` |
| Pip | `pip-0` | 0 | `https://pypi.org/simple/` |
| Pip | `pip-1` | 2 | `https://pypi.tuna.tsinghua.edu.cn/simple/` |
| Chromium | `chromium-0` | 0 | `https://cdn.playwright.dev/` |
| Chromium | `chromium-1` | 2 | `https://npmmirror.com/mirrors/chromium-browser-snapshots/` |
| Installer | `installer-0` | 0 | `https://hermes-agent.nousresearch.com/install.ps1` |
| ReleaseApi | `release-0` | 0 | `https://api.github.com/repos/NousResearch/hermes-agent/releases/latest` |

> **注意 `Level` 的跳号**：Npm / Pip / Chromium 只有 L0 与 L2（无 L1）。`Downgrade` 的「下一级」必须按**实际存在的级**取最小值，不能简单 `Level+1`。

#### G4 `Core/NetworkPreparer.cs`（改造，79 → 约 150 行）

**签名不变**（`BuildEnvironment()` 与构造 `NetworkPreparer(Settings, Logger)`），6 处既有调用点零改动。

**新增构造重载**（供 `InstallerEngine` 注入 planner）：

```csharp
public NetworkPreparer(Settings settings, Logger logger);                    // 保留（内部自建 planner）
public NetworkPreparer(Settings settings, Logger logger, NetworkPlanner planner);  // 新增
```

**改造点**

| # | 位置 | 改造 |
|---|---|---|
| 1 | `:27-31` 取值处 | 由「`_settings.*` 或默认值」改为「`planner 的组当前级` → 端点 URL；`_settings` 里的显式值优先（用户手填的镜像地址要生效）」 |
| 2 | `:33` `HERMES_REPO_URL` | 值改为按 `Repo` 组当前级取 |
| 3 | `:36` `ELECTRON_BUILDER_CACHE` | 保留 |
| 4 | 新增 | `UV_CACHE_DIR` = `<CacheDir>\uv` |
| 5 | 新增 | `npm_config_cache` + `NPM_CONFIG_CACHE` = `<CacheDir>\npm`（**两个大小写都要给**） |
| 6 | 新增 | 当 `GitDeps` 组级 ≥1 时注入 `GIT_CONFIG_COUNT=1` / `GIT_CONFIG_KEY_0=url.<mirror>.insteadOf` / `GIT_CONFIG_VALUE_0=https://github.com/`；级为 0 时**不注入** |
| 7 | `:48-52` 创建目录 | 追加创建 `uv` 与 `npm` 目录（同样 `try/catch` 不阻断） |
| 8 | `:54-62` 一次性日志 | 改为每次 `BuildEnvironment` 都记一行 `[NET] kind=env` 摘要（因为级会变，缓存日志会骗人） |

**优先级规则（冻结）**

```
取值的优先级：Settings 里显式非空 > planner 当前级对应端点 > Mirrors 内置默认
```

> 这条很关键：用户在 UI 里手填了 `cmbRepoMirror` 就必须覆盖自动选级，否则用户的选择被静默忽略。

#### G5 `Core/NetworkProbe.cs`（改造，183 → 约 250 行）

**新增方法**

```csharp
public async Task<long> MeasureThroughputAsync(NetworkEndpoint endpoint, int sampleBytes, CancellationToken cancellationToken);
public async Task<NetworkResult> ProbeSingleAsync(NetworkEndpoint endpoint, CancellationToken cancellationToken);  // 复用 ProbeOneAsync 的公开版
```

**`MeasureThroughputAsync` 伪代码**

```
if endpoint.Kind != Http: return -1          // git 端点不做速率测量
sampleBytes = sampleBytes > 0 ? sampleBytes : 262144
sw = Stopwatch.StartNew()
using cts = linked(cancellationToken); cts.CancelAfter(15s)
try:
    resp = await _client.GetAsync(endpoint.Url, ResponseHeadersRead, cts.Token)
    if !resp.IsSuccessStatusCode: return -1
    stream = await resp.Content.ReadAsStreamAsync()
    buffer = new byte[8192]; total = 0
    while total < sampleBytes:
        n = await stream.ReadAsync(buffer, 0, buffer.Length, cts.Token)
        if n <= 0: break
        total += n
    sw.Stop()
    if total <= 0: return -1
    return (long)(total / 1024.0 / Math.Max(0.001, sw.Elapsed.TotalSeconds))   // KB/s
catch: return -1
```

**注意**：`MeasureThroughputAsync` 只对 `Http` 端点有意义；git 端点的「速率」用探测耗时粗估（≤22s 视为可用）。这要与 `ShouldDowngrade` 的 `LastRateKbps` 语义对齐——**git 组传 -1 表示「不参与速率判定」**。

#### G6 `Core/InstallerEngine.cs`（改造）

**改造点（精确到既有位置，行号按 M1 后文件）**

| # | 位置 | 改造 |
|---|---|---|
| 1 | 构造函数 | 新增 `_planner = new NetworkPlanner(logger, _settings)`，并把它注入 `_network = new NetworkPreparer(_settings, logger, _planner)` |
| 2 | `RunUpgradeAsync` 中 update 调用处（M1 后约 `:361-373`） | 包进 `RunWithFailoverAsync` |
| 3 | `RunInstallAsync` 的 `DownloadInstallerScriptAsync`（`:182-205`） | 改用 `VerifiedDownloader.DownloadWithCrossCheckAsync(官方, GitHub raw, 10000, …)` |
| 4 | 桌面端重建处 | 同样包进 `RunWithFailoverAsync`（label=`desktop-build`） |
| 5 | 新增私有 `RunWithFailoverAsync` | 见 §1.2 时序 |
| 6 | 新增私有 `InferGroups(ProcessResult)` | 按 HLD §4.2 API-M2-10 的文本规则 |
| 7 | 新增私有 `AllGroups()` | 返回 `EndpointGroup` 全枚举 |

**不改**：`:287`/`:473` 的 `result.ExitCode != 0` 判据、`:314` 的阶段判据、M1 的指纹/快照/断言/三态全部保持。

#### G7 `Core/Settings.cs`（改造，89 → 约 95 行）

```csharp
[DataMember(Name = "preferDirectL0")] public bool PreferDirectL0 { get; set; }
[DataMember(Name = "enableL1")]      public bool EnableL1 { get; set; }

// 构造函数默认值：
PreferDirectL0 = true;    // D-11：默认 L0 优先
EnableL1 = true;          // D-12：L1 参与调度
```

**兼容性**：`DataContractJsonSerializer` 对缺失字段会用默认值（构造函数已赋值），**老配置文件不需要迁移**。`Settings.Load` 的静默 catch 保持不变。

#### G8 `MainForm.cs`（改造，**净增 ≤ 30 行**）

| 位置 | 改造 | 行数 |
|---|---|---|
| 网络面板或状态栏 | 显示各组当前级与最近一次 `[NET]` 摘要（一行文本，不加控件） | +12 |
| 设置区 | `preferDirectL0` / `enableL1` 两个勾选框（复用现有 `chkHostsFallback` 的布局模式） | +16 |
| `SetBusy` | 两个新勾选框加入启用/禁用列表 | +2 |

**不许**新增 `DataGridView` / `TabPage` / 自定义控件。

---

## 3. 数据设计

见 HLD §3 与 §2.2（`network-cooldown.json` schema）。补充：

| 文件 | 读取容错 | 写入 |
|---|---|---|
| `state\network-cooldown.json` | 不存在 / 解析失败 / `schemaVersion > 1` → 视为无记录 + Warn | 先写 `.tmp` 再替换（复用 `Settings.Save` 模式） |

---

## 4. 接口详细设计

见 HLD §4（已冻结）。补充两条实现约束：

1. **`BuildEnvironment()` 必须可重复调用且无副作用**（除 `CreateDirectory`）——`RunWithFailoverAsync` 每轮都会调它。
2. **`FormatNetLine` 的 `rate` 字段**：`-1` 输出 `rate=-`，`0` 输出 `rate=0KB/s`，正数输出 `rate=<n>KB/s`。**不得省略该字段**（PRD FR-4.3 要求「含原因/端点/耗时/速率」）。

---

## 5. 状态机 / 业务流程

### 5.1 组级状态转换

| 当前 | 事件 | 目标 | 条件 |
|---|---|---|---|
| L0 | `RecordFailure` | L0（失败计数+1） | 计数 < 2 |
| L0 | `RecordFailure` + `ShouldDowngrade` | L1 或 L2（按实际存在的下一级） | 计数 ≥ 2 或速率不达标 |
| L1 | `RecordFailure` ×2 | L2（若存在）或保持 L1（已最低） | — |
| 任意 | `Downgrade` | 下一级 + 冷却 300s | 有下一级 |
| 任意 | `Recover` | 上一级 | 冷却已过 且本次操作未回试过 |
| 任意 | `RecordSuccess` | 计数清零（级不变） | — |

### 5.2 `RunWithFailoverAsync` 的终止条件

| 条件 | 结果 |
|---|---|
| 某轮命令成功 | 返回该轮 `ProcessResult`（`ExitCode=0`） |
| 已达 `maxRounds=4` | 返回最后一轮的 `ProcessResult`（失败），由 M1 三态判 `Failed` |
| 所有组都已到最低级且仍失败 | **提前退出**（不再空转），返回最后结果 |
| 用户取消（`CancellationToken`） | 立即抛出 `OperationCanceledException`（M1 已有处理） |

---

## 6. 测试设计

### 6.1 无测试框架的降级方案

项目仍无测试框架（AGENTS.md §4）。全部用 **Windows PowerShell 5.1 + 反射探针**（M1 已验证该路径可行：`[Reflection.Assembly]::LoadFrom` + `[Type]::new()` 构造，**不用 `Add-Type -ReferencedAssemblies`**，它不吃 exe）。

### 6.2 各组件验证命令

| 组件 | 验证方式 | 预期 |
|---|---|---|
| G1 `NetworkPlanner` | 反射构造 planner；连续调 `RecordFailure(Repo,…)` 两次 → 检查 `CurrentLevel(Repo)` 与 `ShouldDowngrade`；再 `RecordSuccess` 检查计数清零 | L0→L1，`CooldownUntilUtc` 在未来 300s 内；`Snapshot()` 可见 |
| G1 冷却持久化 | `Downgrade(Repo)` → 检查 `state\network-cooldown.json` → 新建 planner 实例 → `LoadCooldowns()` → `CurrentLevel(Repo)` | 仍是 L1（说明冷却跨实例保留） |
| G1 分级跳号 | `CurrentLevel(Npm)=0` 时 `Downgrade(Npm)` | 直接到 **2**（不是 1，因为 Npm 无 L1） |
| G2 `VerifiedDownloader` | 三案：①官方 + raw（应一致）；②官方 + 一个错误 URL（次源不可用 → 降级）；③官方 + 另一个不同内容的 URL（应抛异常） | 分别得 `Ok=true/Detail=双源一致`、`Ok=true/Detail=次源不可用`、异常 + 两个 sha256 |
| G3 `Mirrors` | `GroupsFor(Repo)` / `GroupsFor(Npm)` / `BuildEndpoints()` 计数 | Repo 3 条（L0×1 + L1×2）、Npm 2 条（L0+L2）、`RepoCandidates[0]` 是 `github.com` |
| G4 `NetworkPreparer` | 构造 planner 并把 Npm 组降到 L2 → `BuildEnvironment()` → 检查 `npm_config_registry` | 值为 `https://registry.npmmirror.com/`；再检查 `UV_CACHE_DIR` / `npm_config_cache` / `NPM_CONFIG_CACHE` 三个键均存在 |
| G4 git 注入 | `GitDeps` 组 L1 时 `BuildEnvironment()` | 含 `GIT_CONFIG_COUNT=1` 与 `GIT_CONFIG_KEY_0=url.*.insteadOf`；L0 时**不含** `GIT_CONFIG_COUNT` |
| G5 速率 | `MeasureThroughputAsync(npmmirror 端点, 256KB)` | 返回 > 0 的 KB/s（本机实测 npmmirror 91ms 可达） |
| G6 切级编排 | 把 `PipIndex` 指向黑洞地址后跑一次操作；看日志 | 出现 `[NET] kind=downgrade group=Pip`，且该组 `CurrentLevel` 变化；重跑次数 ≤4 |
| G7 `Settings` | 老配置文件（无新字段）`Load()` | `PreferDirectL0=true`、`EnableL1=true`（默认值生效，不抛异常） |
| 全量回归 | `pwsh -File ./build/build.ps1` | **0 警告 0 错误**；产物 zip 体积记录 |
| M1 不回归 | 复跑 M1 的 5 个探针 | 全部 `PASSED` |

### 6.3 AC 覆盖对照

| AC | 状态 | 说明 |
|---|---|---|
| AC-06（同一 wheel 只下 1 次） | ⚠️ **部分** | `UV_CACHE_DIR` + npm cache 让 uv/npm 复用缓存；但「抓包证明只下 1 次」需真机长跑，本轮只验「缓存目录被使用」 |
| AC-07（git 依赖命中镜像且 commit 校验通过） | ✅ | `GIT_CONFIG_COUNT` 注入可测（`git ls-remote --get-url` 输出镜像地址） |
| AC-27（每次切级产生 1 条 `[NET]`） | ✅ | `FormatNetLine` 格式冻结 + 非空字段断言 |
| AC-28（L1 篡改产物 → sha256 失败 → 禁用该级） | ✅ | 双源不一致抛异常 + `Downgrade` |
| AC-30（L1 默认参与调度，记录来源域名与校验结果） | ✅ | `[NET] target=` 记录域名 |
| AC-34（首包 120s 无数据判不稳定、同级 2 次失败判不可用、300s 冷却不回试） | ⚠️ **部分** | 判定逻辑与冷却可测；「首包 120s 无数据」需要长挂测试，本轮用「黑洞地址 + 短超时」等价验证 |
| AC-35（L3 关闭、L1 探测后使用、UI 可见状态） | ⚠️ **部分** | L3 本轮不实现（D-08）；UI 展示各组级与开关可测 |

---

## 7. 配置项

| 配置 | 默认值 | 说明 |
|---|---|---|
| `preferDirectL0` | `true` | L0 官方直连优先；`false` 时直接走 L1 |
| `enableL1` | `true` | 是否允许 L1 加速域名参与调度 |
| 冷却时长 | 300 s（硬编码常量） | 不暴露给用户 |
| 速率阈值 | 100 KB/s（硬编码常量） | 只在预估传输 > 5 MB 时生效 |
| 双源校验样本上限 | 5 MB（硬编码常量） | 超过则不启用双源（R-M2-03） |

---

## 8. 任务卡切片建议

| 卡 | 阶段码 | FR | 文件白名单 | 依赖 | 预估 |
|---|---|---|---|---|---|
| `T-HU2-DEV-1` | DEV | FR-4.1 + FR-4.3 | `Core/NetworkPlanner.cs`（新）、`Core/Mirrors.cs`、`Core/Models.cs` | 无（首卡） | 4 h |
| `T-HU2-DEV-2` | DEV | FR-1.4 + FR-1.5 + FR-4.1（注入） | `Core/NetworkPreparer.cs`、`Core/Settings.cs` | DEV-1 | 3 h |
| `T-HU2-DEV-3` | DEV | FR-4.2（测量） | `Core/NetworkProbe.cs` | DEV-1 | 2.5 h |
| `T-HU2-DEV-4` | DEV | FR-4.4 | `Core/VerifiedDownloader.cs`（新） | DEV-1 | 2.5 h |
| `T-HU2-DEV-5` | DEV | FR-4.2（编排） | `Core/InstallerEngine.cs`、`MainForm.cs` | DEV-2/3/4 | 4 h |
| `T-HU2-QA-1` | QA | 全部 | 只读 | DEV-5 | 2 h |
| `T-HU2-CLS-1` | CLS | — | `doc/*`、`AGENTS.md` | QA | 1 h |

> **并行机会**：`T-HU2-DEV-3`（`NetworkProbe.cs`）与 `T-HU2-DEV-4`（新建 `VerifiedDownloader.cs`）文件不重叠，可同批并行。其余必须串行（DEV-5 同时依赖 2/3/4）。

---

## 9. 不改清单（红线，任务卡必须逐条抄入）

1. `Core/HostsFixer.cs` — 全文件零改动。
2. `Core/Redactor.cs` — 全文件零改动（`[NET]` 走 `Logger` 的脱敏）。
3. `Core/Logger.cs` — 零改动（`[NET]` 用现有 `LogLevel.Info`，不加新级别）。
4. `Core/EnvironmentProbe.cs` — 零改动（M1 已记录其进程重复缺陷，属 M3 范围）。
5. M1 的全部新增件 **零改动**：`RetryPolicy.cs` / `ProcessRunner.cs` / `ResultVerifier.cs` / `VersionFingerprint.cs` / `RunStateStore.cs` / `HomeRegistry.cs` / `UiTextBuilder.cs`（只允许**调用**）。
6. `Program.cs` — 零改动（M2 不涉及启动流程）。
7. `HermesPaths.HermesHome`（:8-21）— 一行不改；只允许在 `HermesPaths` 追加 `NetworkCooldownPath` 属性。
8. `InstallerEngine` 的 `:287`/`:473` 退出码判据与 `:314` 阶段判据 — 零改动。
9. `Settings` 旧 12 字段 — 零改动（只追加 2 个新字段）。
10. 语法红线：插值 / `?.` / `nameof(` 三项必须为 0。
11. 不引入 NuGet；不改 `.csproj`；不改 `build/build.ps1`。
12. **不写用户全局配置**：不得生成/修改 `.gitconfig`、`.npmrc`、`pip.ini`、`uv.toml`。
13. 不 `git commit` / `git push`（由 Hermes 验收后统一处理）。
14. `MainForm.cs` 净增 ≤ 30 行（1,345 → ≤1,375）。
