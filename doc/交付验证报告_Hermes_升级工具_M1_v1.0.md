# 交付验证报告 — M1（Hermes_Update 结论可信化与超时治理）

| 字段 | 内容 |
|------|------|
| 任务 | M1 里程碑：FR-1.1 / 1.2 / 1.3 / 1.6 / 1.7 / FR-2.5 / FR-2.8 共 7 条 FR |
| 状态 | ⚠️ **部分完成**（6 条 FR 全交付；1 条 FR-1.7 的端到端复原路径受环境限制未实测，见遗留问题） |
| 交付时间 | 2026-10-24 |
| 验证人 | Hermes（与执行方 Claude Code 分离） |
| 关联任务卡 | `.hermes/tasks/T-HU-DEV-1.md` ~ `T-HU-DEV-5.md`、`T-HU-QA-1.md` |
| 关联文档 | PRD v1.5、HLD v1.0、DD v1.0 |
| 代码提交 | `a4c7eb7`(DEV-1) / `1d234bf`(DEV-2) / `c295339`(DEV-3) / `5ca7369`(DEV-4) / `002452a`(DEV-5) / `1f93563`(文档修正) |

---

## 一、交付物

**新增 7 个 Core 文件（2,060 行）**

| 文件 | 行数 | 承接需求 |
|---|---|---|
| `Core/RetryPolicy.cs` | 169 | FR-1.3 / FR-2.8 |
| `Core/ResultVerifier.cs` | 524 | FR-1.1 / FR-1.7（A4） |
| `Core/VersionFingerprint.cs` | 228 | FR-1.2 / FR-2.5 |
| `Core/HomeRegistry.cs` | 272 | FR-1.6 |
| `Core/ServiceStateGuard.cs` | 343 | FR-1.7 |
| `Core/RunStateStore.cs` | 379 | FR-2.5 |
| `Core/UiTextBuilder.cs` | 145 | NFR-09（UI 逻辑外移） |

**改造 6 个文件**

| 文件 | 变更 | 说明 |
|---|---|---|
| `Core/InstallerEngine.cs` | +315 | 指纹接入、快照/复原接线、三态结论、按根停进程 |
| `Core/ProcessRunner.cs` | +159 | 超时重载（旧签名保留） |
| `Core/Models.cs` | +41 | `OperationOutcome` + `OperationResult` 三态扩展 |
| `Core/HermesPaths.cs` | +18 | 四个 state 路径属性 + **`InstallLooksPresent` 缺陷修复** |
| `MainForm.cs` | +39 | 三态图标、断言汇总、多根摘要（1,310 → **1,345**） |
| `Program.cs` | +4 | 锁被占时展示另一实例进度 |

**总量**：13 个文件，**+2,595 / −41**；`git diff 1128dd0..HEAD --stat -- src/` 逐文件与五张卡的 `files_scope` 并集完全一致，**零越界**。

---

## 二、验证结果（8 项清单）

| # | 验证项 | 结果 | 独立复现证据 |
|---|---|---|---|
| 1 | 代码符合详细设计 | ✅ | 逐组件对照 DD §2.2 G1–G10；参数表九类与 DD 冻结表逐格一致（`RetryPolicy.cs:44-167`） |
| 2 | 单元测试通过 | ⚠️ N/A | 项目无测试框架（AGENTS.md §4）。按制度层降级为「可复现命令」，全部以反射探针实现，见第三节 |
| 3 | 集成测试通过 | ✅ | 5 个探针、**38 条断言全部 PASSED**（末次复跑：`ALL CASES/CHECKS PASSED` ×5）；本机真实双数据根、真实网关进程参与验证 |
| 4 | 文档已更新 | ✅ | PRD §9.2 追踪矩阵 12 行回填；HLD 新增 R-12/R-13；DD §4.2 API-06 超时口径修正 |
| 5 | 代码规范一致 | ✅ | 干净重建 **0 警告 0 错误**（删 obj/bin 后全量）；语法红线三项全 0（15 个源文件） |
| 6 | 安全无漏洞 | ✅ | 无新增外部输入入口；本轮未引入凭据处理；`Redactor` 零改动仍为日志唯一收口 |
| 7 | 性能达标 | ⚠️ 部分 | 预算机制落地（CLI 1500s / 桌面 2400s + 无进展 180s 杀树）；**真实耗时 ≤25/40 min 未长跑验证**（需真机 3 次计时，AC-04 手法已证明中止能力） |
| 8 | 改动范围核对 | ✅ | 见第一节；14 条不改清单见第四节 |

