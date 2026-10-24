# HLD：Hermes Agent Windows 安装/升级工具 —— M2 分级网络源与完整性校验（概要设计）

| 字段 | 内容 |
|------|------|
| 文档版本 | v1.0 |
| 关联 PRD | `doc/PRD_Hermes_Agent_Windows_升级工具_v1.6.md`（v1.6，M2 评审中） |
| 设计负责人 | Hermes |
| 评审状态 | 待评审（芦哥） |
| 本轮范围 | 6 条 FR：FR-1.4 / FR-1.5 / FR-4.1 / FR-4.2 / FR-4.3 / FR-4.4 |
| 前置 | M1 已交付并上线（`origin/main` HEAD `fee5f8e`） |
| 涉及代码 | 21 个 `.cs` / 6,550 行（M1 后），其中 M2 新增 2 个、改造 6 个 |

**版本历史**

| 版本 | 日期 | 修改人 | 修改说明 |
|---|---|---|---|
| v1.0 | 2026-10-24 | Hermes | 初稿：per-group 分级模型、命令级切级状态机、双源交叉校验、6 条 FR 的组件划分与接口冻结 |

---

## 1. 设计概述

### 1.1 设计目标

| PRD 需求 | 本轮要达成的结果 |
|---|---|
| FR-1.4 | uv / npm 有持久缓存目录；下载后按可用基准校验，通过即复用到后续重试 |
| FR-1.5 | `git+https://github.com/...` 依赖在子进程环境中被 `insteadOf` 重写到镜像，**不改用户 `.gitconfig`** |
| FR-4.1 | 每个受管对象都有 L0→L1→L2 的有序候选序列（L3 不做，D-08） |
| FR-4.2 | 命令失败/超时后能判定「该降哪一级」并重跑；含配额、冷却、回试 |
| FR-4.3 | 每次降级/切换产出一条可机读的 `[NET]` 行 |
| FR-4.4 | 官方脚本走双源交叉校验；校验失败即判该源不可用并降级 |

### 1.2 设计约束

| # | 约束 | 来源 | 实际影响 |
|---|---|---|---|
| C1 | net48 + 单文件 + **不引 NuGet** | 项目红线 | 无 Polly / 无 Newtonsoft；速率测量用 `HttpClient` + `Stopwatch` |
| C2 | C# 5/6/7 风格（禁插值 / `?.` / `nameof`） | AGENTS.md §7.1 | 全部字符串用 `string.Format` 与拼接 |
| C3 | `MainForm.cs` 继续受行数预算约束（现 1,345） | NFR-09 | M2 的 UI 增量 ≤ 30 行，逻辑全部进 Core |
| C4 | **不许在 `hermes update` 子进程内部换源** | PRD §9.6 B-1（实测） | 切级 = 命令级重跑，不是包级切换 |
| C5 | **`install.ps1` 无官方 checksum** | PRD §9.6 B-2（实测） | 只能双源交叉，不得拿 ETag 当基准 |
| C6 | **`pm/lock.json` 的 sha256 由 PM 自校** | PRD §9.6 B-3（实测） | 本工具只读报告，不重复校验 |
| C7 | 所有日志走 `Logger.Write` | AGENTS.md §7.4 | `[NET]` 用 `LogLevel.Info` + 前缀，不加新级别 |
| C8 | 不改用户全局 git 配置 | FR-1.5 语义 | 只用 `GIT_CONFIG_COUNT` 环境变量注入（B-4 实测生效） |

### 1.3 设计原则

| # | 原则 | 落实 |
|---|---|---|
| P1 | **分级按「对象组」而非全局** | 见 §2.1，这是本设计最核心的一条 |
| P2 | **零副作用优先** | 镜像注入全部走子进程环境变量 / `GIT_CONFIG_*`，不写文件、不改用户配置 |
| P3 | **降级必须可解释** | 每次切换产出 `[NET]` 行，含 `reason` / `op` / `target` / `elapsed` / `rate` / `action` |
| P4 | **校验失败即禁用该源** | 不重试同一源、不"凑合用"，记录期望值与实际值 |
| P5 | **不破坏 M1 成果** | M1 的 `RetryPolicy` / `ProcessRunner` 超时重载 / `ResultVerifier` 三态 / `MainForm` 行数预算全部沿用 |

