# DD：Hermes Agent Windows 安装/升级工具 —— M1 结论可信化与超时治理（详细设计）

| 字段 | 内容 |
|------|------|
| 文档版本 | v1.0 |
| 关联 HLD | `doc/HLD_Hermes_Agent_Windows_升级工具_v1.0.md`（v1.0）§2.4 组件 G1–G10 |
| 回溯链 | 本 DD ← HLD#2.4 G1–G10 ← PRD §4.1/§4.2 的 FR-1.1 / 1.2 / 1.3 / 1.6 / 1.7 / 2.5 / 2.8 |
| 设计负责人 | Hermes |
| 证据基线 | 2026-10-24 本机实测（`hermes --help`、`hermes gateway --help`、`hermes gateway status`、`pm/lock.json`、`tools/python-*/python.exe`、双 home 目录、`git rev-parse HEAD`） |

**版本历史**

| 版本 | 日期 | 修改人 | 修改说明 |
|------|------|--------|----------|
| v1.0 | 2026-10-24 | Hermes | 初稿：G1–G10 的代码级规格、伪代码、验收命令、不改清单、卡切片建议 |

---

## 0. 本机环境实测事实（2026-10-24，任务卡直接引用，勿重复探测）

| 项 | 实测值 |
|---|---|
| `HERMES_HOME` | `C:\Users\<USER>\AppData\Local\hermes` |
| CLI 入口 | `<HOME>\bin\hermes.cmd`（存在）、`<HOME>\bin\hermes.exe`（存在） |
| 安装目录 | `<HOME>\hermes-agent\`，`git rev-parse HEAD` = `c78ca093aa4b6759c055c3ed2bfbe7f741cf0292` |
| `pm/lock.json` | `<HOME>\hermes-agent\pm\lock.json`，**33,533 B**，结构 `{"schema":1,"packages":{"bws","chromium","cua-driver",…}}`，`sha256` 可算 |
| Python | `<HOME>\tools\python-3.14.7+…\python.exe`、`<HOME>\tools\python-3.14.9+…\python.exe`；两者 `import openai` 均成功（openai **2.54.0**） |
| 网关状态 | `hermes gateway status` → `✓ Scheduled Task registered: Hermes_Gateway` + `✓ Gateway process running (PID: 20860)` |
| 网关子命令 | `hermes gateway {run,ensure,start,stop,restart,status,install,uninstall,list,setup,migrate-legacy,migrate,enroll}` **全部存在** |
| 数据根 ①（选中） | `%LOCALAPPDATA%\hermes` — 有 `bin\`、有 `hermes-agent\` → **有 CLI 安装** |
| 数据根 ②（legacy） | `%USERPROFILE%\.hermes` — **无 `bin\`、无 `hermes-agent\`**，仅有 `config.yaml` / `auth.json` / `cache` 等 → **只有配置，无 CLI** |
| 构建基线 | `pwsh -File .\build\build.ps1` → 0 警告 0 错误，`dist/HermesInstaller/HermesInstaller.exe` 57 KB |

> 数据根 ② 的存在使 FR-1.6 可在**本机直接验收**（AC-08 无需第二台机器）。

---

## 1. 模块职责

### 1.1 输入 / 输出定义

| 方向 | 内容 | 类型/格式 | 来源/去向 | 格式约束 |
|---|---|---|---|---|
| 输入 | 当前 `Settings` | `Settings` 对象 | `Settings.Load()` | 本轮**不新增字段** |
| 输入 | 子进程输出流 | 行文本（UTF-8） | `ProcessRunner.Emit` 回调 | 每行带到达时间戳 |
| 输入 | `gateway status` 输出 | 两行纯文本 | `hermes gateway status` | 解析 `Gateway process running (PID: n)` |
| 输入 | `pm/lock.json` | 33 KB JSON | 文件系统 | 只算 sha256，不解析内容 |
| 输出 | `ProcessResult`（扩展） | 对象 | `InstallerEngine` | 新增 `TimedOut` / `TimeoutKind` / `ElapsedMs` / `LastOutputAgoMs` |
| 输出 | `VerificationReport` | 对象 + 日志行 | UI 结论页 / 日志 | 4 条 `AssertionItem` |
| 输出 | `OperationOutcome` | 枚举 | `OperationResult.Outcome` | `Success` / `Degraded` / `Failed` |
| 输出 | `state\*.json` | 3 份 JSON | `%LOCALAPPDATA%\HermesInstaller\state\` | 带 `SchemaVersion`，读写全 `try/catch` |

### 1.2 核心逻辑（升级主流程，含新增步骤）

```
RunUpgradeAsync(includeDesktop, progress, ct):
  ① startedAt = DateTime.Now                    [新增]
  ② runState.Begin("Upgrade")                    [新增]
  ③ snapshot = serviceGuard.CaptureAsync(ct)     [新增] ← 必须在停进程之前
  ④ fpBefore = fingerprint.CaptureAsync(ct)      [新增]
  ⑤ homes = HomeRegistry.Enumerate(); MatchProcesses(homes)   [新增]
     StopHermesProcessesAsync(homes, selectedOnly:true, ct)   [改造]
  ⑥ budget = RetryPolicy.For(OperationBudget)                [新增]
     result = RunPowerShellAsync("& hermes.cmd update --yes", …, budget)
     if result.TimedOut → 抛 InstallerException("升级中止：<超时类型>，已用 <ElapsedMs>ms，最后一次输出在 <LastOutputAgoMs>ms 前")
  ⑦ fpAfter = fingerprint.CaptureAsync(ct)       [新增]
     若 fpBefore 与 fpAfter 相等 → 记 INFO「本轮未产生版本变化」（不再触发第二条 update）
  ⑧ includeDesktop 且「产物 mtime > startedAt」为假 → 重建一次
  ⑨ restore = serviceGuard.RestoreAsync(snapshot, ct)          [新增] ← 先复原
  ⑩ report = verifier.VerifyAsync(includeDesktop, snapshot, restore, startedAt, ct)  [新增]
  ⑪ 结论 = report.Outcome：
        Success  → OperationResult.Ok(msg)
        Degraded → OperationResult.Degrade(msg)
        Failed   → OperationResult.Error(msg)
  ⑫ finally: runState.Clear()