---

## 三、逐卡独立验收证据（均为 Hermes 自行复跑，不采信执行方自报）

### T-HU-DEV-1 — 分级超时与无进展检测

| 用例 | 注入 | 实测 |
|---|---|---|
| A 无进展 | `Start-Sleep -Seconds 400` + NoProgress=5s | **5,636ms** 返回，`TimedOut=true`、`TimeoutKind=no-progress`、进程树清零 |
| B 总超时 | 同上 + Total=3s | **3,538ms** 返回，`TimeoutKind=total` |
| C 不误杀 | 持续输出 12 行 + 双限制 | `TimedOut=false`、`ExitCode=0`、StdOutLen=99 |
| 日志格式 | — | `[TIMEOUT] kind=no-progress limit=5000ms elapsed=5178ms lastOutputAgo=5178ms op=OperationBudget target=powershell.exe action=kill-tree`，与 DD 冻结格式逐字一致 |

### T-HU-DEV-2 — 四项断言与三态结论

| 用例 | 实测 |
|---|---|
| 三工厂 | `Ok→(Success,true)` / `Degrade→(Degraded,false)` / `Error→(Failed,false)`；`Success` 恒等于 `Outcome==Success` |
| 三态合成 | 全Pass→Success、+Warn→Degraded、+Skipped→Degraded、+Fail→Failed、Fail 优先于 Warn→Failed（五分支全对） |
| **A3 可失败验证** | `startedAt=-30d` → **Pass**；`startedAt=+1d` → **Fail**（正是修复的旧判据缺陷：原仅判 `File.Exists`，分不清是否本次产出）；`includeDesktop=false` → **Skipped** |

### T-HU-DEV-3 — 服务状态快照与复原

| 用例 | 实测 |
|---|---|
| 快照 | `CaptureAsync` 8,483ms 返回 `HadGateway=true`、`GatewayPid=20860`、`StatusRaw` 完整两行原文 |
| 双起防护 | 网关在跑时 `RestoreAsync` → `Restored=true`、`PidBefore=PidAfter=20860`、未发起任何启动命令 |
| 解析器 | 五案全对：真实输出→20860、`(PID: 4242)`→4242、`not running`→0、无PID行→0、空→0 |
| 首轮 P0 bug | 首轮照抄 DD 的 `EndpointProbe(5s)` 导致恒超时（实测 5.7s）→ A4 假通过；二轮统一 `BuildGatewayOptions()`（120s/300s）后修复 |

### T-HU-DEV-4 — 版本指纹与运行状态

| 用例 | 实测 |
|---|---|
| 三元组 | `gitHead=c78ca093aa4b6759c055c3ed2bfbe7f741cf0292`（40位）、`lockSha256=40b06778…`（64位小写hex）、`cliVersion=Hermes Agent v0.22.5+456` |
| IsSame | 同快照→True、篡改 gitHead→False、null 字段→False |
| 落盘 | `fingerprint.json` 写入并往返一致；`run.json` Begin 创建 → UpdateStage 持久化 → Clear 删除 |
| 双实例 | `ReadOther` 排除自身、能读到其他存活实例（pid=9440，op/stage 正确）；`TryReadStale` 新鲜→True、2小时陈旧→False |
| **D-1 红线** | **结构性成立**：`LoadLast` 全仓调用次数 = 0（grep 确认）；`fpBefore/fpAfter` 为方法局部变量，跨轮无法复用 |
| 首轮 bug | `gitHead` 恒空，根因是**既有代码** `InstallLooksPresent` 用 `File.Exists(".git")` 判目录（恒 false）；二轮改 `Directory.Exists \|\| File.Exists` 并解除其闸门作用 |