### 1.4 复用调研结论（制度层 §7.4）

| 待实现能力 | ①仓库内已有 | ②已有依赖 | ③成熟库 | ④自研 | 结论 |
|---|---|---|---|---|---|
| 超时载体 | ✅ M1 `RetryPolicy` + `ProcessRunner` 超时重载 | — | Polly（违反 C1） | — | **直接复用 M1** |
| 并发探测 | ✅ `NetworkProbe.RunAsync`（`SemaphoreSlim(4)`） | `HttpClient` | — | — | **扩展现有** |
| 端点候选表 | ✅ `Mirrors.BuildEndpoints()` + `Pick*` | — | — | — | **改造为分级模型** |
| 环境注入 | ✅ `NetworkPreparer.BuildEnvironment()` | — | — | — | **改造为按级注入** |
| JSON 存取 | ✅ `Settings` / `RunStateStore` 的 DataContract 模式 | — | — | — | **复用同一模式**（冷却状态落盘） |
| 哈希 | ✅ `VersionFingerprint` 已用 `SHA256.Create` | — | — | — | **复用同一用法** |
| 速率测量 | ❌ 无（`ProbeOneAsync` 只读 header） | `HttpClient` `ResponseHeadersRead` | — | 自研读前 N KB 计时 | **自研（约 40 行）** |
| 结论三态 | ✅ M1 `ResultVerifier` | — | — | — | **直接复用** |

---

## 2. 系统架构

### 2.1 架构选型及理由

**选型：per-group（对象组维度）的分级状态机 + 命令级切级编排，全部在 `Core/`，不引入新进程。**

#### 为什么「级」不能是全局唯一的一个

PRD FR-4.1 的分级表本身就说明了这一点——L0/L1/L2 覆盖的**对象不同**：

| 对象组 | L0（官方直连） | L1（加速域名） | L2（公共镜像） |
|---|---|---|---|
| `Repo`（git 主仓） | `github.com` | `ghproxy.net` / `gh-proxy.com` | 不适用 |
| `GitDeps`（git 依赖） | `github.com` | `ghproxy.net` | 不适用 |
| `Electron` | `github.com` releases | `npmmirror.com/mirrors/electron/` | 不适用 |
| `Builder` | `github.com` releases | `npmmirror.com/mirrors/electron-builder-binaries/` | 不适用 |
| `Npm` | `registry.npmjs.org` | — | `registry.npmmirror.com` |
| `Pip` | `pypi.org` | — | `pypi.tuna.tsinghua.edu.cn` |
| `Installer`（install.ps1） | `hermes-agent.nousresearch.com` | — | — |
| `ReleaseApi` | `api.github.com` | — | — |

**若用全局单一"当前级"，会出现「npm 已降到 L2 却把 git 也拖到 L2（而 git 没有 L2）」的荒谬状态**。因此状态机的键是 `EndpointGroup`，每个组独立维护自己的级游标与失败计数。

#### 备选方案与放弃理由

| 备选 | 描述 | 放弃理由 |
|---|---|---|
| A. 全局单一级游标 | 整个工具处于 L0/L1/L2 之一 | 与 FR-4.1 的对象差异矛盾（git 无 L2、npm 无 L1）；会导致降级过度或无处可降 |
| B. 每个 URL 独立游标 | 每个端点各自记失败 | 粒度太细：`repo-0/1/2` 本是一组候选，独立游标会让「镜像 1 失败」与「镜像 2 失败」互不关联，无法表达「这一组整体不可用」 |
| **C. per-group 游标（选定）** | 8 个对象组，每组一个级游标 + 失败计数 + 冷却时间 | 与 PRD 分级表结构一致；粒度足够表达「这家镜像不行」又不至于碎片化 |

### 2.2 系统上下文（M2 视角）

