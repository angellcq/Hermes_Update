# PRD：Hermes Agent Windows 安装/升级工具 —— 跨机器健壮性与多级网络源策略

| 字段 | 内容 |
|------|------|
| 文档版本 | v1.5（S2：本轮范围锁定 M1 + 追踪矩阵 + 现状锚点实证） |
| 文档状态 | **已确认（2026-10-24 芦哥评审通过）**——S3 已准入；范围锁定 M1 七条 FR，其余 27 条 FR 留待后续版本 |
| 需求负责人 | Hermes |
| 优先级 | P0 |
| 需求来源 | 用户 2026-10-24「走全流程」指令 + 同日拍板 D1=A / D2=A / D3=A / D4=hermes-update |
| 创建日期 | 2026-10-24 |
| 最后更新 | 2026-10-24 |
| 证据基线 | 本机 2 份升级日志 + 本机运行时日志 5 份 + 云正事 2 份升级日志 + 官方文档（抓取 2026-10-24）+ 代码锚点实测（2026-10-24，见 §9.2） |
| 涉及代码 | `src/HermesInstaller/`（net48 单文件 WinForms，Core 分层；实测 14 个 .cs / 3,996 行） |

**版本历史**

| 版本 | 日期 | 修改说明 |
|------|------|----------|
| v1.0 | 2026-10-24 | 初稿（模板骨架，无日志证据，作废） |
| v1.1 | 2026-10-24 | 重写：全部需求由真实日志归纳，补 FR/AC/参数表；路径与凭据全部占位符化 |
| v1.2 | 2026-10-24 | 按 D-01（L1 加速器可尝试）/ D-02（云正事保留桌面端源码构建）更新 FR-2.1、FR-2.8、FR-3.2、FR-4.1，新增 FR-3.4 与 NFR-13、AC-29/AC-30 |
| v1.3 | 2026-10-24 | 新增 FR-2.9 路径解析与禁止硬编码（依赖/工具路径逐机解析）、NFR-14、AC-31；按 D-03 补充提权修复确认要求与 AC-32 |
| v1.4 | 2026-10-24 | 补齐验收覆盖：为 FR-3.1 / FR-4.2 / FR-4.4 / FR-4.7 补 验收 指向，新增 AC-33 / AC-34 / AC-35（34 条 FR 全部有对应 AC） |
| v1.5 | 2026-10-24 | **S2 定稿**：①移入 `doc/` 并锁定本轮范围 D1=A（仅 M1 的 7 条 FR）；②新增 §9 需求追踪矩阵（含每条 FR 的**现状锚点 `文件:行号`**，逐条实测）；③新增 §10 评审记录；④**同日评审通过**（D5），状态改「已确认」，S3 准入 |

---

## 0. 文档说明

- **证据标注**：每条结论后以「E#」标注来源，附录 F 给出完整索引。
- **脱敏约定**：用户目录统一写 `C:\Users\<USER>`，数据根写 `%LOCALAPPDATA%\hermes`；SSH 主机写 `<SSH_HOST>`，凭据写 `<占位符>`。全文不含真实用户名、机器名、密码、token。
- **优先级定义**：P0 = 导致升级失败或结果不可信，必须本期解决；P1 = 显著浪费时间或显著提高失败率；P2 = 体验与长期维护。

---

## 1. 背景与问题

工具现为「单文件 WinForms 外壳 + 调用官方 `install.ps1`」的结构，功能可用，但两次真实跨机器运行暴露出三类系统性问题：

1. **结果不可信**：两台机器结尾都报「升级完成」，但依赖实际失败、桌面端未构建、网关处于停止态（E2）。
2. **时间大量浪费**：本机 CLI 升级 47 分 30 秒，其中 30 分钟是 `uv` 超时空转（E1）；桌面端 57 分 27 秒，其中 21 分钟是同一件事做了两遍（E3）。
3. **网络策略缺失**：镜像变量只覆盖 npm/pip 的一部分，`uv` / git 依赖 / Node headers / Electron 打包仍直连（E5、E14）；环境里残留的死代理反而把直连堵死了（E5）。

### 1.1 输入材料（脱敏）

| 类别 | 材料 | 用途 |
|------|------|------|
| 本机·工具日志 | `%LOCALAPPDATA%\HermesInstaller\logs\CliUpgrade-20261024-001158.log` | CLI 升级主证据（47m30s 时间线、超时、依赖失败） |
| 本机·工具日志 | `%LOCALAPPDATA%\HermesInstaller\logs\DesktopUpgrade-20261024-070631.log` | 桌面端升级主证据（57m27s、双跑、ACL） |
| 本机·运行时 | `%LOCALAPPDATA%\hermes\logs\update.log` | 多轮 `hermes update` 失败样本（死代理、429、部分下载、EPERM、模块遮蔽） |
| 本机·运行时 | `%LOCALAPPDATA%\hermes\logs\bootstrap-installer.log` | SOCKS 握手失败、`unsupported scheme socks5h` |
| 本机·运行时 | `%LOCALAPPDATA%\hermes\logs\` 下 `errors.log` / `agent.log` / `desktop.log` | MCP 节点路径失效、SQLite 告警、插件 401 |
| 云正事·工具日志 | `CliUpgrade-20261024-073253.log` | node-gyp 缺 VC++、git 依赖直连超时、23m44s 时间线 |
| 云正事·工具日志 | `DesktopUpgrade-20261024-081127.log` | 双跑、ACL 101810 条目、双 home 进程归属 |
| 官方文档 | `https://hermes-agent.nousresearch.com/docs/getting-started/installation`、`…/docs/user-guide/windows-native` | 标准环境与依赖版本（抓取 2026-10-24） |

> 云正事的两份日志实际存放在 D 盘根目录、文件名以「云正事的日志输出」为前缀（原给定路径中的目录并不存在）。这本身就是一条可用证据：**路径不能假设为目录，中文路径与文件名粘连必须在代码里容错**（见 FR-2.2）。

### 1.2 两台机器基线对比（跨机器差异清单）

| 维度 | 本机 | 云正事 | 差异带来的风险 |
|------|------|--------|----------------|
| 用户目录 | `C:\Users\<USER>` | `C:\Users\<USER>`（不同账户） | 路径必须动态取，不能跨机复制 |
| Hermes 数据根 | `%LOCALAPPDATA%\hermes` | `%LOCALAPPDATA%\hermes` **且**遗留 `%USERPROFILE%\.hermes` 并存 | 进程清理只覆盖一个 home，另一 home 的 gateway 存活（E16） |
| CLI 版本轨迹 | `v0.22.5`（detached HEAD） | `v0.22.1+83` → `main@c78ca09`（`v0.22.5+456`） | 通道不一致（stable vs main），工具未提示 |
| CLI 升级耗时 | 47m30s（00:11:58→00:59:28） | 23m44s（07:32:53→07:56:37） | 无时间预算，慢到用户以为卡死 |
| 桌面端耗时 | 57m27s（07:06:31→08:03:58） | 14m59s（08:11:27→08:26:26） | 同上 |
| git 源 | 直连 `github.com`（CLI）/ `gh-proxy.com`（桌面） | `cnb.cool` 社区镜像（CLI）/ `gh-proxy.com`（桌面） | 同机两条流程两条策略，无统一判据 |
| 包镜像覆盖 | npm / electron / electron-builder / pip = npmmirror + 清华 | 同左，但 Node headers 仍直连 `nodejs.org` | 镜像覆盖不全，关键路径仍走国际出口 |
| 原生构建链 | 可用（桌面端构建成功） | VS2026 在 `D:\Program Files` 但**缺 VC++ 工具集** → node-gyp 失败 | 未预检即编译，白跑两轮（E13）；D-02 决策后该机**必须**能构建 → 需工具链修复而非降级 |
| 工具库 | node 26.7.0 + 26.12.1、uv 0.12.3 + 0.13.5、python 3.14.7 + 3.14.9，`tools` 2.1 GB | 运行中途 node 由 26.7.0 换到 26.12.1 | 无 GC、任务期间工具版本漂移（E17） |
| 结尾结论 | 「升级完成」，但依赖失败、网关停止 | 「升级完成」，但 native 构建失败、插件被禁用 | 最高风险：假成功（E2） |

### 1.3 日志分析结论（现象 → 根因 → 方案 → 优先级）