```

`RunInstallAsync` 的 ⑨–⑪ 同样接入（PRD FR-1.1 明确「安装同样」有此缺陷，见 §2 末尾 A3 说明）；`RunUninstallAsync` **不接**（卸载后无产物、无服务，断言集不适用）。

---

## 2. 类 / 函数设计

### 2.1 类图

```
                    ┌──────────────┐
                    │ InstallerEngine │（改造 G8）
                    └───┬───┬───┬──┬┘
        ┌───────────────┘   │   │  └────────────────┐
        ▼                   ▼   ▼                   ▼
 ┌────────────┐  ┌──────────────┐ ┌──────────────┐ ┌──────────────┐
 │ProcessRunner│  │ResultVerifier│ │ServiceState  │ │Version       │
 │  (G7)      │  │   (G2)       │ │Guard   (G5)  │ │Fingerprint(G3)│
 └─────┬──────┘  └──────────────┘ └──────┬───────┘ └──────┬───────┘
       │                                  │                │
       ▼                                  ▼                ▼
 ┌────────────┐                  ┌──────────────┐  ┌──────────────┐
 │RetryPolicy │                  │HomeRegistry  │  │ProcessRunner │
 │  (G1)      │                  │   (G4)       │  └──────────────┘
 └────────────┘                  └──────┬───────┘
                                        ▼
                                 ┌────────────┐
                                 │HermesPaths │（改造 G9）
                                 └────────────┘

 ┌──────────────┐        ┌──────────────┐
 │RunStateStore │◄───────│Program/MainForm│（改造 G10）
 │   (G6)       │        └──────────────┘
 └──────────────┘
```

### 2.2 关键方法签名与伪代码

#### G1 `Core/RetryPolicy.cs`（新增，约 130 行）

```csharp
public enum OperationClass
{
    EndpointProbe = 0, FileDownload = 1, GitFetch = 2, GitClone = 3,
    NpmCi = 4, UvSync = 5, NodeGyp = 6, Generic = 7, OperationBudget = 8
}

public sealed class ProcessRunOptions
{
    public OperationClass Class { get; set; }
    public TimeSpan? NoProgressTimeout { get; set; }
    public TimeSpan? TotalTimeout { get; set; }
    public int PollIntervalMs { get; set; }
    public ProcessRunOptions() { PollIntervalMs = 500; Class = OperationClass.Generic; }
}

public static class RetryPolicy
{
    public static ProcessRunOptions For(OperationClass cls);
    public static int MaxAttempts(OperationClass cls);
    public static TimeSpan BackoffFor(OperationClass cls, int attemptIndex);  // 0-based
    public static TimeSpan BudgetFor(bool includeDesktop);                     // 1500s / 2400s
    public static bool ShouldRetry(OperationClass cls, string lastError);      // 编译类永不重试
}
```

**参数表（冻结，源自 PRD FR-2.8）**

| OperationClass | NoProgressTimeout | TotalTimeout | MaxAttempts | Backoff 序列 |
|---|---|---|---|---|
| `EndpointProbe` | 无 | 5 s | 2 | 1 s, 2 s |
| `FileDownload` | 30 s | 90 s | 3 | 1 s, 2 s, 4 s |
| `GitFetch` | 60 s | 180 s | 2 | 2 s, 5 s |
| `GitClone` | 60 s | 600 s | 2 | 2 s, 5 s |
| `NpmCi` | 120 s | 900 s | 1 | 无 |
| `UvSync` | 120 s | 900 s | 2 | 5 s, 15 s |
| `NodeGyp` | 180 s | 600 s | **0** | 无 |
| `Generic` | 无 | 1800 s | 0 | 无 |
| `OperationBudget` | **180 s** | CLI 1500 s / 桌面 2400 s | 0 | 无 |

`ShouldRetry` 规则：`NodeGyp` / `OperationBudget` / `Generic` 恒 `false`；其余按 `MaxAttempts` 与错误文本判定（含 `error C`、`MSB`、`node-gyp` 关键字即视为编译类 → `false`）。

> `OperationBudget` 的 `NoProgressTimeout=180s` 是本轮的**关键落地参数**：它把 PRD 里「uv 1800s 空转」压到「180s 无输出即杀」，AC-04 的「中止」部分由此满足。

#### G2 `Core/ResultVerifier.cs`（新增，约 210 行）

```csharp
public enum AssertionStatus { Pass, Warn, Fail, Skipped }

public sealed class AssertionItem
{
    public string Name { get; set; }
    public string Expected { get; set; }
    public string Actual { get; set; }
    public AssertionStatus Status { get; set; }
    public string Detail { get; set; }
}

public sealed class VerificationReport
{
    private readonly List<AssertionItem> _items = new List<AssertionItem>();
    public List<AssertionItem> Items { get { return _items; } }
    public OperationOutcome Outcome { get; internal set; }
    public string Summary { get; internal set; }
    public void Add(AssertionItem item) { _items.Add(item); Recompute(); }
    private void Recompute();
}

public sealed class ResultVerifier
{
    public ResultVerifier(Logger logger, InstallerEngine engine);
    public async Task<VerificationReport> VerifyAsync(
        bool includeDesktop, ServiceSnapshot baseline, ServiceRestoreResult restore,
        DateTime operationStartedAt, CancellationToken ct);

    // 四项断言私有实现
    private async Task<AssertionItem> AssertCliVersionAsync(CancellationToken ct);
    private async Task<AssertionItem> AssertDependenciesAsync(bool preCheckOk, CancellationToken ct);
    private Task<AssertionItem> AssertDesktopArtifactAsync(DateTime startedAt);
    private AssertionItem AssertServiceState(ServiceSnapshot baseline, ServiceRestoreResult restore);