```
┌─────────────┐  点击升级
│   芦哥      │───────────┐
└─────────────┘           ▼
                ┌──────────────────────────────┐
                │  InstallerEngine（切级编排）  │
                │   ①测当前级 → ②跑命令        │
                │   ③失败 → ④降级 → ⑤重跑      │
                └───┬──────────┬───────────┬───┘
                    │          │           │
                    ▼          ▼           ▼
        ┌───────────────┐ ┌─────────┐ ┌──────────────┐
        │NetworkPlanner │ │Network  │ │Verified      │
        │ per-group 游标 │ │Preparer │ │Downloader    │
        │ 冷却/配额/[NET]│ │按级注入 │ │双源交叉校验  │
        └───────┬───────┘ └────┬────┘ └──────┬───────┘
                │              │             │
                ▼              ▼             ▼
        ┌──────────────────────────────────────────┐
        │ 子进程（hermes.cmd / install.ps1）        │
        │ 拿到 env：镜像地址 + GIT_CONFIG_* + 缓存  │
        └──────────────────────────────────────────┘
```

### 2.3 关键组件说明

| # | 组件 | 类型 | 职责 | 承接 |
|---|---|---|---|---|
| **G1** | `NetworkPlanner` | **新增** | per-group 级游标、失败计数、冷却时间、降级/回试判定、`[NET]` 日志生成、状态落盘 | FR-4.1 / 4.2 / 4.3 |
| **G2** | `VerifiedDownloader` | **新增** | 双源交叉校验下载（两份比 sha256）、长度校验、失败抛 `VerificationException` | FR-4.4 |
| **G3** | `Mirrors` | 改造 | 端点按「组 + 级」组织；`RepoCandidates[0]` 改为官方（D-11）；新增 `GroupsFor(group)` 查询 | FR-4.1 |
| **G4** | `NetworkPreparer` | 改造 | **按 group 的当前级**生成环境块；新增 `UV_CACHE_DIR`、`npm_config_cache`、`GIT_CONFIG_COUNT` insteadOf | FR-1.4 / 1.5 |
| **G5** | `NetworkProbe` | 改造 | 新增单端点探测、速率测量（读前 256 KB 计时）、按组探测 | FR-4.2 |
| **G6** | `InstallerEngine` | 改造 | 切级重试编排（命令级）、错误文本 → 组的推断、失败归因 | FR-4.2 |
| **G7** | `Settings` | 改造 | `+preferDirectL0`（默认 true）、`+enableL1`（默认 true） | FR-4.1 / 4.7 |
| **G8** | `MainForm` | 改造（薄） | 显示各组当前级与最近探测结果；**净增 ≤30 行** | FR-4.1 |

---

## 3. 数据架构

### 3.1 分级状态（进程内 + 可选落盘）

```
NetworkPlanner（进程内单例，InstallerEngine 持有）
  ├─ Dictionary<EndpointGroup, GroupState>
  │     GroupState { int Level; int ConsecutiveFailures; DateTime CooldownUntilUtc; DateTime LastAttemptUtc; string LastReason; }
  └─ Snapshot() → 供 UI 展示与日志
```

**落盘**：`%LOCALAPPDATA%\HermesInstaller\state\network-cooldown.json`

```json
{"schemaVersion":1,"savedAt":"2026-10-24 21:30:00",
 "groups":[{"group":"Repo","level":1,"failures":2,"cooldownUntil":"2026-10-24 21:35:00","reason":"timeout"}]}
```

**为什么落盘**：PRD FR-4.2 要求「降级后 300s 内不回试上级」。若只在进程内记冷却，用户关掉工具再打开就会立刻又去打已确认失败的 L0，等于没有冷却。

**并发**：与 `RunStateStore` 同策略——读失败一律 `catch` + Warn 视为无记录；写走「先 `.tmp` 再替换」。多实例场景下以「最后写入者胜」，接受（冷却信息非关键数据）。

### 3.2 缓存目录（FR-1.4）

| 缓存 | 环境变量 | 路径 | 现状 |
|---|---|---|---|
| Electron builder | `ELECTRON_BUILDER_CACHE` | `<CacheDir>\electron-builder` | ✅ 已有（`NetworkPreparer.cs:36`） |
| **uv** | `UV_CACHE_DIR` | `<CacheDir>\uv` | ❌ **新增** |
| **npm** | `npm_config_cache` + `NPM_CONFIG_CACHE` | `<CacheDir>\npm` | ❌ **新增**（两个大小写都要给，同 M1 的 npm registry 做法） |