| # | 现象（证据） | 根因 | 优化方案 | 优先级 |
|---|--------------|------|----------|--------|
| 1 | 两台机器结尾均输出「升级完成」，实际 Python/Node 依赖失败、桌面端未构建（E2） | 成败判定只看阶段退出码与「最后一行动作」，无依赖健康断言 | 结果三元组（成功 / 降级成功 / 失败）+ 结束前强制自检 | P0 |
| 2 | 同一次桌面端升级把 `hermes update` 跑了两遍，第二遍输出 `Already up to date!`（本机 07:06:37→07:41:53 与 07:42:22→08:03:35；云正事 08:11:32→08:20:27 与 08:20:34→08:26:11）（E3、E15） | 编排把「源码更新 + 依赖补齐」与「常规 update」各触发一次 | 合并为单条编排；版本指纹判等，相等即跳过 | P0 |
| 3 | `uv sync timed out after 1800 seconds`，本机 00:29:13→00:59:13 空转 30 分钟（E1） | 超时按「最坏情况」而非「可接受时长」设定；无「无进展」检测 | 分级超时：单包 120s、整体 900s、无进展 120s 即失败 | P0 |
| 4 | 同一批 wheel（openai / pillow / pywin32）在 `files.pythonhosted.org` 被反复下载多次（E1） | 直连不稳且未复用缓存；`uv.lock` hash 固定使镜像索引不生效 | uv 缓存复用；`UV_INDEX_URL` + 工具级 index 覆盖；按 lock 内 sha256 校验后入库 | P0 |
| 5 | 云正事 Python 依赖连续 3 次解析失败（`misaki`、`psutil`），均以 `github.com:443` 连接超时 21s 告终（E14） | git+https 依赖不在镜像覆盖范围，且使用前不探测 | git 依赖 URL 重写（镜像前缀）+ 使用前连通性探测 | P0 |
| 6 | `Failed to connect to github.com port 443 via 127.0.0.1` / `over proxy 127.0.0.1`（E5） | 环境残留已失效的本地代理，反向阻断直连 | 启动时实测 `HTTP(S)_PROXY`、`ALL_PROXY`、`git http.proxy`；失效则本次临时摘除并记录 | P0 |
| 7 | `io error during SOCKS handshake`、`failed to create underlying connection`、`unsupported scheme socks5h`（E11） | scheme 不统一（`socks5h` 不被部分客户端支持）、隧道未就绪即使用 | scheme 规范化（socks5h 优先，不支持回落 socks5）+ 使用前三步健康检查 | P0 |
| 8 | `ZIP update failed: got only 15231214 out of 73151808 bytes`（E6） | 无 Range 续传、无完整性校验 | 断点续传 + 长度与 sha256 双校验 + 换源重下 | P0 |
| 9 | 云正事 node-gyp 失败两轮（07:33:26–07:36:36、07:48:26–07:51:31），报缺 VC++ 工具集（E13） | 未预检构建工具链即编译；编译类失败被当成网络类重试 | 预检 `vswhere` + `cl.exe` + SDK；缺失即阻断并进入 FR-3.4 修复流程（D-02：必须支持桌面端源码构建），修复后自检通过才重试编译 1 次 | P0 |
| 10 | 云正事另一 home 的 gateway（PID 16044，属 `%USERPROFILE%\.hermes`）被判定 `left alone` 后继续存活（E16） | 进程清理只覆盖单一数据根 | 枚举全部数据根，列出进程-归属-动作，交 UI 由用户选择 | P0 |
| 11 | 升级成功后网关仍停止（`Paused gateway(s) left stopped: dependencies are not current`）（E2） | 失败/降级路径未做服务状态复原 | 收尾增加「服务状态复原」步骤，失败至少回到升级前状态 | P0 |
| 12 | ACL 修复扫描 32118（本机 19m13s）/ 101810（云正事 4m04s）条目后 100% `skipped`（E4、E15） | 无条件执行 ACL 修复，实际零收益 | 按需触发（仅权限类错误）+ 采样上限（≤5000 条 / ≤120s） | P1 |
| 13 | 杀软干扰：`EPERM: operation not permitted, unlink '…@rolldown/binding-win32-x64-msvc.node'`（E7） | 实时扫描锁定 `.node` 文件 | 预检 Defender 与第三方 AV；给排除建议；失败延迟 5s 重试 1 次 | P1 |
| 14 | 工具库双版本共存，且任务进行中 node 由 26.7.0 换成 26.12.1（E17） | 无 GC；工具升级与业务任务并行 | 任务期间冻结工具版本；任务结束 GC，保留当前 + 上一版 | P1 |
| 15 | 清理失败 `.previous-python-3.14.7…\libcrypto-3-x64.dll [WinError 5]`；每轮 `dependency generation cleanup skipped`（E10） | 文件被运行中进程占用，失败仅 warn | 清理前停进程；写「待清理清单」，下次启动重试 | P1 |
| 16 | `cannot import name 'base_url_origin' from 'utils' (D:\tools\utils.py)`（E8） | 用户目录进 `sys.path`，同名模块遮蔽 Hermes 自身 `utils` | 启动检测模块遮蔽；子进程隔离模式（`-I` / `-P`）运行 | P1 |
| 17 | 配置迁移后 `platform 'homeassistant' references unknown toolset 'hermes-homeassistant'`（云正事，config v51→v52） | 迁移只改版本号，不做引用完整性校验 | 升级后配置校验 + `hermes doctor`，失败给一键修复建议 | P1 |
| 18 | `429` 限流、`git fetch timed out after 300s`、`Could not refresh release history (fatal: index-pack failed)`（E9） | 无 Retry-After 处理；release 历史失败无降级 | 429 按 Retry-After 退避；release 历史失败降级为 tag 列表 | P1 |
| 19 | MCP 配置指向 `desktop-runtime\hermes-runtime-hermes-agent-0.20.0-win-x64\node\node.exe`，升级后路径已不存在（E12） | 运行时目录随版本变化，配置未回写 | 升级后校验 MCP `command` 路径，失效则按当前运行时重写 | P2 |
| 20 | autostash 残留 2 条且超过 7 天，仓库持续膨胀 | 无自动回收策略 | 升级前提示并可一键清理（保留最近 1 条） | P2 |
| 21 | **依赖与工具路径逐机不同**：PM 生成带版本目录（`node-26.7.0`、`python-3.14.7+…`）、桌面运行时 `…0.20.0…` 路径升级后失效、云正事 VS 在 D 盘（E12、E17、E13） | 代码/配置中存在路径字面量假设，探测失败时回落到默认值 | 统一走 `Core/PathResolver.cs` 运行时解析 + 禁止字面量（FR-2.9、NFR-14 静态检查） | P0 |

### 1.4 耗时与浪费量化

| 指标 | 本机 | 云正事 | 目标 |
|------|------|--------|------|
| CLI 升级总耗时 | 47m30s | 23m44s | ≤ 25 min |
| 其中可消除的浪费 | 30m00s（uv 超时） | 6m15s（node 依赖两轮编译失败） | 0 |
| 桌面端升级总耗时 | 57m27s | 14m59s | ≤ 40 min |
| 其中重复执行 | 21m13s（第二遍 update） | 5m37s | 0 |
| ACL 无效扫描 | 19m13s | 4m04s | ≤ 2 min 且按需 |
| 结尾结论可信度 | 报成功 / 实为降级 | 报成功 / 实为降级 | 必须如实反映 |

### 1.5 现有实现能力基线（避免重复建设）

| 能力 | 现状（代码位置） | 本期缺口 |
|------|------------------|----------|
| 环境探针 | `Core/EnvironmentProbe.cs`：Win build ≥17763、架构、PowerShell ≥5.1、磁盘（最小 3G / 建议 8G）、内存、.NET 4.8、长路径、既有安装、运行进程、管理员、系统代理、安全软件、系统时间 | 构建工具链检测与修复（FR-3.4）、代理可用性实测、模块遮蔽、多 home 枚举、路径字符体检 |
| 网络探测 | `Core/NetworkProbe.cs`：HTTP 与 `git ls-remote`，产出延迟/错误 | 速率阈值、丢包统计、分级切换编排 |
| 镜像注入 | `Core/NetworkPreparer.cs`：npm / electron / electron-builder / pip 环境变量 | uv 索引、git 依赖重写、Node headers、Playwright、Chromium 快照 |
| 镜像清单 | `Core/Mirrors.cs`：官方端点 + gh-proxy / ghproxy.net / npmmirror / 清华 | 分级策略对象、可用性权重、冷却状态 |
| 进程管理 | `InstallerEngine.StopHermesProcessesAsync` | 多 home 归属识别、按 home 选择、复原 |
| 超时与重试 | `Core/ProcessRunner.cs`：超时、取消、流式输出 | 分级超时、无进展检测、按失败类型区分重试 |
| 日志与脱敏 | `Core/Logger.cs` + `Core/Redactor.cs` | socks 凭据、SSH 主机、诊断包导出 |
| 状态与续跑 | 无 | 阶段状态机、回滚、断点续装（全新） |

---

## 2. 目标与非目标

### 2.1 目标

1. **结论可信**：操作结束给出的「成功/降级/失败」必须与实际状态一致，并附可复核的自检输出。
2. **时间可控**：CLI 升级 ≤ 25 min，桌面端 ≤ 40 min；超预算主动中止并产出诊断包，而非静默空转。
3. **跨机器可复制**：同一份 exe 在 C/D 盘、不同用户名、含中文/空格路径、有无构建工具链的机器上行为确定。
4. **失败可恢复**：任一阶段失败可回滚；中断重跑无副作用；一键导出脱敏诊断包。
5. **网络分级明确**：官方直连优先，仅失败/超时/不稳定时逐级回退；私人代理只作最后一级且默认关闭。

### 2.2 非目标

- 不替换官方 `install.ps1` 的安装逻辑，只做编排、守卫、兜底与网络策略。
- 不支持 Linux / macOS；不支持 32 位 Windows。
- 不修复 Hermes 运行时自身缺陷（插件 401、SQLite 告警等），只负责识别、记录、提示。
- 不托管用户业务数据。

---

## 3. 用户场景