    // 本轮唯一允许的路径推导（显式例外，见 §2.3 例外条款）
    private string FindToolsPython();   // 扫 <HOME>\tools\python-*\python.exe，取版本号最高
}
```

**`Recompute()` 判定（冻结）**

```
if any Status == Fail      → Outcome = Failed,   Summary = "失败：<失败项名用「、」连接>"
else if any Status == Warn → Outcome = Degraded, Summary = "降级完成：<警告项名>"
else if any Status == Skipped → Outcome = Degraded, Summary = "降级完成：<n> 项无法断言（<项名>）"
else                       → Outcome = Success,  Summary = "升级完成，4 项自检全部通过"
```

**四项断言规格（冻结）**

| # | 名称 | 实现 | Pass 条件 | Fail 条件 | Skipped 条件 |
|---|---|---|---|---|---|
| A1 | CLI 版本 | `await _engine.GetVersionAsync(ct)` | 非空且匹配 `^v?\d+\.\d+`（用 `Regex.IsMatch`，`System.Text.RegularExpressions` 属 BCL） | 空 / 不匹配 / 异常 | 不适用（必须有） |
| A2 | 关键依赖 | ①`FindToolsPython()` 得 `py`；②操作前 `py -c "import openai"` 的结果由调用方传入 `preCheckOk`；③`preCheckOk==false` → Skipped；④否则操作后再跑一次，比对 | 退出码 0 | 操作前 0、操作后非 0 | **操作前就非 0** |
| A3 | 桌面端产物 | `HermesPaths.DesktopBuilt && File.GetLastWriteTime(HermesPaths.DesktopExe) > startedAt` | 两者皆真 | 任一为假（含「有产物但 mtime 早于本次操作」） | `includeDesktop == false` |
| A4 | 服务状态 | 由 `restore` 与 `baseline` 合成 | `baseline.HadGateway == false` → Pass（无需恢复）；或 `restore.Restored == true` | `baseline.HadGateway == true` 且 `restore.Restored == false` | 不适用 |

> **A3 是对 PRD 锚点 `InstallerEngine.cs:312` 的直接修复**：原判据「`DesktopBuilt` 为真即认为已随更新重建」无法区分旧产物，改为 mtime 比较。操作开始时间由 `RunUpgradeAsync` 的 ① 传入。

#### G3 `Core/VersionFingerprint.cs`（新增，约 150 行）

```csharp
[DataContract]
public sealed class FingerprintRecord
{
    [DataMember(Name="schemaVersion")] public int SchemaVersion { get; set; }
    [DataMember(Name="gitHead")]       public string GitHead { get; set; }
    [DataMember(Name="lockSha256")]    public string LockSha256 { get; set; }
    [DataMember(Name="cliVersion")]    public string CliVersion { get; set; }
    [DataMember(Name="capturedAt")]    public string CapturedAt { get; set; }
}

public sealed class VersionFingerprint
{
    public VersionFingerprint(Logger logger, ProcessRunner runner, InstallerEngine engine);
    public Task<FingerprintRecord> CaptureAsync(CancellationToken ct);   // 任一项取不到则该字段为 null，整体不为 null
    public static bool IsSame(FingerprintRecord a, FingerprintRecord b); // 三者非空且全等
    public FingerprintRecord LoadLast();                                 // 读 state\fingerprint.json
    public void SaveLast(FingerprintRecord record);                      // 先写 .tmp 再替换
}
```

**`CaptureAsync` 规格**

| 字段 | 取值方式 | 失败处理 |
|---|---|---|
| `GitHead` | `& git -C "<InstallDir>" rev-parse HEAD`，超时 15 s（`OperationClass.Generic` 的短化实例） | 退出码非 0 → `null` + 记 Warn |
| `LockSha256` | 读 `<InstallDir>\pm\lock.json` 全字节 → `SHA256.Create().ComputeHash` → 小写 hex | 文件不存在 → `null` + 记 Warn |
| `CliVersion` | `await engine.GetVersionAsync(ct)` | 返回 null → `null` |
| `CapturedAt` | `DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")` | — |

**`IsSame` 语义（关键，含对 PRD 的口径修正）**

本 DD 明确：`IsSame` **只用于「同一次操作内的防重复」**，不用于「跨会话判断是否已是最新」。

依据：PRD FR-1.2 原文「版本指纹一致则跳过源码更新与依赖安装」，若用于跨会话，会导致「远端已有新版本时用户重跑也永远不升级」——这是设计缺陷。PRD 要解决的真实问题是 E3/E15 记录的「**同一次桌面端升级把 `hermes update` 跑了两遍**」，属单次操作内的重复。跨会话的「已是最新」由官方 `hermes update --yes` 自身输出 `Already up to date!` 保证（该命令本就幂等）。

**因此本轮实现**：`RunUpgradeAsync` 只在同一次调用内比较 `fpBefore` / `fpAfter`，若相等则记 INFO「本轮未产生版本变化」，并**不再触发第二遍 update 或 desktop 重建**（`InstallerEngine.cs:295-308` 的二次构建条件同时收紧为「产物 mtime 未更新」）。

#### G4 `Core/HomeRegistry.cs`（新增，约 170 行）

```csharp
public sealed class ProcessRef
{
    public string ProcessName { get; set; }
    public int Pid { get; set; }
    public string ExecutablePath { get; set; }
}

public sealed class HermesHomeInfo
{
    public string Path { get; set; }
    public string Kind { get; set; }          // "HERMES_HOME" | "LOCALAPPDATA" | "USERPROFILE"
    public bool Exists { get; set; }
    public bool HasCliInstall { get; set; }   // bin\hermes.cmd 或 hermes-agent\.git 存在
    public bool IsSelected { get; set; }      // == HermesPaths.HermesHome
    public string CliVersion { get; set; }    // 仅 IsSelected 时探测，其余为 null（省时间）
    public List<ProcessRef> Processes { get; } = new List<ProcessRef>();
}

public static class HomeRegistry
{
    public static List<HermesHomeInfo> Enumerate();     // 顺序固定 + 去重（OrdinalIgnoreCase, TrimEnd('\\')）
    public static void MatchProcesses(List<HermesHomeInfo> homes);  // 进程 → 根 归属
    public static string Describe(List<HermesHomeInfo> homes);      // 一行中文摘要，给 UI/日志
}
```

**`Enumerate` 伪代码**