三个目录在 `BuildEnvironment()` 内 `Directory.CreateDirectory`，失败不阻断（与现有 `:48-52` 同策略）。

### 3.3 git 依赖重写（FR-1.5）

用 `GIT_CONFIG_COUNT` 注入，**不写 `.gitconfig`**（B-4 实测生效）：

```
GIT_CONFIG_COUNT=1
GIT_CONFIG_KEY_0=url.<mirrorBase>.insteadOf
GIT_CONFIG_VALUE_0=https://github.com/
```

当 `GitDeps` 组当前级为 L1 时 `mirrorBase = https://ghproxy.net/https://github.com/`，为 L0 时**不注入**（`GIT_CONFIG_COUNT` 不设或设 0），让 git 直连。

---

## 4. 接口设计（S3 准出前冻结）

### 4.1 接口清单

| 编号 | 接口 | 所属 | 对应 |
|---|---|---|---|
| API-M2-01 | `enum EndpointGroup`（**9 个成员**：Installer / ReleaseApi / Repo / GitDeps / Electron / Builder / Npm / Pip / Chromium） | G1 | FR-4.1 |
| API-M2-02 | `NetworkEndpoint.Level { get; set; }`（int，0/1/2） | G3 | FR-4.1 |
| API-M2-03 | `NetworkPlanner.CurrentLevel(group)` / `CandidatesFor(group)` | G1 | FR-4.1 / 4.2 |
| API-M2-04 | `NetworkPlanner.RecordFailure(group, reason, rateKbps)` / `RecordSuccess(group)` | G1 | FR-4.2 |
| API-M2-05 | `NetworkPlanner.ShouldDowngrade(group)` / `CanRecover(group)` / `Downgrade(group, reason)` | G1 | FR-4.2 |
| API-M2-06 | `NetworkPlanner.Snapshot()` / `FormatNetLine(...)` | G1 | FR-4.3 |
| API-M2-07 | `NetworkPreparer.BuildEnvironment()`（**签名不变**，内部按组取级） | G4 | FR-1.4 / 1.5 / 4.1 |
| API-M2-08 | `VerifiedDownloader.DownloadWithCrossCheckAsync(primary, secondary, minBytes, ct)` | G2 | FR-4.4 |
| API-M2-09 | `NetworkProbe.MeasureThroughputAsync(endpoint, sampleBytes, ct)` | G5 | FR-4.2 |
| API-M2-10 | `InstallerEngine` 的切级编排（私有方法 `RunWithFailoverAsync`） | G6 | FR-4.2 |

### 4.2 关键接口定义（冻结）

#### API-M2-01 / 02：对象组与端点级别

```csharp
public enum EndpointGroup
{
    Installer = 0,   // install.ps1（无镜像，仅官方域名）
    ReleaseApi = 1,  // api.github.com 查版本
    Repo = 2,        // git 主仓
    GitDeps = 3,     // git+https 依赖
    Electron = 4,
    Builder = 5,
    Npm = 6,
    Pip = 7,
    Chromium = 8
}

// Models.cs 的 NetworkEndpoint 增加一个字段（其余六字段不变）
public int Level { get; set; }   // 0=L0 官方直连 / 1=L1 加速 / 2=L2 公共镜像
```

> `Level` 只新增字段并给默认值 `0`，构造重载保持向后兼容（旧 6 参构造内部 `Level = 0`）。

#### API-M2-03 ~ 06：分级状态机

```csharp
public sealed class GroupState
{
    public EndpointGroup Group;
    public int Level;                    // 当前级
    public int ConsecutiveFailures;      // 当前级连续失败次数
    public DateTime CooldownUntilUtc;    // 冷却结束时间（UTC）
    public string LastReason;
    public long LastRateKbps;
}

public sealed class NetworkPlanner
{
    public NetworkPlanner(Logger logger, Settings settings, bool includeDesktop);

    public int CurrentLevel(EndpointGroup group);
    public List<NetworkEndpoint> CandidatesFor(EndpointGroup group);   // 该组从当前级起的候选，按级升序

    public void RecordSuccess(EndpointGroup group);
    public void RecordFailure(EndpointGroup group, string reason, long rateKbps);

    public bool ShouldDowngrade(EndpointGroup group);   // 连续失败≥2 或速率不达标 且 下一级存在
    public bool CanRecover(EndpointGroup group);        // 冷却已过 且 上级存在
    public void Downgrade(EndpointGroup group, string reason);
    public void Recover(EndpointGroup group);

    public List<GroupState> Snapshot();
    public string FormatNetLine(string kind, EndpointGroup group, string reason,
                                string op, string target, int attempt,
                                long elapsedMs, long rateKbps, string action);

    public void LoadCooldowns();    // 启动时读 state\network-cooldown.json
    public void SaveCooldowns();    // 每次 Downgrade/Recover 后写
}
```