| 编号 | 场景 | 关键诉求 | 关联需求 |
|------|------|----------|----------|
| US-01 | 家用机器（有代理、有构建工具链）升级 CLI + 桌面端 | 一次点完，25 分钟内完成，结论可信 | FR-1.1、FR-1.2、FR-2.8 |
| US-02 | 办公机器（无构建工具链、无代理）只升级 CLI | 自动跳过需要编译的步骤并明示，不空跑 | FR-1.3、FR-3.2 |
| US-03 | 用户目录含中文或空格、装在 D 盘 | 正常运行，不因路径字符失败 | FR-2.2 |
| US-04 | 网络时好时坏 | 自动降级到可用源，日志能看出在哪一级、为什么切 | FR-4.1、FR-4.2 |
| US-05 | 升级中途断电/关机 | 重跑能续上，不重复下载、不留半成品 | FR-2.3、FR-2.6 |
| US-06 | 升级后功能异常需报障 | 一键导出诊断包（已脱敏） | FR-2.7 |
| US-07 | 机器上有两套 Hermes 数据根 | 明确列出两套进程与版本，用户选择处理哪一个 | FR-1.6 |
| US-08 | 有私人 SOCKS5 隧道但通常关闭 | 仅在公共源全挂时提示启用，凭据不落盘不落日志 | FR-4.5、FR-4.6 |

---

## 4. 功能需求

### 4.1 FR-1 问题诊断与优化项

**FR-1.1 结论可信化（P0）**
- 操作结束前必须执行结果断言，任一项不通过即不得报「成功」。
- 断言集：① `hermes --version` 返回预期版本；② 关键依赖可导入；③ 桌面端产物存在（仅当包含桌面端）；④ 服务状态与升级前一致或更好。
- 验收：AC-01、AC-02。

**FR-1.2 消除重复执行（P0）**
- 以版本指纹 = `git rev-parse HEAD` + `pm/lock.json` sha256 + CLI 版本 三者组合判等；一致则跳过源码更新与依赖安装，只做自检。
- 验收：AC-03。

**FR-1.3 分级超时与无进展检测（P0）**
- 所有网络与子进程调用按 FR-2.8 设连接超时、无进展超时、总超时；编译类失败不重试，网络类失败按 FR-4 换源。
- 验收：AC-04、AC-05。

**FR-1.4 依赖下载去重与缓存（P0）**
- uv/npm 使用持久缓存目录；下载后按 lock 内 sha256 / npm integrity 校验，通过即复用到后续重试。
- 验收：AC-06。

**FR-1.5 git 依赖镜像化（P0）**
- `git+https://github.com/...` 依赖在注入环境中重写为镜像前缀，并对 git 依赖单独做连通性探测。
- 验收：AC-07。

**FR-1.6 多数据根识别（P0）**
- 枚举 `%LOCALAPPDATA%\hermes`、`%USERPROFILE%\.hermes`、`HERMES_HOME` 及自定义 `-HermesHome`；逐一列出进程 PID、所属根、版本；默认只处理选中根，其余明确提示「已保留运行」。
- 验收：AC-08。

**FR-1.7 服务状态复原（P0）**
- 收尾检查网关/桌面端进程，升级前在运行而当前未运行则尝试恢复；恢复失败结论显示为「降级」。
- 验收：AC-09。

**FR-1.8 ACL 修复按需化（P1）**
- 仅在本次出现权限类错误（`WinError 5`、`Access denied`）后触发；单次扫描上限 5000 条目 / 120 秒。
- 验收：AC-10。

**FR-1.9 杀软干扰应对（P1）**
- 预检 Defender 实时保护与常见第三方 AV；命中时提示排除 `%LOCALAPPDATA%\hermes`；`EBUSY/EPERM` 延迟 5s 重试 1 次。
- 验收：AC-11。

**FR-1.10 工具库版本冻结与 GC（P1）**
- 任务期间不升级 node/uv/python；任务结束清理非当前、非上一版目录；被占用目录进待清理清单。
- 验收：AC-12。

**FR-1.11 环境污染检测（P1）**
- 检测 `PYTHONPATH`、`PYTHONSTARTUP` 及 Hermes 目录外的同名模块遮蔽；子进程以隔离模式启动。
- 验收：AC-13。

**FR-1.12 配置完整性校验（P1）**
- 升级后执行配置校验与 `hermes doctor`，把「引用未知 toolset / 未知 platform」转为可读结论与修复建议。
- 验收：AC-14。

**FR-1.13 限流与降级（P1）**
- 429 按 `Retry-After` 退避（上限 3 次）；release 历史失败降级为 tag 列表，不阻塞主流程。
- 验收：AC-15。

**FR-1.14 运行时路径回写（P2）**
- 升级后校验 MCP `command`、快捷方式目标等运行时路径，失效则按当前版本目录重写。
- 验收：AC-16。

### 4.2 FR-2 守卫与兜底

**FR-2.1 运行前置检查（P0）**
在既有 `EnvironmentProbe` 基础上补齐为可阻断门禁：

| 检查项 | 判据 | 阻断级别 |
|--------|------|----------|
| 磁盘空间 | 目标盘可用 ≥ 8 GB（最低 3 GB） | Fail 阻断 |
| 目录读写权限 | 目标目录创建/写/删临时文件成功 | Fail 阻断 |
| 网络连通性 | L0 官方端点 TCP+TLS 探测（3s/5s） | 不阻断，直接进 L1 |
| 管理员权限 | 仅 hosts / ACL 修复需要，主流程不要求 | 按需提示 |
| 杀软干扰 | Defender 实时保护 + 已知误报项 | Warn，提示排除 |
| 路径字符 | 中文/空格/`&`/`%`/`!`、长度 >200、UNC | 中文空格 Warn + 安全调用；>200 Warn；UNC Fail |
| 构建工具链 | `vswhere` + `cl.exe` + Windows SDK 三者齐备（D-02：云正事必须支持桌面端源码构建） | 缺失 → Fail 阻断，进入 FR-3.4 修复流程（不得静默降级） |
| 系统代理 | 实测可用性，失效即本次摘除 | Warn |
| 模块遮蔽 | Hermes 目录外同名模块 | Warn |

- 验收：AC-17。

**FR-2.2 跨机器适配（P0）**
- 所有路径由 `HermesPaths` 动态推导（`HERMES_HOME` → `%LOCALAPPDATA%\hermes`），禁止字面量盘符与用户目录。
- **机器名与用户名不得进入任何逻辑判断**，仅可作日志标签。
- 路径统一 `Path.Combine` + 引号安全拼接；含空格路径在子进程调用中必须加引号。
- 中文路径、目录/文件名粘连、UNC 路径必须显式容错（本次云正事日志路径即为此类）。
- 验收：AC-18。

**FR-2.3 阶段状态机与断点续装（P0）**
- 阶段：`precheck → source-update → deps-python → deps-node → desktop-build → config-migrate → verify → finalize`。
- 状态落盘 `%LOCALAPPDATA%\HermesInstaller\state\stage.json`（阶段、时间、指纹、产物校验值、失败原因）。
- 重跑时已完成且指纹未变的阶段跳过。
- 验收：AC-19。

**FR-2.4 分阶段回滚（P0）**
- 升级前记录还原点：`git rev-parse HEAD`、`uv.lock` / `package-lock.json` 摘要、CLI 版本、服务状态。
- 失败回滚：`git reset --hard <snapshot>` + 按锁文件重装依赖 + 服务状态复原；保留最近 2 个还原点。
- 验收：AC-20。

**FR-2.5 幂等性（P0）**
- 重复执行无副作用：已是最新时只自检并退出；重复清理、重复写 hosts、重复改配置必须可重入。
- 单实例锁 + 心跳：锁存在时 UI 显示「另一实例正在执行（阶段 X，已进行 Y），点击查看日志」，不让用户反复重试。
- 验收：AC-21。

**FR-2.6 残留清理（P1）**
- 对象：`.previous-*` 工具目录、失败下载临时文件、半成品构建目录、超期 autostash。
- 占用即失败：写「待清理清单」，下次启动重试，绝不静默忽略。
- 验收：AC-22。

**FR-2.7 一键诊断包（P0）**
- 内容：本次工具日志全量、`update.log` / `bootstrap-installer.log` / `errors.log` 尾部 2000 行、`hermes --version`、`hermes doctor`、工具库清单、磁盘与内存、分级探测结果（每级延迟/速率/失败原因）、代理配置（脱敏）、`git status` 与 stash 列表、阶段状态快照。
- 出口：`%USERPROFILE%\Desktop\HermesDiag-<yyyyMMdd-HHmmss>.zip`，≤ 20 MB。
- 脱敏：强制走 `Redactor`，新增规则见附录 D。
- 验收：AC-23。

**FR-2.8 超时与重试参数（P0）**