```
candidates = []
if HERMES_HOME 非空      → add(Kind="HERMES_HOME",    path=env)
add(Kind="LOCALAPPDATA", path=Combine(LocalAppData,"hermes"))
add(Kind="USERPROFILE",  path=Combine(UserProfile,".hermes"))

去重：按 TrimEnd('\\') 的 OrdinalIgnoreCase 比较，**保留先出现者的 Kind**
逐个：Exists = Directory.Exists(path)
      HasCliInstall = File.Exists(Combine(path,"bin","hermes.cmd")) || Directory.Exists(Combine(path,"hermes-agent",".git"))
      IsSelected = 与 HermesPaths.HermesHome 等值
每个根独立 try/catch，异常只记 Warn 并继续（R-06）
```

**`MatchProcesses` 伪代码**

```
names = { "Hermes","hermes","hermes-gateway","python","pythonw" }   // 与现有 StopHermesProcessesAsync 一致
foreach name: foreach proc in GetProcessesByName(name):
    try { exe = proc.MainModule.FileName } catch { exe = null }
    if exe == null → 归入「未知归属」桶（不入任何根），不 kill
    对每个 home：if exe.StartsWith(home.Path+"\\", OrdinalIgnoreCase) → home.Processes.Add(ref); break
```

> **必须保留现有 `StopHermesProcessesAsync`（:408-459）的正确部分**：跳过 self（:425-428）、`process.Kill()` 失败回落 `KillTree`（:436-440）、杀完 `Task.Delay(1500)`（:453）。改造只把「单一 `home` 前缀」换成「传入的根集合」。

#### G5 `Core/ServiceStateGuard.cs`（新增，约 190 行）

```csharp
public sealed class ServiceEntry { public string HomePath; public string ProcessName; public int Pid; public string ExecutablePath; }
public sealed class ServiceSnapshot
{
    public DateTime CapturedAt;
    public List<ServiceEntry> Entries = new List<ServiceEntry>();
    public bool HadGateway;      // gateway status 解析出 PID
    public int GatewayPid;
    public bool HadDesktop;      // 桌面端进程存在
    public string StatusRaw;     // gateway status 原文（进日志，便于排障）
}
public sealed class ServiceRestoreResult
{
    public bool Restored;
    public string Detail;
    public int PidBefore;
    public int PidAfter;
}
public sealed class ServiceStateGuard
{
    public ServiceStateGuard(Logger logger, ProcessRunner runner);
    public async Task<ServiceSnapshot> CaptureAsync(CancellationToken ct);
    public async Task<ServiceRestoreResult> RestoreAsync(ServiceSnapshot baseline, CancellationToken ct);
    private async Task<int> QueryGatewayPidAsync(out string raw, CancellationToken ct);
}
```

**`QueryGatewayPidAsync` 规格**

- 执行 `& "<HermesCmd>" gateway status`（`OperationClass.EndpointProbe`，5 s 总超时，不重试）。
- 返回 `stdout` 原文（**必须整段写日志**，R-03 要求）。
- 解析规则：逐行找同时含 `Gateway process running` 与 `PID:` 的行，取 `PID:` 后的连续数字。解析失败 → 返回 0。
- 若 `stdout` 含 `Gateway process not running` 或 PID=0 → 视为未运行。

**`RestoreAsync` 伪代码**

```
if (!baseline.HadGateway) → return { Restored=true, Detail="升级前网关未运行，无需恢复" }

pid = QueryGatewayPidAsync()                       // 先看是不是还在跑
if (pid > 0) → return { Restored=true, PidAfter=pid, Detail="网关仍运行（PID <pid>）" }

// 第一步：ensure（不装服务）
run("& hermes.cmd gateway ensure", EndpointProbe 变体：总超时 60s)
pid = QueryGatewayPidAsync()
if (pid > 0) → return { Restored=true, PidAfter=pid, Detail="gateway ensure 已恢复（PID <pid>）" }

// 第二步：start（启动已注册的计划任务）
run("& hermes.cmd gateway start", 同上)
pid = QueryGatewayPidAsync()
if (pid > 0) → return { Restored=true, PidAfter=pid, Detail="gateway start 已恢复（PID <pid>）" }

return { Restored=false, Detail="网关未能恢复；最后一次 status 原文：" + raw }   // 结论降级
```

**双起防护（R-10）**：`RestoreAsync` 只在「当前 PID == 0」时才尝试启动，绝不会在网关已在跑的情况下再 `ensure`；两次 `status` 的 PID 都进日志，便于事后核对是否双起。

#### G6 `Core/RunStateStore.cs`（新增，约 120 行）

```csharp
[DataContract]
public sealed class RunStateRecord
{
    [DataMember(Name="schemaVersion")] public int SchemaVersion;
    [DataMember(Name="pid")]           public int Pid;
    [DataMember(Name="operation")]     public string Operation;
    [DataMember(Name="stageName")]     public string StageName;
    [DataMember(Name="startedAt")]     public string StartedAt;
    [DataMember(Name="heartbeatAt")]   public string HeartbeatAt;
}
public sealed class RunStateStore
{
    public void Begin(string operation);
    public void UpdateStage(string stageName);
    public void Heartbeat();
    public RunStateRecord ReadOther();                                  // 读不到返回 null
    public bool TryReadStale(TimeSpan maxAge, out RunStateRecord rec);   // heartbeat 超龄 → false
    public void Clear();
}
```

路径固定 `%LOCALAPPDATA%\HermesInstaller\state\run.json`；写盘沿用「先 `.tmp` 再替换」；所有异常 `catch` 后记 Warn 不抛。

#### G7 `Core/ProcessRunner.cs`（改造，249 → 约 330 行）

**新增重载（旧签名一行不改）**

```csharp
public Task<ProcessResult> RunProcessAsync(
    string fileName, string arguments, string workingDirectory,
    IDictionary<string, string> environment, Action<string, LogLevel> onLine,
    CancellationToken cancellationToken, string cleanupFile, ProcessRunOptions options)

public Task<ProcessResult> RunPowerShellAsync(
    string invocation, string workingDirectory, IDictionary<string, string> environment,
    Action<string, LogLevel> onLine, CancellationToken cancellationToken, ProcessRunOptions options)
```

旧重载内部转调新重载，并**记一条 Warn**：