**判定阈值（冻结，源自 PRD FR-4.2）**

| 判定 | 条件 |
|---|---|
| 本级失败 | 同一 group 同一 level **连续 2 次**失败（探测超时 / 连接重置 / 命令失败归因到该组） |
| 本级不稳定 | 测量速率 **< 100 KB/s** 且本次操作传输量预计 > 5 MB；或同级 3 次尝试中 2 次超阈值 |
| 单次配额 | 同一 group 同一 level 在一次操作内最多 **3 次**尝试；换源后每组 **1 次** |
| 冷却 | 降级后 **300 秒**内不 `Recover` 回上级 |
| 回试 | 冷却结束 **且** 上级探测恢复，本次操作内允许回上级 **1 次** |

#### API-M2-07：按组分级的环境注入（签名不变）

```csharp
// 签名保持不变（6 处既有调用点零改动）
public Dictionary<string, string> BuildEnvironment();
```

内部改为：

```
repoBase    = planner 的 Repo 组当前级 → 对应 URL（L0=github.com/…，L1=ghproxy.net/…）
gitDepsBase = planner 的 GitDeps 组当前级 → L1 时注入 GIT_CONFIG_COUNT/_KEY_0/_VALUE_0
npmIndex    = planner 的 Npm 组当前级 → L0=registry.npmjs.org，L2=registry.npmmirror.com
pipIndex    = planner 的 Pip 组当前级 → L0=pypi.org/simple，L2=tuna
electron    = planner 的 Electron 组当前级
builder     = planner 的 Builder 组当前级

新增注入：
  UV_CACHE_DIR                    = <CacheDir>\uv
  npm_config_cache                = <CacheDir>\npm
  NPM_CONFIG_CACHE                = <CacheDir>\npm

保留原有：
  HERMES_REPO_URL / ELECTRON_MIRROR / ELECTRON_BUILDER_BINARIES_MIRROR /
  ELECTRON_BUILDER_CACHE / npm_config_registry / NPM_CONFIG_REGISTRY /
  PIP_INDEX_URL / PIP_TRUSTED_HOST / UV_DEFAULT_INDEX / UV_INDEX_URL /
  GIT_TERMINAL_PROMPT / GCM_INTERACTIVE / PYTHONUTF8 / PYTHONIOENCODING
```

> **`Settings` 为 null 时的行为**：`NetworkPreparer` 现有构造是 `NetworkPreparer(Settings, Logger)`。M2 新增 `NetworkPlanner` 依赖，但**不改变该构造函数签名**——内部按需创建 planner（`InstallerEngine` 会把同一实例注入，避免状态分散）。

#### API-M2-08：双源交叉校验下载

```csharp
public sealed class VerificationResult
{
    public bool Ok;
    public string PrimarySha256;
    public string SecondarySha256;
    public long Bytes;
    public string Detail;          // "双源一致（sha256=…）" / "双源不一致" / "次源不可用，降级为长度校验"
}

public sealed class VerifiedDownloader
{
    public VerifiedDownloader(Logger logger);
    public async Task<byte[]> DownloadWithCrossCheckAsync(
        string primaryUrl, string secondaryUrl, int minBytes, CancellationToken ct, out VerificationResult verification);
}
```

**判定规则（冻结）**