| 操作 | 连接超时 | 无进展判定 | 总超时 | 重试 | 退避 | 超时/失败动作 |
|------|----------|------------|--------|------|------|----------------|
| 端点探测（TCP/HTTP HEAD） | 3s | — | 5s | 2 | 1s、2s | 标记该级不可用 |
| 单文件下载 | 10s | 30s 无数据 | 90s | 3 | 1s、2s、4s | 换下一级源 |
| `git fetch` | 30s | 60s 无输出 | 180s | 2 | 2s、5s | 换下一级源 |
| `git clone` | 30s | 60s 无输出 | 600s | 2 | 2s、5s | 换下一级源 |
| `npm ci` | — | 120s 无输出 | 900s | 1 | — | 失败 + 诊断包 |
| `uv sync` | 30s/包 | 120s 无进展 | 900s | 2 | 5s、15s | 换源；仍失败则回滚 |
| `node-gyp` 编译 | — | 180s 无输出 | 600s | 0（编译失败不重试；缺工具链先走 FR-3.4，修复后可重试 1 次） | — | 触发工具链修复；仍失败则报失败，**不得报成功** |
| 子进程通用 | — | — | 1800s 硬上限 | 0 | — | SIGTERM → 10s → `taskkill /T /F` |
| 单次任务总预算 | — | — | CLI 25 min / 桌面 40 min | — | — | 中止 + 诊断包 |

- 验收：AC-24。

**FR-2.9 路径解析与禁止硬编码（P0，覆盖全部依赖与工具路径）**
- 原则：**任何依赖、工具、运行时、配置的路径都必须在运行时从本机解析**；代码与配置中不得出现盘符、用户名、版本号目录、安装目录字面量。解析失败必须显式报错并进入修复，**禁止用「默认路径」兜底**——本机 `errors.log` 里 `D:\tools\...` 这类历史残留正是硬编码兜底的后果（E8）。
- 禁止硬编码清单（含但不限于）：

| 类别 | 禁止写法 | 两机的真实差异 |
|------|----------|----------------|
| 数据根 | `C:\Users\<USER>\AppData\Local\hermes` 字面量 | 账户名不同；云正事另有 `%USERPROFILE%\.hermes` 并存 |
| 盘符 | `D:\...` / `C:\...` | 云正事 VS 装在 `D:\Program Files`，本机在 C 盘 |
| VS / MSVC | 典型安装路径、`2019`/`2022`/`2026` 版本号目录 | 云正事为 VS2026 且位于 D 盘 |
| 工具链 | Python / Node / uv / npm / git 的绝对路径 | 由 PM 生成带版本目录（`node-26.7.0`、`uv-0.13.5`、`python-3.14.7+…`），随升级变化 |
| 桌面运行时 | `desktop-runtime\...\0.20.0-...\node\node.exe` | 版本号随升级变化，旧值直接失效（E12） |
| 已知文件夹 | 拼 `%USERPROFILE%\Desktop` | 桌面可能被 OneDrive / 组策略重定向 |
| 第三方目录 | 把用户自定义目录（如 `D:\tools`）写进 PATH / PYTHONPATH | 造成同名模块遮蔽（E8） |

- 动态解析来源（按优先级）：
  1. 环境变量与已知文件夹 API（`SHGetKnownFolderPath`，而不是拼 `%USERPROFILE%`）；
  2. 工具自带清单：`pm/lock.json` + PM 查询命令（工具版本目录的唯一真值）；
  3. 可执行文件定位：`vswhere`（VS/MSVC/SDK）、`where` / `Get-Command`（PATH 内工具），多实例时按版本校验后择优；
  4. Hermes 自身输出：`hermes --version`、`hermes doctor`、配置文件（数据根、MCP 路径）。
- 校验：解析结果必须通过「存在 + 可执行 + 版本匹配」三重校验，任一失败即报错并进入修复流程，不得静默回退默认值。
- 集中收口：全部路径解析走单一入口 `Core/PathResolver.cs`（见附录 E），禁止在业务代码里散落盘符拼接。
- 验收：AC-31。

### 4.3 FR-3 标准环境与依赖版本

**FR-3.1 版本矩阵与来源（P0）**

| 组件 | 要求 | 来源 | 校验方式 |
|------|------|------|----------|
| Windows | 10 1809（build 17763）及以上 / 11 | 官方安装文档 | 注册表 `CurrentBuildNumber` |
| 架构 | x64（ARM64 允许但 Warn） | 官方安装文档 | `PROCESSOR_ARCHITECTURE` |
| Python | 首选 3.14；工程约束 `>=3.11,<3.15` | 官方安装文档 + 仓库 `pyproject.toml` | `python -V` |
| Node.js | ≥ 20（实际由 PM 锁提供，本机观测 26.7.0 / 26.12.1） | 官方安装文档 + `pm/lock.json` | `node -v` |
| npm / ripgrep / FFmpeg | 由 PM 锁文件锁定并校验 sha256 | `pm/lock.json`（E18） | PM 自检命令 |
| Git | 建议 ≥ 2.30（官方脚本可自带 pinned Git for Windows） | 官方安装文档 | `git --version` |
| 原生构建（仅桌面端源码构建） | MSVC + Windows SDK | 官方 Windows 原生文档 | `vswhere` + `cl.exe` |
| 桌面端 MSIX 形态 | Windows 11 22H2+ | 官方 Windows 原生文档 | build ≥ 22621 |

> 抓取日期 2026-10-24。来源：`https://hermes-agent.nousresearch.com/docs/getting-started/installation`、`https://hermes-agent.nousresearch.com/docs/user-guide/windows-native`。
> **待确认**：文档与 `pm/lock.json` 可能漂移，具体版本以 lock 为准；确认途径——每次发版前比对 docs 与 lock 并记录差异。
- 验收：AC-33（矩阵逐项在 UI 显示「要求 / 实测 / 结论」）。

**FR-3.2 安装前版本校验与修复路径（P0）**
- 校验顺序：Python → Node/npm → Git → 构建工具链 → 汇总结论。
- 修复策略：优先使用 Hermes 自带 PM（锁文件固定版本 + sha256）；不可用时才走系统包管理器；**禁止自动升级到未锁定的大版本**。
- 降级策略：检测到 Python ≥3.15 或 Node 主版本超范围时，提示使用 PM 的 pinned 版本，不擅自降级系统全局版本。
- 构建工具链缺失：**不静默跳过**。按 D-02，云正事必须保留桌面端源码构建能力，因此缺失时进入 FR-3.4 修复流程（自动加装 VC++ 工作负载）；仅当用户显式选择「本次跳过桌面端构建」时才降级，且结论必须标为降级。
- 验收：AC-25。

**FR-3.3 修复后自检（P0）**

| 自检项 | 命令 | 期望输出 |
|--------|------|----------|
| CLI 版本 | `hermes --version` | 与目标通道最新版本一致（含 commit 前缀） |
| 环境体检 | `hermes doctor` | 无 Fail 项，Warn 项可解释 |
| 工具链 | PM 自检命令 | pinned 组件版本与 `pm/lock.json` 一致，sha256 通过 |
| Python | `python -V` | `Python 3.1x.y`（x 在 1–4 之间） |
| Node | `node -v` | `v20+`，且与 PM 锁版本一致 |
| 桌面端产物 | 检查 `%LOCALAPPDATA%\hermes\apps\desktop\release\win-unpacked\Hermes.exe` | 存在且可执行（仅当包含桌面端） |
| 服务 | 网关状态查询 | 与升级前一致 |

- 验收：AC-26（自检输出原样落日志并进入诊断包）。

**FR-3.4 构建工具链检测与修复（P0，D-02 决策后新增）**
- 背景：云正事已装 VS2026 却仍因缺 VC++ 工具集编译失败 —— 说明「装了 VS」不等于「工具链可用」，且该机必须保留桌面端源码构建能力，因此不能靠跳过降级收场。
- 检测（三件套，全部动态取路径，禁止硬编码盘符）：
  1. `vswhere -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`（有输出即存在 MSVC 工作负载）；
  2. `VC\Tools\MSVC\*\bin\Hostx64\x64\cl.exe` 是否实际存在；
  3. Windows SDK `10.0.*` 目录是否存在。
- 修复路径（按优先级，任一成功即停）：
  1. **已有 VS 实例**：`vs_installer.exe modify --installPath "<vswhere 返回路径>" --add Microsoft.VisualStudio.Workload.VCTools --includeRecommended --quiet --norestart`；
  2. **无 VS 实例**：`winget install --id Microsoft.VisualStudio.2022.BuildTools --override "--add Microsoft.VisualStudio.Workload.VCTools --includeRecommended --quiet"`；
  3. 以上均不可用（无 winget / 无网络 / 无管理员）：明确报失败并给出可复制的分步指引，**不得静默降级为「跳过桌面端构建」**。
- 前置条件：需要管理员权限（沿用现有 `--fix-hosts` 自提权模式另起自身进程执行）；可用磁盘 ≥ 6 GB；预计耗时 10–25 分钟，须在 UI 显示进度并允许取消（取消即视为失败，不做半装）。
- 提权与确认（D-03）：执行 `vs_installer` / `winget` 前必须弹出确认框，展示**将执行的确切命令行、影响范围（安装 VC++ 工作负载，约 2–3 GB）、预计耗时、是否需要重启**；用户取消则不做任何系统变更。命令与执行结果全文入日志。
- 修复后自检：`vswhere -property installationVersion` 有输出 + `cl.exe` 可执行 + `node-gyp` 探测通过；三项全绿才允许进入 `desktop-build` 阶段。
- 重试策略：修复成功后可重试编译 1 次；再次失败按编译类错误处理，不循环重试，且结论不得为「成功」。
- 验收：AC-29。