### T-HU-DEV-5 — 多数据根

| 用例 | 实测 |
|---|---|
| 枚举 | 2 根：`%LOCALAPPDATA%\hermes`（selected=true / hasCli=true / cli=0.22.5+456）、`%USERPROFILE%\.hermes`（selected=false / hasCli=false） |
| 去重 | `HERMES_HOME` 指向 LOCALAPPDATA 时仍返回 2 根（非 3），首个 `Kind=HERMES_HOME` |
| 归属 | 7 个进程全部落到选中根（Hermes×5 + python×2，含网关 PID 20860），**PID 无重复** |
| 隔离根 | 临时目录带 `bin\hermes.cmd` 时正确识别为 selected + hasCli（3 根） |
| 首轮 bug | 同一进程归属两次（12 条记录 5 个 PID 各两遍），根因 `ProcessNames` 含 `Hermes`+`hermes` 而 Windows `GetProcessesByName` 不区分大小写；二轮用 `HashSet<int> seenPids` 按 PID 去重 |

---

## 四、不改清单（DD §9 十四条）逐条核对

| # | 条目 | 结果 | 证据 |
|---|---|---|---|
| 1 | `HostsFixer.cs` 零改动 | ✅ | `git diff` 0 行 |
| 2 | `Mirrors.cs` / `NetworkPreparer.cs` / `NetworkProbe.cs` 零改动 | ✅ | 0 行 |
| 3 | `Redactor.cs` 零改动 | ✅ | 0 行 |
| 4 | `Settings.cs` 零改动（未加字段） | ✅ | 0 行 |
| 5 | `EnvironmentProbe.cs` 零改动 | ✅ | 0 行 |
| 6 | `Logger.cs` 零改动 | ✅ | 0 行 |
| 7 | `HermesPaths.HermesHome`(:8-21) 一行不改 | ✅ | `grep '^[-+].*HermesHome'` = 0 |
| 8 | `ProcessRunner` 旧签名保留且行为等价 | ✅ | 旧 6 参数重载存在；`InstallerEngine` 6 处调用点零改动且编译通过 |
| 9 | `InstallerEngine` 私有 helper 零改动 | ✅ | `ExtractJsonLine`/`ExtractLastJsonLine`/`GetString`/`GetBool`/`Truncate`/`AppendHermesLogs`/`StageResult` 的 diff 为空 |
| 10 | `RunUninstallAsync` 不接入断言 | ✅ | 方法段内 `VerifyAsync` 出现 0 次 |
| 11 | 语法红线（插值 / `?.` / `nameof`） | ✅ | 15 个源文件三项均 0 |
| 12 | 无 NuGet、不改 csproj | ✅ | `git diff` 0 行 |
| 13 | 不改 `build/build.ps1` | ✅ | 0 行 |
| 14 | 不由执行方 git commit/push | ✅ | 全部提交由 Hermes 逐卡验收后代为执行 |

**附加保留项**：`InstallerEngine.cs:377/473` 的 `result.ExitCode != 0` 判据与 `:314` 的 `Ok = result.ExitCode == 0` 阶段判据均**原样保留**（未被三态改造删改）。

---

## 五、AC 逐条判定（如实标注部分覆盖）