```csharp
_logger.Warn("[TIMEOUT] 调用未指定超时策略，按 Generic(1800s) 处理: " + fileName);
```

> 这条 Warn 是 P3「不静默」的落地：任何忘记传策略的调用点都会在日志里暴露。注意 `ProcessRunner` 目前**不持有 Logger**（构造函数无参）——本轮允许给它加一个可选 Logger 字段，构造重载 `ProcessRunner(Logger logger)`，默认构造 `ProcessRunner()` 保持可用（内部 `_logger = null`，则跳过该 Warn）。

**核心循环（替换现有 :170-174 的 `Task.Run(() => process.WaitForExit())`）**

```
long lastOutputStamp = Stopwatch.GetTimestamp();   // 在 outHandler/errHandler 里更新
var sw = Stopwatch.StartNew();
bool timedOut = false; string timeoutKind = null;
int poll = (options != null && options.PollIntervalMs > 0) ? options.PollIntervalMs : 500;

await Task.Run(() =>
{
    while (true)
    {
        if (process.WaitForExit(poll)) break;

        if (options == null) continue;

        if (options.TotalTimeout.HasValue && sw.Elapsed >= options.TotalTimeout.Value)
        { timedOut = true; timeoutKind = "total"; KillTree(process); break; }

        if (options.NoProgressTimeout.HasValue)
        {
            long idleMs = (long)((Stopwatch.GetTimestamp() - Volatile.Read(ref lastOutputStamp))
                                 * 1000.0 / Stopwatch.Frequency);
            if (idleMs >= (long)options.NoProgressTimeout.Value.TotalMilliseconds)
            { timedOut = true; timeoutKind = "no-progress"; KillTree(process); break; }
        }
    }
    // R-05：超时被杀后必须再调无参 WaitForExit 以清空异步输出缓冲
    try { process.WaitForExit(); } catch { }
}).ConfigureAwait(false);
```

**`ProcessResult` 扩展**

```csharp
public bool TimedOut { get; set; }
public string TimeoutKind { get; set; }    // "no-progress" | "total" | null
public long ElapsedMs { get; set; }
public long LastOutputAgoMs { get; set; }
```

**超时后的日志格式（冻结）**

```
[TIMEOUT] kind=no-progress limit=180000ms elapsed=182431ms lastOutputAgo=181002ms op=OperationBudget target=hermes.cmd action=kill-tree
```

**`Emit` 改造（:195-202）**：加 `Interlocked.Exchange(ref lastOutputStamp, Stopwatch.GetTimestamp())`；签名不变。

#### G8 `Core/InstallerEngine.cs`（改造，580 → 约 700 行）

新增字段与构造注入：

```csharp
private readonly ServiceStateGuard _serviceGuard;
private readonly VersionFingerprint _fingerprint;
private readonly ResultVerifier _verifier;
private readonly RunStateStore _runState;
// 构造函数内初始化（不改变现有构造签名 InstallerEngine(Settings, Logger)）
```

改造点（**精确到既有行号**）：

| # | 位置 | 改造 | 是否改动既有逻辑 |
|---|---|---|---|
| 1 | `RunUpgradeAsync` :262 之后 | 插入 startedAt / runState.Begin / CaptureAsync / fpBefore / HomeRegistry 枚举 | 纯新增 |
| 2 | `:269` | `StopHermesProcessesAsync(ct)` → 改为 `StopHermesProcessesAsync(homes, true, ct)` 重载，旧签名保留转调 | 保留旧签名 |
| 3 | `:276-281` | `RunPowerShellAsync(...)` 增加 `RetryPolicy.For(OperationClass.OperationBudget)` 参数 | 只加参数 |
| 4 | `:287-291` | 超时场景追加专门分支：`if (result.TimedOut)` 抛带 `TimeoutKind` 的 `InstallerException` | 新增分支，原退出码分支保留 |
| 5 | `:295` | 二次构建条件收紧为「`!DesktopBuilt` **或** 产物 mtime ≤ startedAt」 | **改判据**（FR-1.1 A3 / FR-1.2） |
| 6 | `:305-308` | 重建用的子进程也加 `OperationBudget`（CLI 侧用 NodeGyp 变体 600s） | 只加参数 |
| 7 | `:316-318` | 原来的无条件 `Success` + `Ok` → 改为 ⑨⑩⑪ 三步（复原 → 断言 → 按 Outcome 出结论） | **删除既有 3 行**（唯一允许删除的既有行） |
| 8 | `RunInstallAsync` :85-88 | 同上，插入 ⑨⑩⑪（`includeDesktop` 透传给 A3） | **删除既有 2 行** |
| 9 | `:408-459` `StopHermesProcessesAsync` | 加重载 `(List<HermesHomeInfo> homes, bool selectedOnly, CancellationToken)`；旧实现改为「以单根构造列表 → 转调新重载」 | 旧行为等价 |
| 10 | `finally` 块 :330-333 / :100-103 | 追加 `_runState.Clear()` | 纯新增 |

#### G9 `Core/HermesPaths.cs`（改造，90 → 约 110 行）

只新增：

```csharp
public static string StateDir        { get { return Path.Combine(AppDataDir, "state"); } }
public static string FingerprintPath { get { return Path.Combine(StateDir, "fingerprint.json"); } }
public static string RunStatePath    { get { return Path.Combine(StateDir, "run.json"); } }
public static string ServiceSnapshotPath { get { return Path.Combine(StateDir, "service-snapshot.json"); } }
```

`EnsureAppData()`（:83-88）追加 `Directory.CreateDirectory(StateDir);`。

**`HermesHome` 属性（:8-21）一行不改**——它仍返回「当前生效根」，语义不变；多根能力放在 `HomeRegistry`，避免改动 6 个文件依赖的既有属性。

#### G10 `MainForm.cs` / `Program.cs` / 新增 `Core/UiTextBuilder.cs`

**`Core/UiTextBuilder.cs`（新增，约 90 行，纯字符串，无 UI 依赖）**