### 4.4 FR-4 下载源策略（多级回退）

**FR-4.1 分级定义（P0）**

| 级别 | 类型 | 覆盖对象 | 具体端点（示例） | 默认 |
|------|------|----------|------------------|------|
| L0 | 官方直连 | 全部 | `github.com`、`raw.githubusercontent.com`、`api.github.com`、`registry.npmjs.org`、`files.pythonhosted.org`、`nodejs.org/dist`、`astral.sh` | **首选** |
| L1 | 官方 CDN / 受控加速域名 | git 源码、raw 文件、Electron 与 electron-builder 二进制、Node headers | `gh-proxy.com`、`ghproxy.net`、`ghfast.top`；`cnb.cool` 社区镜像仓；`npmmirror.com/mirrors/electron/`、`…/electron-builder-binaries/`、`…/node/`；`cdn.playwright.dev` | **默认启用**（D-01：探测后使用） |
| L2 | 国内公共包镜像 | npm / PyPI / Chromium 快照 | npm：`registry.npmmirror.com`（备 `mirrors.aliyun.com/npm`）；PyPI：`pypi.tuna.tsinghua.edu.cn/simple`（备 `mirrors.aliyun.com/pypi/simple`、`mirrors.cloud.tencent.com/pypi/simple`、`mirrors.huaweicloud.com/repository/pypi/simple`）；Chromium：`npmmirror.com/mirrors/chromium-browser-snapshots/` | 仅 L1 失败时 |
| L3 | 私人 SOCKS5 隧道 | 全部（作为出口代理） | `socks5h://127.0.0.1:1080` → `root@<SSH_HOST>` | **最后一级，默认关闭** |

> **L1 风险与决策（D-01）**：第三方加速器属公共中转，链路不可信。用户已确认「可以尝试」，故 L1 默认参与调度（仅在 L0 探测失败/不稳定时启用），但下载物**必须**按 `pm/lock.json` 的 sha256 / npm integrity 校验；校验失败即判该级不可用，记录来源域名并切下一级（防投毒）。
> **L2 适用范围**：只代理包管理器层流量；git 仓库与裸文件下载无法代理，须走 L1 仓库镜像或 L3。

**FR-4.2 切换判据（P0）**

| 判定 | 具体阈值 |
|------|----------|
| 本级失败 | 同级连续 2 次探测失败（TCP 3s / TLS 5s 超时、连接重置） |
| 本级不稳定 | 连续 10s 平均速率 < 100 KB/s 且剩余 > 5 MB；或同级 3 次重试中 2 次超阈值 |
| 单次配额 | 每级最多 3 次尝试，退避 1s / 2s / 4s |
| 冷却 | 降级后 300s 内不回试上级 |
| 回试 | 冷却结束且上级探测恢复，本次任务内可回上级一次 |
| L3 准入 | L0–L2 全不可用，或用户显式启用；且隧道健康检查通过 |

- 验收：AC-34。

**FR-4.3 切换日志要求（P0）**
```
[NET] level=L1->L2 reason=timeout op=uv-sync target=pypi.tuna.tsinghua.edu.cn attempt=2 elapsed=45.2s rate=0KB/s action=fallback
[NET] level=L2 probe=ok latency=380ms rate=1.8MB/s action=use
```
- 验收：AC-27。

**FR-4.4 完整性校验（P0）**
- git 源码校验 commit / tag；raw 与 ZIP 校验长度 + sha256；Python 按 `uv.lock` hash；npm 按 `integrity`。
- 校验失败：删除产物、标记该级不可用、切下一级，并记录期望值与实际值。
- 验收：AC-28。

**FR-4.5 私人代理接入方式（P0）**
- 隧道：`ssh -D 1080 -N -o ServerAliveInterval=30 -o ServerAliveCountMax=3 -o ExitOnForwardFailure=yes -o ConnectTimeout=10 root@<SSH_HOST>`
- 类型：SOCKS5，`127.0.0.1:1080`。
- 凭据：仅从 Windows 凭据管理器或环境变量读取，**不写配置文件、不出现在命令行参数、不写日志**。
- scheme 规范化：优先 `socks5h`（远端 DNS）；实测有客户端报 `unsupported scheme socks5h`，此时回落 `socks5`（本地 DNS）。git 侧 `http.proxy=socks5h://127.0.0.1:1080`，环境侧同时设 `HTTP_PROXY/HTTPS_PROXY/ALL_PROXY`。

**FR-4.6 隧道健康检查与自动重建（P0）**
- 健康检查：SOCKS5 握手（3s）+ `CONNECT github.com:443`（3s），连续 3 次成功才健康。
- 重建：每 15s 检查；不健康则重启隧道，退避 2s / 5s / 10s / 30s，最多 5 次；仍失败标记 L3 不可用并**停止重建**，UI 提示手动处理。
- 风险明示：依赖单台 SSH 主机与家宽上行，可能随时中断或被限速；只用于公共源全挂的最后兜底。

**FR-4.7 默认值与开关（P1）**
- L3 默认关闭，设置项 `allowPrivateProxy`（默认 false），与现有 `allowHostsFix` 同级。
- L1 加速域名默认「探测后使用」，不做无条件替换。
- 每级启用状态与最近探测结果写入配置并在 UI 可见。
- 验收：AC-35。

---

## 5. 非功能需求

| 编号 | 类别 | 要求 | 验证方法 |
|------|------|------|----------|
| NFR-01 | 性能 | CLI 升级 ≤ 25 min、桌面端 ≤ 40 min（基线 47m30s / 57m27s） | 两机各跑 3 次计时 |
| NFR-02 | 性能 | 无网络请求超过 FR-2.8 上限；超预算主动中止 | 故障注入（阻断、限速） |
| NFR-03 | 可靠性 | 任一阶段失败可回滚；连续重跑 3 次结果一致 | 断网/断电模拟 |
| NFR-04 | 幂等性 | 「已是最新」时零副作用（不改配置、不写 hosts、不重建依赖） | 前后文件哈希对比 |
| NFR-05 | 跨机器 | 不同盘符、不同用户名、含中文与空格路径行为一致 | 4 组路径矩阵 |
| NFR-06 | 安全 | 全文与日志无明文密码/token；凭据只从凭据管理器或环境变量取 | 诊断包扫描 + 日志正则审计 |
| NFR-07 | 安全 | 系统级修改（hosts、ACL）可还原且默认关闭 | 代码审查 + 还原演练 |
| NFR-08 | 可观测 | 每次级别切换、重试、超时都有结构化日志并可定位到阶段与端点 | 日志抽样评审 |
| NFR-09 | 可维护 | 业务逻辑在 `Core/`，`MainForm.cs` 只做编排展示（已 1311 行，禁止继续膨胀） | 代码审查 |
| NFR-10 | 兼容 | net48 + Win10 1809/11，单文件免安装，不引入第三方 NuGet | `build.ps1` 构建 + 双机启动 |
| NFR-11 | 兼容 | 沿用现有 C# 5/6/7 风格（`string.Format`，不用插值/`?.`/`nameof`） | 代码审查 |
| NFR-12 | 可测试 | 每阶段支持 dry-run 与故障注入开关，可离线验证 | 单机自测脚本 |
| NFR-13 | 可构建性 | 缺工具链的机器必须能经 FR-3.4 修复流程完成桌面端源码构建，「跳过构建」不得成为默认结局（D-02） | 云正事实机复现 |
| NFR-14 | 可移植性 | 源码与配置中不出现盘符、用户名、版本号目录字面量；CI 静态检查可拦截（`[A-Za-z]:\\`、`\\Users\\`、`\d+\.\d+\.\d+` 目录） | 静态扫描 + 代码评审 |

---

## 6. 验收标准