| AC | 需求 | 判定 | 依据 / 说明 |
|---|---|---|---|
| AC-01 | 依赖被阻断时结论必须为非成功 | ✅ | 三态结论已落地；`Failed`/`Degraded` 路径实测可达 |
| AC-02 | 结论展示 4 项自检与期望值对照 | ✅ | `ResultVerifier.Record()` 每条经 `Logger.Write` 落日志，含 Expected/Actual；`UiTextBuilder.VerificationLines` 供 UI |
| AC-03 | 已是最新时重跑依赖重装 = 0 | ⚠️ **部分** | 按 DD 偏差 D-1：指纹仅防**单次操作内**重复；跨会话「已是最新」由官方 `update` 幂等保证，本工具不额外拦截（否则用户永远升不了级） |
| AC-04 | uv 黑洞 900s 内中止并切级 | ⚠️ **部分** | 「中止」已实测且更严（180s 无进展即杀）；「切级」属 M2（FR-4），本轮不实现 |
| AC-05 | 编译失败不触发网络级重试 | ✅ | `ShouldRetry(NodeGyp/Generic/OperationBudget)` 恒 false；错误文本含 `error C`/`MSB`/`node-gyp` 亦 false |
| AC-08 | 双 home 列出两套进程/根/版本 | ✅ | 本机真实双根实测（枚举、去重、归属、隔离根） |
| AC-09 | 升级前网关在跑、中途被停则结束必须恢复 | ⚠️ **部分** | 双起防护、状态解析、命令拼接、失败降级路径均已实测/审查；**端到端「停→恢复」未测**，因停止网关会中断主控会话（HLD R-13） |
| AC-21 | 连续点 3 次只有 1 实例执行、其余提示可查看进度 | ✅ | Mutex 既有；锁被占时读 `run.json` 展示操作名/阶段/已进行时长；`ReadOther`/`TryReadStale` 实测正确 |
| AC-24 | 9 类操作逐项注入超时偏差 ≤10% | ⚠️ **部分** | 九类参数表已实现并被查询；仅对 `OperationBudget`、NoProgress、Total 三类做注入实测（偏差 1.2%–7.7%） |
| NFR-01 | CLI ≤25min、桌面 ≤40min | ⚠️ **部分** | 预算机制与中止能力到位；真实耗时未做 3 次长跑计时 |
| NFR-04 | 「已是最新」零副作用 | ✅ | 重复执行只有锁与日志变化；指纹相等时短路桌面端重建 |
| NFR-08 | 切换/重试/超时有结构化日志 | ✅ | `[TIMEOUT]` 行格式冻结并实测；`[FP]`/`[GATEWAY]`/`[STATE]` 前缀可 grep |
| NFR-09 | `MainForm.cs` 不得继续膨胀 | ✅ | 1,310 → **1,345**（+35，预算 ≤45），逻辑外移 `UiTextBuilder` |
| NFR-11 | 沿用 C# 5/6/7 风格 | ✅ | 插值/`?.`/`nameof` 三项全 0 |

**统计**：全通过 9 项 / 部分通过 5 项 / 未通过 0 项。

---

## 六、偏差与设计层修正记录

| 编号 | 内容 | 处置 |
|---|---|---|
| D-1 | PRD FR-1.2 原文「指纹一致则跳过源码更新」若跨会话使用会导致永远无法升级 | DD 修正为「仅单次操作内防重复」，并加结构性保证（`LoadLast` 零调用） |
| D-2 | FR-1.3 的「切级」属 M2 | 本轮只落实「中止」，AC-04 标部分 |
| D-3 | FR-1.7 复原命令由 `gateway start` 改为 `ensure → start` | 依实测（`ensure` 官方语义为「不装服务拉起运行时」） |
| D-4 | A2 断言具体化为 `import openai` + 操作前可导入才断言 | 实测本机 openai 2.54.0 可导入 |
| D-5 | NFR-09 允许 `MainForm` 净增 ≤45 行 | 实际 +35 |
| D-6 | DD §4.2 API-06 原写 `gateway status` 用 `EndpointProbe(5s)` 是错的 | 已按实测改为 `Generic` 定制（120s/300s），HLD 新增 R-12 |
| 实现层 | 二次构建条件最终形态 `includeDesktop && !versionUnchanged && (!DesktopBuilt \|\| !IsDesktopArtifactFresh(startedAt))` | 比 DD 与 DEV-2 中间版本更完整（指纹无变化时不白跑 40 分钟重建），接受 |
| 实现层 | `StopHermesProcessesAsync` 重载用 `IList<string>` 而非 `List<HermesHomeInfo>` | 路径列表已足够做前缀匹配，接受 |