```csharp
public static class UiTextBuilder
{
    public static List<string> VerificationLines(VerificationReport report);  // 4 行，含 [通过]/[警告]/[失败]/[跳过]
    public static string OutcomeTitle(OperationOutcome outcome);             // "升级完成" / "降级完成" / "升级失败"
    public static string HomesSummary(List<HermesHomeInfo> homes);           // "根1（选中，有 CLI v0.22.5）｜根2（无 CLI）"
    public static List<string> HomesDetail(List<HermesHomeInfo> homes);      // 每根一行：路径 / CLI / 进程
    public static string OtherInstanceText(RunStateRecord rec);              // "另一实例正在执行：<op>／阶段 <stage>／已进行 <m> 分"
}
```

**`MainForm.cs` 改动（行数预算：净增 ≤ 45 行，只允许新增方法 + 局部替换）**

| 位置 | 改动 | 行数 |
|---|---|---|
| `:897-910` 结果展示 | 把 `result.Success ? Information : Warning` 换成 `switch (result.Outcome)` 三态；`Degraded` 用 `MessageBoxIcon.Warning` + 橙色日志行 | 净 +6 |
| 新增 `AppendVerification(...)` | 调 `UiTextBuilder.VerificationLines` 逐行 `AppendLog` | +8 |
| `RunOperationAsync` 开头 | 调 `UiTextBuilder.HomesSummary` 写日志（多根展示，不新增控件） | +4 |
| 新增 `ShowHomesToLog()` | 多根明细进日志区 | +10 |
| `UpdateSummary()` :755-759 | `lblSubtitle` 追加根数量（如「数据根 2 个」） | 净 +2 |
| `SetBusy` 等 | 不动 | 0 |

**`Program.cs` 改动（净增 ≤ 12 行）**

`:42-47` 的 `if (!createdNew)` 分支改为：

```csharp
if (!createdNew)
{
    string extra = string.Empty;
    try
    {
        var store = new Core.RunStateStore();
        Core.RunStateRecord rec;
        if (store.TryReadStale(TimeSpan.FromMinutes(30), out rec))
        {
            extra = "\r\n\r\n" + Core.UiTextBuilder.OtherInstanceText(rec);
        }
    }
    catch { }
    MessageBox.Show("Hermes 安装助手已经在运行。" + extra, "Hermes 安装助手",
        MessageBoxButtons.OK, MessageBoxIcon.Information);
    return;
}
```

### 2.3 错误处理策略

| 异常类别 | 处理 | 结论影响 |
|---|---|---|
| `OperationCanceledException` | 沿用现有：返回 `Error("操作已取消。")` | `Failed`（用户主动取消仍如实报未完成） |
| 超时（`TimedOut=true`） | 抛 `InstallerException`，消息含 `TimeoutKind` + 耗时 + 最后输出时间 | `Failed` |
| 断言未过 | 不抛异常，收集进 `VerificationReport` | 按 `Recompute()` 定级 |
| 服务复原失败 | 不抛异常，`RestoreResult.Restored=false` | A4 Fail → `Degraded`（若其他项全过） |
| `state\*.json` 读写失败 | `catch` + Warn，功能降级为「无记录」（指纹判等失效 → 照常执行 update） | 不影响主流程 |
| `HomeRegistry` 单根异常 | `catch` + Warn，跳过该根 | 不影响主流程 |
| `gateway status` 解析失败 | 保留原文进日志，PID=0 | A4 按未恢复处理 |

**「不静默」检查清单（验收时逐条 grep）**：`catch { }` 空捕获只允许出现在 `Settings.Load/Save`、`ProcessRunner.TryDelete`、`Emit`（既有代码）以及本轮新增的 `RunStateStore`/`HomeRegistry` 的**状态文件/枚举**路径中，且每个此类块内必须有 `_logger.Warn(...)`（除既有代码）。

---

## 3. 数据设计

### 3.1 `state\fingerprint.json`

```json
{"schemaVersion":1,"gitHead":"c78ca093aa4b6759c055c3ed2bfbe7f741cf0292",
 "lockSha256":"40b06778980ff8c50dcd97c0d9b39ba3…","cliVersion":"v0.22.5+456",
 "capturedAt":"2026-10-24 16:45:00"}
```

### 3.2 `state\run.json`

```json
{"schemaVersion":1,"pid":18472,"operation":"DesktopUpgrade",
 "stageName":"deps-node","startedAt":"2026-10-24 16:45:00",
 "heartbeatAt":"2026-10-24 16:52:30"}
```

### 3.3 `state\service-snapshot.json`

```json
{"schemaVersion":1,"capturedAt":"2026-10-24 16:45:00","hadGateway":true,"gatewayPid":20860,
 "hadDesktop":false,"statusRaw":"✓ Scheduled Task registered: Hermes_Gateway\n✓ Gateway process running (PID: 20860)",
 "entries":[{"homePath":"C:\\Users\\<USER>\\AppData\\Local\\hermes","processName":"hermes-gateway","pid":20860,"executablePath":"…"}]}
```

### 3.4 迁移与兼容

- 三份文件均带 `schemaVersion: 1`。
- 读取时若 `schemaVersion > 1` → **忽略该文件并按无记录处理**（不报错、不迁移）。
- 任何字段缺失 → 视为 null / 默认值，功能降级不崩溃。
- 无需回填、无需迁移脚本。

---

## 4. 接口详细设计

见 HLD §4（接口已在 S3 冻结）。本 DD 补充两条实现约束：

1. **`ProcessRunner` 旧重载的行为必须与原实现完全一致**（含 `cleanupFile` 语义、`ExitCode = cancelled ? -2 : exitCode`）。验收方式：旧调用点不改，编译零错误，真机跑一次「新装」路径，行为与改造前一致。
2. **`OperationResult.Success` 必须始终等于 `(Outcome == Success)`**。`Degrade()` 置 `Success=false`——UI 的既有 `!result.Success` 分支（`MainForm.cs:906` 的 `LooksLikeNetworkFailure` 判定）因此对 `Degraded` 也会触发，这是**期望行为**（降级后可能需要网络兜底）。

---

## 5. 状态机 / 业务流程

### 5.1 升级流程状态转换（M1 范围，非完整 FR-2.3 状态机）