| 编号 | 对应需求 | 可测断言 | 验证手段 |
|------|----------|----------|----------|
| AC-01 | FR-1.1 | 依赖安装被人为阻断时，结论必须为「降级/失败」 | 断网 + 指定 wheel 不可达 |
| AC-02 | FR-1.1 | 结论页展示 4 项自检结果与期望值对照 | 截图 + 日志 |
| AC-03 | FR-1.2 | 已是最新时重跑，依赖重装次数 = 0，全程 ≤ 3 min | 日志计数 |
| AC-04 | FR-1.3 | `uv` 源指向黑洞地址，900s 内必须中止并切级 | 故障注入计时 |
| AC-05 | FR-1.3 | node-gyp 失败不触发网络级重试，日志无同类第二遍编译 | 日志核对 |
| AC-06 | FR-1.4 | 同一 wheel 一次任务只下载 1 次 | 抓包/日志 |
| AC-07 | FR-1.5 | git 依赖请求命中镜像前缀且 commit 校验通过 | 日志 URL 与哈希 |
| AC-08 | FR-1.6 | 双 home 时 UI 列出两套进程/根/版本，未选中一套状态不变 | 双 home 实测 |
| AC-09 | FR-1.7 | 升级前网关运行、中途被停时，结束必须恢复；否则结论为降级 | 进程前后对比 |
| AC-10 | FR-1.8 | 无权限错误时 ACL 不执行；有时单次 ≤ 120s | 日志计时 |
| AC-11 | FR-1.9 | 开启 Defender 时 `EPERM` 场景重试 1 次后成功或给出排除指引 | 复现测试 |
| AC-12 | FR-1.10 | 任务结束 `tools` 只保留当前与上一版，占用下降 ≥ 30%（基线 2.1 GB） | 目录大小对比 |
| AC-13 | FR-1.11 | `PYTHONPATH` 放同名模块，工具告警且子进程不受影响 | 注入测试 |
| AC-14 | FR-1.12 | 存在未知 toolset 时结论给出条目与修复建议 | 构造配置 |
| AC-15 | FR-1.13 | 429 按 `Retry-After` 退避且 ≤ 3 次；release 历史失败不阻塞 | Mock 服务 |
| AC-16 | FR-1.14 | 升级后 MCP 配置 node 路径指向当前运行时且文件存在 | 配置读取 |
| AC-17 | FR-2.1 | 磁盘 <3 GB 阻断；目录只读阻断；中文/空格路径仅 Warn 且完成 | 逐项构造 |
| AC-18 | FR-2.2 | C:/D: 两种盘符与含中文空格路径各完成一次升级 | 4 组矩阵 |
| AC-19 | FR-2.3 | `deps-node` 阶段强杀后重跑，前两阶段被跳过 | 状态机日志 |
| AC-20 | FR-2.4 | 构建阶段人为失败，回滚后 HEAD 与升级前一致、依赖按锁复原 | 失败注入 + 比对 |
| AC-21 | FR-2.5 | 连续点升级 3 次只有 1 实例执行，其余提示可查看进度 | 手动操作 |
| AC-22 | FR-2.6 | 残留目录被占用时进待清理清单，下次启动清理成功 | 占用复现 |
| AC-23 | FR-2.7 | 诊断包 ≤ 20 MB，含 8 类内容，扫描无明文凭据 | 产物检查 |
| AC-24 | FR-2.8 | 9 类操作逐项注入超时，实际中止时间与上限偏差 ≤ 10% | 自动化注入 |
| AC-25 | FR-3.2 | Python 3.15 下提示使用 PM pinned 版本而非直接失败；Node 主版本超范围同样走 pinned | 环境构造 |
| AC-26 | FR-3.3 | 7 项自检全部执行，输出格式一致并进入日志与诊断包 | 日志核对 |
| AC-27 | FR-4.3 | 每次切级产生 1 条 `[NET]` 日志，含原因/端点/耗时/速率 | 日志解析脚本 |
| AC-28 | FR-4.1/4.4 | L1 返回被篡改产物时 sha256 校验失败、该级禁用并切 L2 | 中间人模拟 |
| AC-29 | FR-3.4 | 在缺 VC++ 的云正事上执行修复流程后 `cl.exe` 可用、`desktop-build` 成功产出 `Hermes.exe` | 云正事实机 |
| AC-30 | FR-4.1 | L1 默认参与调度（L0 失败即启用），每次使用记录来源域名与校验结果 | 日志核对 |
| AC-31 | FR-2.9 | 在「数据根在 D 盘 + 用户名含中文 + 桌面被 OneDrive 重定向 + 存在两个 VS 实例」矩阵下全部路径解析成功；人为删除解析目标时显式报错且**不**回落到默认路径 | 4 组环境构造 |
| AC-32 | FR-3.4 / D-03 | 提权修复前弹出确认，展示确切命令/影响/耗时；取消后系统零变更（前后快照一致） | 手动 + 快照对比 |
| AC-33 | FR-3.1 | 版本矩阵每项在 UI 显示「要求 / 实测 / 结论」；构造 Python 3.15、Node 18、build 17134 三种不合规环境均被判 Fail | 环境构造 |
| AC-34 | FR-4.2 | 首包 120s 无数据须在 90s 阈值内判「不稳定」并切 L1；同级连续 2 次探测失败判该级不可用；300s 冷却期内不回试上级 | 故障注入 |
| AC-35 | FR-4.7 | 默认配置下 L3 关闭、L1 为「探测后使用」；UI 可见每级启用状态与最近探测结果 | 配置检查 + 截图 |

---

## 7. 里程碑与优先级

| 里程碑 | 范围 | 关键产出 | 预估 | 依赖 |
|--------|------|----------|------|------|
| M1（P0 核心） | FR-1.1 / 1.2 / 1.3 / 1.6 / 1.7、FR-2.5、FR-2.8 | 结论可信、消除双跑、分级超时、多 home、幂等锁 | 1.5 周 | 无 |
| M2（P0 网络） | FR-1.4 / 1.5、FR-4.1–4.4 | 四级源编排 + 切换判据 + 完整性校验 + `[NET]` 日志 | 1.5 周 | M1 探测框架 |
| M3（P0 兜底） | FR-2.1–2.4、FR-2.7 | 前置门禁、状态机、断点续装、回滚、诊断包 | 1.5 周 | M1 |
| M4（P1 环境） | FR-3.1–3.4、FR-1.8–1.12 | 版本矩阵、修复路径、**构建工具链修复**、自检表、ACL 按需、GC、遮蔽检测 | 1.5 周 | M3 |
| M5（P1/P2 收尾） | FR-4.5–4.7、FR-1.13、FR-1.14 | 代理守护、限流降级、运行时路径回写 | 1 周 | M2 |

优先做 M1 + M2：直接消除「假成功」与 30 分钟空转，投入产出比最高。

---

## 8. 风险与开放问题

### 8.1 风险

| 风险 | 概率 | 影响 | 应对 |
|------|------|------|------|
| 第三方加速器（L1）链路不可信 | 中 | 高（供应链） | D-01 已决策可尝试：强制 sha256/integrity 校验，失败即禁用该级并记录来源域名 |
| 私人代理不稳定、被限速或封禁 | 高 | 中 | 仅 L3、默认关闭、重建有上限、失败不阻塞已完成阶段 |
| 官方文档与 `pm/lock.json` 版本漂移 | 中 | 中 | 以 lock 为唯一真值，发版前比对并记录差异 |
| 自动回滚本身改坏可用环境 | 低 | 高 | 回滚前二次快照；回滚后必跑自检，失败则停「只读诊断」模式 |
| 杀软误报本工具（同类 `uv.exe` 问题） | 中 | 中 | 不自行改杀软设置，只给排除指引与签名说明 |
| 过度自动化导致用户不知情 | 中 | 中 | 危险动作默认关闭；每次系统级修改前后留可还原记录 |
| net48 + 老语法限制实现方式 | 低 | 低 | 沿用现有风格，不引入新语法糖与第三方库 |

### 8.2 决策记录（已关闭）

| 编号 | 决策问题 | 结论 | 决策人 / 日期 | 影响的需求 |
|------|----------|------|----------------|------------|
| D-01 | 是否允许把第三方 GitHub 加速器作为 L1？ | **可以尝试**：L1 默认参与调度，仅在 L0 探测失败/不稳定时启用；每次使用强制 sha256 / npm integrity 校验，校验失败即禁用该级并记录来源域名 | 用户 / 2026-10-24 | FR-4.1、FR-4.2、FR-4.4、AC-28、AC-30 |
| D-02 | 云正事是否需要桌面端源码构建？ | **需要保留**：缺工具链不得跳过降级，必须进入 FR-3.4 修复流程，修复后自检通过再编译 | 用户 / 2026-10-24 | FR-1.3、FR-2.1、FR-2.8、FR-3.2、NFR-13、AC-29 |
| D-03 | 是否允许工具自动执行需提权的修复动作（如加装 VC++ 工作负载）？ | **先纳入**：允许，但必须先弹确认，展示确切命令行、影响范围、预计耗时与是否重启；用户取消即不执行、不留任何系统变更。全部提权动作沿用 `--fix-hosts` 自提权模式，命令与结果全文入日志 | 用户 / 2026-10-24 | FR-3.4、AC-32 |

### 8.3 开放问题（待确认）

| 编号 | 问题 | 影响 | 确认途径 |
|------|------|------|----------|
| Q3 | 私人 SOCKS5 的实际吞吐与可用时段 | L3 能否承担大文件 | 连续 3 天探测并记录速率 |
| Q5 | 官方推荐版本以 docs 还是 `pm/lock.json` 为准 | FR-3.1 版本矩阵 | 发版前比对，冲突以 lock 为准并提 issue |
| Q6 | 是否需要 CI 中的双机矩阵自动化验证 | AC-18 执行成本 | 评估是否增加机器/虚拟机 |

---

## 9. 需求追踪矩阵（S2 准出必备）

### 9.1 本轮范围（D1 = A：M1 核心）

本轮**只做 M1 的 7 条 FR**：FR-1.1 / 1.2 / 1.3 / 1.6 / 1.7 / FR-2.5 / FR-2.8。依据：用户 2026-10-24 拍板 D1=A；PRD §7 亦写明「优先做 M1 + M2：直接消除『假成功』与 30 分钟空转，投入产出比最高」。

其余 27 条 FR（M2~M5）不在本轮，留待后续版本；§8.3 的 Q3 / Q5 / Q6 三条开放问题**均落在 M4/M5**（Q5→FR-3.1、Q3→L3 代理、Q6→AC-18 双机成本），故带其推进 M1，到 M4/M5 时再逐个关闭。

### 9.2 追踪矩阵