| 场景 | 结果 | 动作 |
|---|---|---|
| 两源都成功且 sha256 相同 | `Ok=true` | 用该字节；记一条 `[NET] kind=verify result=dual-match` |
| 两源都成功但 sha256 不同 | `Ok=false` | 抛 `InstallerException`，**不采用任何一份**；记录两个 sha256 |
| 主源成功、次源失败 | `Ok=true`（降级） | 用主源字节；记 Warn `[NET] kind=verify result=len-only reason=secondary-unavailable` |
| 主源失败、次源成功 | `Ok=true`（换源） | 用次源字节；记 `[NET] kind=verify result=secondary-fallback` |
| 两源都失败 | `Ok=false` | 抛 `InstallerException` |
| 任一源字节数 < `minBytes` | 该源判失败 | 按上面的组合判定 |

#### API-M2-09：速率测量

```csharp
public async Task<long> MeasureThroughputAsync(NetworkEndpoint endpoint, int sampleBytes, CancellationToken ct);
// 返回 KB/s；失败返回 -1
```

实现要点：`HttpCompletionOption.ResponseHeadersRead` 打开流后读最多 `sampleBytes`（默认 262144）字节，用 `Stopwatch` 计时；**读不满就超时则按已读量折算**（避免把慢速源误判为完全不可用）。

#### API-M2-10：命令级切级编排（`InstallerEngine` 私有）

```
RunWithFailoverAsync(Func<Dictionary<string,string>, Task<ProcessResult>> action, string operationLabel, ct):
  for round in 1..maxRounds:                    // maxRounds = 4（3 次尝试 + 1 次回试）
      env = preparer.BuildEnvironment()          // 按各组当前级生成
      result = await action(env)
      if result.TimedOut || result.ExitCode != 0:
          affected = InferGroups(result)         // 错误文本 → 受影响的组集合
          if affected is empty → affected = AllGroups()      // 推断不出就整体降级
          foreach g in affected:
              planner.RecordFailure(g, reason, rate)
              if planner.ShouldDowngrade(g):
                  planner.Downgrade(g, reason)
                  logger.Info(planner.FormatNetLine("downgrade", g, reason, operationLabel, …, "fallback"))
      else:
          foreach g in AllGroups(): planner.RecordSuccess(g)   // 整体成功即全组复位
          return result
  return lastResult                              // 全部失败 → 交给 M1 的三态结论判 Failed
```

**`InferGroups` 的推断规则（冻结）**

| 错误文本关键片段 | 归因组 |
|---|---|
| `npm`、`registry`、`ETARGET`、`E404` | `Npm` |
| `pip`、`pypi`、`tuna`、`Could not find a version` | `Pip` |
| `git`、`github.com:443`、`Could not resolve host` | `Repo` + `GitDeps` |
| `electron`、`builder`、`node-gyp`、`headers` | `Electron` + `Builder` |
| 其它 / 无法识别 | 全部组（保守：整体降一级） |

---

## 5. 技术选型

| 领域 | 选型 | 理由 | 备选 |
|---|---|---|---|
| 分级状态 | 进程内对象 + JSON 落盘 | 冷却需跨进程保留（300s） | 纯内存（跨会话失效）、注册表（不可移植） |
| 环境注入 | 子进程 `ProcessStartInfo.EnvironmentVariables` | M1 已验证（13 个变量）；零副作用 | 写 `.npmrc`（污染用户目录） |
| git 重写 | `GIT_CONFIG_COUNT` 环境变量 | B-4 实测生效；不改 `.gitconfig` | `git config --global`（污染）、`-c url.x.insteadOf`（每条命令都要拼） |
| 速率测量 | `HttpClient` + `ResponseHeadersRead` + 读 256 KB | 无新依赖；样本足够判 100 KB/s 阈值 | `WebClient`（已过时） |
| 哈希 | `SHA256.Create`（复用 `VersionFingerprint` 用法） | BCL | — |
| `[NET]` 日志 | `Logger.Write(LogLevel.Info, "[NET] …")` | 不加新级别（C7）；可 grep | 新增 `LogLevel.Net`（波及 `Logger` 19 个受影响符号） |

---

## 6. 部署架构

无变化（仍是单 exe）。新增运行期数据：

```
%LOCALAPPDATA%\HermesInstaller\
  ├─ state\            （M1 已建）
  │    ├─ fingerprint.json
  │    ├─ run.json
  │    └─ network-cooldown.json     ← M2 新增
  └─ cache\            （M1 已建）
       ├─ install.ps1
       ├─ electron-builder\          （已有）
       ├─ uv\                        ← M2 新增
       └─ npm\                       ← M2 新增
```