---

## 七、遗留问题

| 问题 | 影响 | 跟进 |
|---|---|---|
| **AC-09 端到端复原未实测** | 若 `gateway ensure/start` 在本机真实停止态下行为与预期不符，复原会失败——但结论会如实降级（不会假成功） | 需在**不依赖网关的时机**（无对话/关机前）跑一次：先记 PID → 停网关 → 跑一次升级 → 确认 `gateway status` 恢复。建议列入 M2 首个验收项 |
| AC-03 / AC-04 / AC-24 / NFR-01 部分覆盖 | 切级能力、9 类全量注入、真实耗时计时未验 | 分别归 M2（FR-4 分级网络）、M3（状态机/回滚）、M4（版本矩阵） |
| `EndpointProbe(5s)` 类短超时对 CLI 命令普遍偏紧 | 已在本轮踩到一次（gateway status） | M3 的 `PathResolver` + 统一命令超时策略一并治理 |
| `InstallLooksPresent` 的缺陷在本轮被顺带修复 | 修复前 `EnvironmentProbe` 的「既有安装」判定恒为「未安装」 | 已在 DEV-4 一并修正，无遗留 |
| **`EnvironmentProbe.FindHermesProcesses` 存在与 DEV-5 同类的重复缺陷** | UI/自检的「运行中的进程」一栏同一 PID 显示两次（实测 selftest 输出：`Hermes (PID 5384)`、`(PID 10016)`、`(PID 28256)`、`(PID 2268)`、`(PID 9316)` 各出现两遍）。**功能不受影响**——停进程走的是 `StopHermesProcessesAsync` 按根路径匹配的另一条路径，重复 kill 同一 PID 无害 | 该文件在 DD §9 不改清单内（M1 不涉及探针改造）。根因与 DEV-5 完全相同：`:318` 的 `new[] { "Hermes", "hermes", "hermes-gateway" }` 在 Windows 上大小写不敏感，同一批进程被枚举两次。建议随 M3 的 `EnvironmentProbe` 扩展一并按 PID 去重 |

---

## 八、结论

**⚠️ 部分完成 —— 6 条 FR 全量交付并通过独立验收，FR-1.7 的端到端复原受环境限制未测，标为部分验证。**

- 构建：干净重建 **0 警告 0 错误**，产物 `HermesInstaller.exe` 158.5 KB / zip **76.4 KB**
- **产物真机冒烟**：`HermesInstaller.exe --selftest` 跑通全链路——环境探针 11 项全过（Win11 build 26200 / AMD64 / PS 5.1 / 磁盘 76.4 GB / 内存 32 GB / 长路径已开 / .NET 4.8）、**CLI 状态正确显示「已安装 0.22.5+456.gc78ca093 @c78ca093」**（`InstallLooksPresent` 修复生效的直接证据）、桌面端 235 MB 已构建、网络探测 11 端点 9 可用、install.ps1 下载成功 86.8 KB、阶段清单 9 个全部解析
- 探针：5 组、**38 条断言全部 PASSED**（末次整体复跑）
- 红线：14 条不改清单 **14/14 通过**；语法红线三项全 0
- 偏差：设计层 6 条（D-1~D-6）+ 实现层 2 条，全部记录在案，无未记录偏差
- 首轮失败 3 次（DEV-3 / DEV-4 / DEV-5），全部由 Hermes 独立实测发现并二轮修复，**无一例假通过进入交付**

**下一步**：进 S7（CLS），执行 `codegraph sync`、回填 `AGENTS.md` 技术地图数字、更新 PRD 追踪矩阵状态、归档看板。