> 现状锚点为 2026-10-24 逐文件实测所得（口径：`splitlines`，14 个 .cs / 3,996 行）。**S3 已回填**（2026-10-24）：HLD / DD / 任务卡 / 验证项四栏由 `doc/HLD_Hermes_Agent_Windows_升级工具_v1.0.md` 与 `doc/DD_Hermes_Agent_Windows_升级工具_v1.0.md` 映射完成，7 条 FR + 5 条 NFR 已由 S6 独立验收回填为「已验证 / 部分验证」，详见 `doc/交付验证报告_Hermes_升级工具_M1_v1.0.md`。

| 需求编号 | 需求摘要 | 现状锚点（文件:行号，实测） | 验收 | HLD | DD | 任务卡 | 验证项 | 状态 |
|---------|---------|---------------------------|------|-----|----|--------|--------|------|
| FR-1.1 | 结论可信化：结束前执行 4 项断言，任一不过不得报成功 | `InstallerEngine.cs:287`（唯一判据是 `result.ExitCode != 0` 的否定式）→ `:317-318` 无条件 `_logger.Success("升级完成。")` + `OperationResult.Ok`；`:85-86` 安装同样；`:248-250` 阶段成败只取 `result.ExitCode == 0`；`:312` 以 `HermesPaths.DesktopBuilt`（`HermesPaths.cs:70-73`，仅 `File.Exists`）判「桌面端已随更新重建」，**不校验是否本次产出** | AC-01、AC-02 | HLD §2.4 G2+G8 | DD §2.2 G2（A1–A4）+G8 改造点 7/8 | T-HU-DEV-2 | AC-02 四项断言逐项 | 已验证 |
| FR-1.2 | 消除重复执行：版本指纹三元组判等，相等即跳过 | `MainForm.cs:863`（`RunUpgradeAsync(false,)`）与 `:866`（`RunUpgradeAsync(true,)`）分发；`InstallerEngine.cs:273` 执行单次 `hermes update --yes`；`:295-308` 仅当 `DesktopBuilt` 为假才二次 `desktop --build-only --force-build`；**全项目无版本指纹判等代码**（无 `git rev-parse HEAD` + lock sha256 + CLI 版本的组合判断） | AC-03 | HLD §2.4 G3+G8 | DD §2.2 G3（含口径修正 D-1） | T-HU-DEV-4 | AC-03 **部分**（见 DD §7 D-1） | 部分验证（AC-03，D-1 口径） |
| FR-1.3 | 分级超时与无进展检测 | `ProcessRunner.cs:83-193`（`RunProcessAsync` 全程**无任何超时**）；`:172` 裸 `process.WaitForExit()`（无参=无限等待）；`:164-168` 唯一中断途径是外部 `CancellationToken`；`:195-202` `Emit` 只转发行、不记时间戳 → **无「无进展」判定能力** | AC-04、AC-05 | HLD §2.4 G1+G7 | DD §2.2 G1 参数表 + G7 轮询循环 | T-HU-DEV-1 | AC-04 **部分**（中止✅/切级→M2）、AC-05 | 部分验证（AC-04 中止✅/切级→M2） |
| FR-1.6 | 多数据根识别：枚举全部根并列进程-归属-动作 | `HermesPaths.cs:8-21` `HermesHome` **只返回单一根**（`HERMES_HOME` 优先，否则 `%LOCALAPPDATA%\hermes`）；**全项目无 `%USERPROFILE%\.hermes` 枚举**；`InstallerEngine.cs:268-269` 进程清理按单根路径前缀匹配 | AC-08 | HLD §2.4 G4+G9 | DD §2.2 G4 + §0 双根实测 | T-HU-DEV-5 | AC-08 本机双根实测 | 已验证 |
| FR-1.7 | 服务状态复原：升级前在跑而当前未跑则恢复 | `InstallerEngine.cs:268-269` 升级前只 `StopHermesProcessesAsync`；`RunUpgradeAsync`（`:256-334`）**全文无任何恢复调用**；`EnvironmentProbe.cs:318` 是全项目唯一涉及 gateway 的代码，且仅探测进程名（`Hermes`/`hermes`/`hermes-gateway`），不做启停 | AC-09 | HLD §2.4 G5（含 gateway 实测） | DD §2.2 G5（ensure→start→降级） | T-HU-DEV-3 | AC-09 gateway stop 后复原 | 部分验证（AC-09 端到端未测，R-13） |
| FR-2.5 | 幂等性：已是最新时零副作用；锁存在时显示另一实例进度 | `Program.cs:40` `Mutex(true, "HermesInstaller.SingleInstance", out createdNew)`——**单实例锁已具备**；缺两块：①「已是最新则只自检退出」的判定（与 FR-1.2 同源指纹）；②锁被占时 UI 显示「另一实例正在执行（阶段 X）」 | AC-21 | HLD §2.4 G6+G10 | DD §2.2 G6 + G10（Program.cs 分支） | T-HU-DEV-4 | AC-21 连续启动 3 次 | 已验证 |
| FR-2.8 | 超时与重试参数表落地（9 类操作） | `ProcessRunner.cs` **无任何超时参数**（与 FR-1.3 同源）；`InstallerEngine.cs:115` `HttpClient{Timeout = 90s}`（下载脚本）；`NetworkProbe.cs:110-111` git 22s / http 12s、`:151` 12s；`EnvironmentProbe.cs:404,423` `RunCapture(file,args,int timeoutMs)` + `WaitForExit(timeoutMs)`——**探针自带超时，全项目唯一例外** | AC-24 | HLD §2.4 G1 | DD §2.2 G1 九类参数表（冻结） | T-HU-DEV-1 | AC-24 **部分**（仅 3 类做注入实测） | 部分验证（AC-24 三类注入） |
| NFR-01 | 性能：CLI ≤ 25 min、桌面端 ≤ 40 min（基线 47m30s / 57m27s） | 无计时与预算机制，超预算不中止 | AC-24 | HLD §1.1 | DD §2.2 `BudgetFor`（1500s/2400s） | T-HU-DEV-1 | 预算中止日志 | 部分验证（预算到位，未长跑计时） |
| NFR-04 | 幂等：「已是最新」时零副作用 | 同 FR-2.5 缺口 | AC-21 | HLD §2.4 G3+G6 | DD §2.2 G3 `IsSame` 语义 | T-HU-DEV-4 | 重复执行前后文件哈希 | 已验证 |
| NFR-08 | 可观测：级别切换/重试/超时均有结构化日志 | `Logger.cs` 有 `Info/Warn/Error/Success/Stage` 五级，但**无 `[NET]` 结构化行、无重试/超时专用级别** | AC-27 | HLD §1.3 P3 | DD §2.2 `[TIMEOUT]` 行格式（冻结） | T-HU-DEV-1 | 日志解析脚本 | 已验证 |
| NFR-09 | 可维护：业务逻辑在 `Core/`，`MainForm.cs` 不得继续膨胀 | `MainForm.cs` 实测 **1,310 行**（50 field + 39 method），已占全项目 3,996 行的 32.8% | 代码审查 | HLD §1.2 C3 | DD §2.2 G10 行数预算（净增 ≤45）+ `Core/UiTextBuilder.cs` | T-HU-DEV-2/4/5 | `wc -l` 卡 ≤1355 | 已验证（1345 行，+35） |
| NFR-11 | 兼容：沿用 C# 5/6/7 风格（`string.Format`，不用插值/`?.`/`nameof`） | 实测插值 0 处、`?.` 0 处、`nameof(` 0 处 | 代码审查 | HLD §1.2 C2 | DD §9 红线第 11 条 | 全部卡 | grep 三项均为 0 | 已验证 |

### 9.3 本轮不包含（Out of Scope）

FR-1.4 / 1.5 / 1.8~1.14、FR-2.1~2.4 / 2.6 / 2.7 / 2.9、FR-3.1~3.4、FR-4.1~4.7 —— 共 27 条，分属 M2（四级源编排）、M3（门禁/状态机/回滚/诊断包）、M4（版本矩阵/工具链修复）、M5（代理/限流/运行时路径）。

### 9.4 首卡建议（D2 = A 的落点）

D2 拍板「先用第 1 张 DEV 卡跑通 S1→S7 全链路，再铺开」，建议首卡选 **FR-1.3（分级超时与无进展检测）**：

| 理由 | 说明 |
|---|---|
| 文件边界最清晰 | 主体落在 `ProcessRunner.cs`（249 行，仅被 `InstallerEngine` 一个文件依赖——AGENTS.md §3 口径），调用点集中在 `InstallerEngine` 两处 |
| 零系统副作用 | 不碰 hosts / ACL / 服务 / 注册表，失败可回滚（纯代码） |
| 可独立验收 | `build.ps1` 0 警告 0 错误 + 空转场景可复现（子进程指向黑洞端口，须在阈值内被 `KillTree` 中止）——满足 AC-04 且无需真机 30 分钟等待 |
| 是 FR-2.8 的地基 | 参数表（连接/无进展/总超时/重试/退避）需要有超时载体才能落地 |

它同时受 NFR-09 约束：新增能力必须进 `Core/`（预计新增 `Core/RetryPolicy.cs`，见附录 E），**不得继续塞进 `MainForm.cs`**。

## 10. 评审记录