---

## 7. 安全设计

| 项 | 设计 |
|---|---|
| 校验失败处理 | **不采用**任何一份（双源不一致时），直接判该级不可用并降级——防投毒（PRD D-01 要求） |
| 来源域名记录 | 每次使用 L1/L2 都记 `[NET] target=<域名>`，便于事后追溯是哪个镜像 |
| 不落凭据 | 本轮不涉及凭据；`Redactor` 零改动仍是日志唯一收口 |
| 不写用户配置 | git 重写走环境变量；npm/pip 走环境变量，**不生成 `.npmrc` / `pip.ini`** |
| 系统级动作 | 本轮**无**任何需管理员权限的动作（无 hosts、无 ACL、无服务安装） |

---

## 8. 关键技术风险

| # | 风险 | 影响 | 验证方案 | 兜底 |
|---|---|---|---|---|
| **R-M2-01** | **命令级切级无法精准定位哪一组失败**：`hermes update` 是一条命令，内部同时拉 git/npm/pip | 可能降错组（把 npm 的失败算到 git 头上） | 用错误文本注入实测：把 `PIP_INDEX_URL` 指向黑洞，看归因是否落到 `Pip` 组 | 推断不出时**整体降一级**（保守但不会漏）；`[NET]` 行记录 `inferred=true/false` 便于事后复盘 |
| **R-M2-02** | **L0 优先会让国内网络每次多一次 3s 探测**（D-11 行为变更） | 启动摸底变慢；用户感知"卡了一下" | 本机实测 L0 探测耗时（`github.com` 目前 980ms 可达） | 探测 3s 超时即降 L1；`preferDirectL0=false` 可锁定 L1 |
| **R-M2-03** | **双源交叉校验需要两倍流量** | `install.ps1` 89 KB × 2 = 178 KB，可接受；但若将来校验大文件会翻倍 | 实测两源下载耗时（当前各 ~355ms / ~1000ms） | 只对「无官方 checksum 且体积 < 5 MB」的对象启用双源；大文件退为单源 + 长度 |
| **R-M2-04** | **冷却状态落盘的并发** | 两个实例同时写 → 后写覆盖 | 读失败即视为无记录；写入走 `.tmp` 替换 | 接受"最后写入者胜"（冷却信息非关键）；文件带 `schemaVersion` |
| **R-M2-05** | **per-group 状态机可能过度设计** | 8 组 × 3 级 = 24 个状态，代码复杂度上升 | 先实现，若实测发现多数组从不降级（如 `Installer` 组只有 L0），可后续收敛 | 初版即把「只有 1 个候选的组」短路掉（`CandidatesFor` 返回单元素时 `ShouldDowngrade` 恒 false） |
| **R-M2-06** | **速率阈值 100 KB/s 在慢速镜像下会误判** | 正常但慢的源被降级 | 用真实镜像测（npmmirror 实测 1.8 MB/s，tuna 207ms） | 阈值只在「本次传输量预计 > 5 MB」时才生效（PRD 原文），小文件不测速率 |
| **R-M2-07** | **`UV_INDEX_URL` 已被 uv 弃用**（实测 uv 0.12.19：`--index-url` 标注 `Deprecated: use --default-index instead`） | 若只注入 `UV_INDEX_URL`，新版本 uv 可能不生效 | 已实测确认参数名：`--index-strategy` 可选值仅 `first-index` / `unsafe-first-match` / `unsafe-best-match` | **现有 `NetworkPreparer.cs:41-42` 已同时注入 `UV_DEFAULT_INDEX` 与 `UV_INDEX_URL`，做法正确**——M2 保持两者并存即可，`UV_DEFAULT_INDEX` 为准；可选追加 `UV_INDEX_STRATEGY=unsafe-best-match`（多镜像时才需要） |

---

## 9. 评审记录

| 日期 | 参会人 | 结论 | 遗留问题 | 关联纪要 |
|---|---|---|---|---|
| 2026-10-24 | 芦哥（需求方）、Hermes | 待评审 | R-M2-01（归因可能降错组）、R-M2-07（uv 索引是否生效待实测）需芦哥知悉 | 本表即决议记录 |