| 当前状态 | 事件 | 目标状态 | 条件 |
|---|---|---|---|
| Idle | 用户点「升级」 | Precheck | 单实例锁获取成功 |
| Precheck | 快照+指纹+多根完成 | Stopping | — |
| Stopping | 进程已停（或无进程） | Updating | — |
| Updating | `hermes update` 退出码 0 | Building（仅桌面端） / Verifying | — |
| Updating | 超时（`TimedOut`） | Failed | 记 `[TIMEOUT]` |
| Updating | 退出码非 0 | Failed | 记退出码 + stderr 尾部 |
| Building | 产物 mtime > startedAt | Restoring | — |
| Building | 重建失败 | Failed | — |
| Restoring | 网关状态复原成功 / 无需复原 | Verifying | — |
| Restoring | 复原失败 | Verifying | A4 将判 Fail |
| Verifying | 4 项全 Pass | Success | — |
| Verifying | 有 Warn / Skipped 无 Fail | Degraded | — |
| Verifying | 有 Fail | Failed | — |

### 5.2 异常分支终态

| 异常 | 终态 | 用户看到 |
|---|---|---|
| 无进展超时（180 s） | `Failed` | 「升级中止：180s 无输出（已运行 3m2s，最后一次输出在 3m1s 前）」+ 建议查看日志 |
| 总预算超时（CLI 25 min） | `Failed` | 「升级中止：超出 CLI 时间预算 25 分钟」 |
| 用户取消 | `Failed` | 「操作已取消。」 |
| 断言 A3 失败（产物 mtime 未更新） | `Degraded` | 「降级完成：桌面端产物未在本次更新中重建」 |
| 网关未能恢复 | `Degraded` | 「降级完成：网关未能恢复运行」+ status 原文 |

---

## 6. 测试设计

### 6.1 单元测试

**本项目无测试框架**（AGENTS.md §4），按制度层 §二降级为「可复现的验证命令」。每个组件给一条可离线执行的验证路径。

### 6.2 各组件验证命令（验收时逐条复跑）

| 组件 | 验证命令 | 预期 |
|---|---|---|
| 编译 | `pwsh -File .\build\build.ps1` | 0 警告 0 错误，产出 57→约 65 KB 的 exe + zip |
| G1 参数表 | 反射或临时 `--selftest` 段打印 9 类参数 | 与 §2.2 表逐格一致 |
| G7 无进展超时 | 在 `%TEMP%` 造一个只 sleep 不输出的 `cmd /c ping -n 400 127.0.0.1`，以 `NoProgressTimeout=5s` 调 `RunProcessAsync` | ≤7 s 内返回，`TimedOut=true`、`TimeoutKind="no-progress"`、进程树已消失（`tasklist` 查不到 pid） |
| G7 总超时 | 同上场景，`TotalTimeout=3s`、`NoProgressTimeout=null` | ≤5 s 内返回，`TimeoutKind="total"` |
| G7 向后兼容 | 不改任何旧调用点，跑一次「环境检测」 | 探针全部正常返回（旧重载未被破坏） |
| G2 A1 | 正常升级后看结论 | `[通过] CLI 版本` |
| G2 A3 | 手动把 `Hermes.exe` 的 mtime 改成 2020 年，再跑一次升级 | A3 判 Fail → 结论 `Degraded`/`Failed`（**可离线构造，无需真等**） |
| G3 指纹 | 连跑两次「检查」类操作，看 `state\fingerprint.json` | 文件生成，三次字段非空，sha256 长度 64 |
| G4 多根 | `--selftest` 或 UI 日志区 | 列出 2 个根：`%LOCALAPPDATA%\hermes`（选中，有 CLI）/ `%USERPROFILE%\.hermes`（无 CLI） |
| G5 复原 | 手动 `hermes gateway stop`，再跑一次「升级」（或注入一个只做 Capture+Restore 的自检段） | 升级后 `hermes gateway status` 重新出现 `Gateway process running` |
| G5 不双起 | 网关在跑时跑升级 | 前后 PID 一致或仅一次变化，日志中不出现两次启动 |
| G6 锁 | 同时启动两个 exe | 第二个弹窗含「另一实例正在执行：…／阶段 …」 |
| G8 结论 | 断网后跑升级 | 结论为 `Failed`/`Degraded`，**不得**出现「升级完成」 |

### 6.3 AC 覆盖对照（M1 七条 FR）

| AC | 覆盖状态 | 验证方式（本轮） |
|---|---|---|
| AC-01 | ✅ 全 | G8 结论：断网跑，结论必须非 Success |
| AC-02 | ✅ 全 | 结论页展示 4 项 + 期望值（截图 + 日志） |
| AC-03 | ⚠️ **部分**（见 §7 偏差 D-1） | 指纹判等只在单次操作内生效；「已是最新重跑 ≤3 min」由官方 update 自身幂等保证，本工具不额外拦截 |
| AC-04 | ⚠️ **部分** | 「900 s 内中止」→ 本轮为 **180 s 无进展即中止**（更严）；「切级」→ M2 |
| AC-05 | ✅ 全 | `ShouldRetry(NodeGyp)=false`，日志中同类编译不出现第二遍 |
| AC-08 | ✅ 全 | 本机双根实测（`%LOCALAPPDATA%\hermes` + `%USERPROFILE%\.hermes`） |
| AC-09 | ✅ 全 | `gateway stop` 后升级，结束必须恢复；否则结论 `Degraded` |
| AC-21 | ✅ 全 | 连续启动 3 次，1 个执行 2 个提示 |
| AC-24 | ⚠️ **部分** | 9 类参数已实现并被 `RetryPolicy` 查询；但「逐项注入超时偏差 ≤10%」需要 9 类各自的真实子进程，本轮只对 `OperationBudget`/`NoProgress`/`Total` 三类做注入实测 |
| NFR-01 | ⚠️ 部分 | 预算机制到位；真实耗时 ≤25/40 min 需真机长跑，不由本轮代码保证 |
| NFR-04 | ✅ 全 | 重复执行只有锁与日志变化，不重装依赖（指纹 + 官方幂等） |
| NFR-08 | ✅ 全 | `[TIMEOUT]` / `[NET]`（M2）结构化行，本轮只落 `[TIMEOUT]` |
| NFR-09 | ✅ 全 | `MainForm.cs` 净增 ≤45 行（验收用 `wc -l` 卡） |
| NFR-11 | ✅ 全 | 验收 grep：插值 `$"` = 0、`?.` = 0、`nameof(` = 0 |