| 日期 | 参会人 | 结论 | 遗留问题 | 关联纪要 |
|------|--------|------|----------|----------|
| 2026-10-24 | 芦哥（需求方）、Hermes | **通过**（D5）——S1 澄清已由 D1/D2/D3/D4 拍板关闭；本轮范围 M1 七条 FR；S3 准入 | 无（3 条 PRD 自带开放问题均不在 M1） | 本表即决议记录 |
| 2026-10-24 | 芦哥（需求方）、Hermes | **HLD/DD 已产出，待评审** | 设计层 5 条偏差 D-1~D-5 需确认（D-1 指纹口径修正、D-2 换源属 M2、D-3 gateway ensure、D-4 A2 断言具体化、D-5 MainForm 净增 ≤45 行） | `doc/HLD_…_v1.0.md`、`doc/DD_…_v1.0.md` |

> 评审已通过（2026-10-24）。S3 启动条件已满足（制度层 §一 S3 准入条件：PRD 评审通过）。同日拍板 D6=PRD v1.5 先入库、D7=首卡 FR-1.3（+FR-2.8）。

---

## 附录 A：超时与重试参数速查

见 FR-2.8 表。实现建议集中在新增 `Core/RetryPolicy.cs` 与 `Core/NetworkPlanner.cs`，避免散落在各调用点。

## 附录 B：网络源清单与适用范围

| 用途 | L0 官方 | L1 受控加速 | L2 公共镜像 |
|------|---------|-------------|-------------|
| git 源码 | `github.com` | `gh-proxy.com` / `ghproxy.net` / `ghfast.top` / `cnb.cool` 镜像仓 | 不适用 |
| raw 文件 | `raw.githubusercontent.com` | 同上 | 不适用 |
| npm 包 | `registry.npmjs.org` | — | `registry.npmmirror.com`、`mirrors.aliyun.com/npm` |
| PyPI 包 | `files.pythonhosted.org` | — | 清华 TUNA、阿里云、腾讯云、华为云 |
| Electron / electron-builder 二进制 | `github.com` releases | `npmmirror.com/mirrors/electron/`、`…/electron-builder-binaries/` | — |
| Node headers / dist | `nodejs.org/dist` | `npmmirror.com/mirrors/node/` | — |
| Chromium 快照 / Playwright | `cdn.playwright.dev`、`storage.googleapis.com` | — | `npmmirror.com/mirrors/chromium-browser-snapshots/` |
| 兜底出口 | — | — | —（L3：`socks5h://127.0.0.1:1080`） |

## 附录 C：官方环境要求来源

- URL 1：`https://hermes-agent.nousresearch.com/docs/getting-started/installation`（抓取 2026-10-24）
- URL 2：`https://hermes-agent.nousresearch.com/docs/user-guide/windows-native`（抓取 2026-10-24）
- 关键结论：Python 3.14 为首选、工程约束 `>=3.11,<3.15`；Python/Node/npm/ripgrep/FFmpeg 由 PM 按 `pm/lock.json` 提供并校验 sha256；Windows 源码安装落 `%LOCALAPPDATA%\hermes` 且免管理员；MSIX 桌面端需 Win11 22H2+；桌面端源码构建需 MSVC + Windows SDK。
- 官网 URL 占位符：`<官网URL占位符>`（本文用上述两条实测可达地址代替，评审时确认是否替换为正式入口）。

## 附录 D：敏感信息与密钥管理

| 信息 | 存放位置 | 允许出现 | 禁止出现 |
|------|----------|----------|----------|
| SSH 主机 | 配置文件（`<SSH_HOST>` 占位） | 配置文件、UI | 日志、诊断包 |
| SSH 用户名/密码 | Windows 凭据管理器 / 环境变量 | 隧道进程内存 | 配置文件、命令行参数、日志、诊断包 |
| 代理用户名/密码 | 同上 | 同上 | 同上 |
| API key / token | 环境变量或凭据管理器 | 运行时内存 | 任何日志与文档 |

`Redactor` 新增规则：`socks5h?://[^@\s]+@`、`ssh\s+\S+@`、`(api[_-]?key|token|password|passwd)\s*[=:]\s*\S+`、`Bearer\s+\S+`。

## 附录 E：需求 → 代码模块落地映射

| 需求 | 落点 |
|------|------|
| FR-1.1 结论断言、FR-1.7 服务复原 | 新增 `Core/ResultVerifier.cs`，`InstallerEngine` 收尾调用 |
| FR-1.2 指纹判等、FR-2.3 状态机、FR-2.4 回滚 | 新增 `Core/StageState.cs`、`Core/Rollback.cs` |
| FR-1.3 / FR-2.8 超时重试 | 新增 `Core/RetryPolicy.cs`，改造 `ProcessRunner` |
| FR-1.4 / 1.5、FR-4.1–4.4 | 新增 `Core/NetworkPlanner.cs`，改造 `Mirrors` / `NetworkPreparer` / `NetworkProbe` |
| FR-1.6 多 home | 扩展 `HermesPaths`（枚举数据根）+ `InstallerEngine.StopHermesProcessesAsync` |
| FR-1.8–1.13 | 扩展 `EnvironmentProbe`（构建链、遮蔽、杀软）+ 新增 `Core/ConfigValidator.cs` |
| FR-1.14 运行时路径回写 | 新增 `Core/RuntimePathFixer.cs` |
| FR-2.7 诊断包 | 新增 `Core/DiagnosticsBundle.cs`（强制走 `Logger`/`Redactor`） |
| FR-4.5 / 4.6 代理 | 新增 `Core/ProxyTunnel.cs`（进程守护，凭据只读环境变量） |
| FR-3.x 版本矩阵 | 扩展 `EnvironmentProbe` + `Settings`（新增开关字段） |
| FR-3.4 构建工具链修复 | 新增 `Core/BuildTools.cs`（vswhere 探测 + vs_installer/winget 修复 + cl.exe 自检，路径全动态取） |
| FR-2.9 路径解析 | 新增 `Core/PathResolver.cs`（唯一收口：环境变量 + KnownFolder + `pm/lock.json` + vswhere/where + hermes 输出；禁止各调用点自行拼接） |

## 附录 F：日志证据索引

| 引用 | 来源（脱敏） | 关键内容 |
|------|--------------|----------|
| E1 | 本机 `CliUpgrade-20261024-001158.log` | `uv sync timed out after 1800 seconds`；时间线 00:11:58→00:59:28；同批 wheel 重复下载 |
| E2 | 本机 `CliUpgrade-…001158.log` | 末尾「升级完成」，但依赖解析失败、网关停止（`Paused gateway(s) left stopped`） |
| E3 | 本机 `DesktopUpgrade-20261024-070631.log` | 两段 `hermes update`（07:06:37→07:41:53、07:42:22→08:03:35），第二段 `Already up to date!` |
| E4 | 本机 `DesktopUpgrade-…070631.log` | `Elevated ACL repair skipped 32118 junction/symlink/hard-link entries`（07:22:16→07:41:29） |
| E5 | 本机 `update.log` | `Failed to connect to github.com port 443 via 127.0.0.1`、`over proxy 127.0.0.1` |
| E6 | 本机 `update.log` | `ZIP update failed: got only 15231214 out of 73151808 bytes` |
| E7 | 本机 `update.log` | `EPERM: operation not permitted, unlink '…@rolldown/binding-win32-x64-msvc.node'` |
| E8 | 本机 `update.log` | `cannot import name 'base_url_origin' from 'utils' (D:\tools\utils.py)` |
| E9 | 本机 `update.log` | `429`、`git fetch timed out after 300s`、`Missing prepared esbuild dependency in ui-tui`、`TypeError: fetch failed` |
| E10 | 本机 `update.log` | `.previous-python-3.14.7…\libcrypto-3-x64.dll [WinError 5]`、`dependency generation cleanup skipped` |
| E11 | 本机 `bootstrap-installer.log` | `io error during SOCKS handshake`、`unsupported scheme socks5h` |
| E12 | 本机 `errors.log` | MCP `command` 指向 `desktop-runtime\…0.20.0…\node\node.exe` 的 `FileNotFoundError` |
| E13 | 云正事 `CliUpgrade-20261024-073253.log` | node-gyp 两轮失败（07:33:26–07:36:36、07:48:26–07:51:31），缺 VC++ 工具集 |
| E14 | 云正事 `CliUpgrade-…073253.log` | `misaki` / `psutil` git 依赖 `github.com:443` 连接超时 21s ×3；hindsight 插件被禁用 |
| E15 | 云正事 `DesktopUpgrade-20261024-081127.log` | 两段 update（08:11:32→08:20:27、08:20:34→08:26:11）；ACL 101810 条目（08:16:17→08:20:21） |
| E16 | 云正事 `DesktopUpgrade-…081127.log` | gateway PID 16044 属 `%USERPROFILE%\.hermes`，被 `left alone` |
| E17 | 本机 `tools` 目录实测 | node 26.7.0 + 26.12.1、uv 0.12.3 + 0.13.5、python 3.14.7 + 3.14.9；`tools` 2.1 GB |
| E18 | 本机 `hermes-agent/pm/lock.json` | 每个组件含 `url` + `sha256`，上游为 `github.com` / `registry.npmjs.org` / `cdn.playwright.dev` |