---

## 7. 配置项

**本轮不新增任何 `Settings` 字段**（避免配置文件 schema 变动与 `Settings.Load` 静默失败风险）。

| 配置 | 默认值 | 说明 | 环境差异 |
|---|---|---|---|
| 既有 12 个字段 | 见 `Settings.cs` | 不变 | — |
| 超时参数 | 硬编码在 `RetryPolicy` | 按 PRD FR-2.8 冻结 | 各机一致 |
| `state\` 路径 | `%LOCALAPPDATA%\HermesInstaller\state\` | 不可配置 | 各机不同（动态推导） |

### 7.1 与 PRD 的偏差（必须让评审人知悉）

| 编号 | PRD 原文 | 本 DD 实现 | 理由 |
|---|---|---|---|
| **D-1** | FR-1.2：「版本指纹一致则跳过源码更新与依赖安装，只做自检」 | 指纹判等**只在同一次操作内**用于防重复；跨会话不据此跳过 update | 若跨会话跳过，远端有新版本时用户重跑也永不升级（缺陷）。PRD 要解决的是 E3/E15 的「单次操作内跑两遍」 |
| **D-2** | FR-1.3：「编译类失败不重试，网络类失败按 FR-4 换源」 | 本轮只实现「不重试」；「换源」属 M2 | 范围锁定 M1（PRD §9.1） |
| **D-3** | FR-1.7：只写「尝试恢复」 | 冻结为 `gateway ensure` → `gateway start` → 失败降级，且**禁止**用桌面端 exe 冒充 | 2026-10-24 实测发现 `gateway ensure` 才是「不装服务拉起运行时」的正确命令 |
| **D-4** | FR-1.1 A2：「关键依赖可导入」 | 具体化为 `import openai`（操作前后对比），操作前不可导入则 Skip | 实测本机 openai 2.54.0 可导入；`pm/lock.json` 是 PM 组件清单，不是 Python 包清单 |
| **D-5** | NFR-09：「MainForm 禁止继续膨胀」 | 允许净增 ≤45 行，且逻辑全部外移 `Core/UiTextBuilder.cs` | 三元组结论与多根展示必须碰 UI；45 行是达成 FR-1.1/1.6 的最小代价 |

---

## 8. 任务卡切片建议（S4 输入）

按文件冲突与依赖排序，**全部串行**（除卡1 外每卡都写 `InstallerEngine.cs`）：

| 卡 | 阶段码 | FR | 文件白名单 | 依赖 | 预估 |
|---|---|---|---|---|---|
| `T-HU-DEV-1` | DEV | FR-1.3 + FR-2.8 | `Core/ProcessRunner.cs`、`Core/RetryPolicy.cs`（新） | 无（首卡） | 3–4 h |
| `T-HU-DEV-2` | DEV | FR-1.1 | `Core/ResultVerifier.cs`（新）、`Core/UiTextBuilder.cs`（新）、`Core/Models.cs`、`Core/InstallerEngine.cs` | 卡1 | 4 h |
| `T-HU-DEV-3` | DEV | FR-1.7 | `Core/ServiceStateGuard.cs`（新）、`Core/HermesPaths.cs`（仅加字段）、`Core/InstallerEngine.cs` | 卡2 | 3–4 h |
| `T-HU-DEV-4` | DEV | FR-1.2 + FR-2.5 | `Core/VersionFingerprint.cs`（新）、`Core/RunStateStore.cs`（新）、`Core/InstallerEngine.cs`、`Program.cs`、`MainForm.cs` | 卡3 | 4 h |
| `T-HU-DEV-5` | DEV | FR-1.6 | `Core/HomeRegistry.cs`（新）、`Core/InstallerEngine.cs`（StopHermesProcessesAsync 重载）、`MainForm.cs` | 卡4 | 3 h |
| `T-HU-QA-1` | QA | 全部 | 无（只读验证） | 卡5 | 2 h |
| `T-HU-CLS-1` | CLS | — | `doc/*`、`AGENTS.md` 数字回填 | 卡QA | 1 h |

> 卡1 与卡2 的分界刻意放在 `Models.cs`：卡1 完全不碰 `Models.cs`，因此卡1 可在卡2 设计复核前先跑起来。

---

## 9. 不改清单（红线，任务卡必须逐条抄入）

1. `Core/HostsFixer.cs` — **全文件零改动**（M1 不涉及 hosts）。
2. `Core/Mirrors.cs`、`Core/NetworkPreparer.cs`、`Core/NetworkProbe.cs` — 全文件零改动（M2 范围）。
3. `Core/Redactor.cs` — 全文件零改动（附录 D 新规则属 M3）。
4. `Core/Settings.cs` — 全文件零改动（不新增字段）。
5. `Core/EnvironmentProbe.cs` — 除必要时**只读调用** `FindHermesProcesses` 外零改动。
6. `Core/Logger.cs` — 零改动（`Logger.Write` 影响 19 个符号，动它成本最高）。
7. `HermesPaths.HermesHome` 属性（:8-21）— 一行不改，语义必须仍是「当前生效根」。
8. `ProcessRunner.RunPowerShellAsync` / `RunProcessAsync` 的**旧签名** — 保留且行为等价。
9. `InstallerEngine` 的私有 helper（`ExtractJsonLine` / `ExtractLastJsonLine` / `GetString` / `GetBool` / `Truncate` / `Report` / `AppendHermesLogs` / `StageResult`）— 零改动。
10. `RunUninstallAsync` — 不接入断言（卸载后无产物/无服务，断言集不适用）。
11. 语法红线：插值字符串 0 处、`?.` 0 处、`nameof(` 0 处；不用 record / switch 表达式 / `is not`。
12. 不引入任何 NuGet 包；不修改 `.csproj`（SDK 风格，新 `.cs` 自动参与编译）。
13. 不修改 `build/build.ps1`。
14. 不 `git commit` / `git push`（由 Hermes 在验收后统一处理）。
