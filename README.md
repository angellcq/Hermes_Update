# Hermes 安装助手

一个面向 Windows 的 **Hermes Agent / Hermes 桌面端** 安装与升级工具。

- 目标用户：想在国内网络环境下稳定安装 Hermes 的朋友
- 形态：单文件 WinForms（.NET Framework 4.8，Windows 10/11 自带，无需安装运行时）
- 特点：**环境探针 + 镜像检测 + 一键新装/重装/升级 + 完整日志**

> 本项目是社区维护的**非官方工具**，与 Nous Research 无隶属关系。Hermes Agent 与 Electron 均以 MIT 许可证发布。

---

## 功能

| 模块 | 能力 |
|---|---|
| 环境探针 | Windows 版本、x64、PowerShell、磁盘、内存、长路径、.NET Framework、已有安装、运行进程、管理员、代理、杀软、系统时间 |
| 版本检查 | 读取 GitHub API 显示官方最新版本；下载官网最新 `install.ps1` |
| 镜像检测 | 官方安装源、GitHub、源码 git 镜像、Electron、electron-builder、npm、PyPI 的可用性与延迟 |
| Hermes CLI | 新装、修复重装、升级、卸载、启动 |
| Hermes 桌面端 | 新装、修复重装、升级、启动 |
| 日志 | 界面实时输出、自动脱敏、落盘保存、汇总 Hermes 自身日志 |
| 网络兜底 | 默认走镜像；全部失败且用户确认后，才尝试修复 hosts（提权 + 备份 + 可还原） |

---

## 系统要求

- Windows 10 1809+ 或 Windows 11，x64
- .NET Framework 4.8（Win10 1903+ / Win11 自带）
- PowerShell 5.1+
- 建议 8GB 内存、8GB 可用磁盘（完整安装实测约 5GB）
- 网络可访问 `hermes-agent.nousresearch.com`（官方安装脚本）

---

## 使用

1. 解压发布包，双击 `HermesInstaller.exe`（无需管理员）。
2. 启动后会自动做**环境检测**与**镜像检测**，结果在左侧两个标签页。
3. 右侧选择操作：
   - **Hermes CLI → 新装**：仅安装命令行版。
   - **Hermes 桌面端 → 新装**：安装 CLI 并构建 Electron 桌面端。
   - **修复重装**：重新运行官方安装器，保留配置、会话和密钥。
   - **升级**：停止运行中的 Hermes，执行 `hermes update --yes`；桌面端会随更新重建。
   - **卸载**：执行 `hermes uninstall --yes`（默认保留用户数据）。
4. 下方实时显示进度与日志。操作完成后会自动重新检测环境。

### 默认使用的镜像

| 用途 | 默认值 |
|---|---|
| 官方安装脚本 | `https://hermes-agent.nousresearch.com/install.ps1` |
| 源码仓库 | `https://ghproxy.net/https://github.com/NousResearch/hermes-agent.git`（自动选最快可用） |
| Electron | `https://npmmirror.com/mirrors/electron/` |
| electron-builder 工具 | `https://npmmirror.com/mirrors/electron-builder-binaries/` |
| npm | `https://registry.npmmirror.com` |
| PyPI | `https://pypi.tuna.tsinghua.edu.cn/simple` |

镜像结果可在「安装选项」中手动修改，点「应用镜像」保存。程序通过环境变量把这些镜像传给官方安装器：

- `HERMES_REPO_URL`
- `ELECTRON_MIRROR`
- `ELECTRON_BUILDER_BINARIES_MIRROR`
- `ELECTRON_BUILDER_CACHE`
- `npm_config_registry`
- `PIP_INDEX_URL` / `UV_DEFAULT_INDEX`

---

## 网络兜底（hosts）

程序**默认不修改系统文件**。只有同时满足以下条件才会提示修复 hosts：

1. 安装/升级因网络失败；
2. 镜像全部不可用；
3. 用户明确确认。

修复流程：

1. 备份 `C:\Windows\System32\drivers\etc\hosts` 到 `%LOCALAPPDATA%\HermesInstaller\hosts-backups\`
2. 写入 GitHub 可用 IP
3. 执行 `ipconfig /flushdns`
4. 提供「还原 hosts」按钮

> 建议优先使用代理/VPN；hosts 只是应急手段，IP 可能失效。

---

## 日志

- 工具日志：`%LOCALAPPDATA%\HermesInstaller\logs\<操作>-<时间>.log`
- Hermes 安装日志：`%LOCALAPPDATA%\hermes\logs\install.log`
- 界面右下角可「打开日志目录」「保存日志」「打开数据目录」
- 日志会自动脱敏（`api_key`、`token`、`sk-*`、`Bearer ...` 等）

---

## 构建

需要 .NET SDK（可构建 net48；本项目用 SDK 10 验证）：

```powershell
pwsh -File .\build\build.ps1
```

产物：

- `dist\HermesInstaller\HermesInstaller.exe`
- `dist\HermesInstaller-win-x64.zip`

---

## 常见问题

**Q：为什么安装桌面端这么慢？**
桌面端是 Electron 应用，需要下载约 158MB 的 Electron 运行时并打包，首次构建通常 10–20 分钟，取决于网络。

**Q：杀软报 `uv.exe` 有毒？**
Hermes 自带 Astral 的 `uv.exe`（Rust 写的 Python 包管理器），ML 杀软经常误报。请添加排除项，不要关闭杀软：
`Add-MpPreference -ExclusionPath "$env:LOCALAPPDATA\hermes\bin"`

**Q：升级时提示文件占用？**
先关闭 Hermes 桌面端和网关。工具会尝试自动停止 `%LOCALAPPDATA%\hermes` 下的进程。

**Q：安装后 `hermes` 命令找不到？**
重新打开终端；安装器会把 `%LOCALAPPDATA%\hermes\bin` 加入用户 PATH。

**Q：卸载会删除我的会话和密钥吗？**
默认不会。工具执行的是 `hermes uninstall --yes`（保留数据）。如需彻底删除，请手动运行 `hermes uninstall --full`。

---

## 许可证

本工具以 Apache License 2.0 发布，见 [LICENSE](LICENSE)。第三方组件见 [NOTICE](NOTICE)。

Hermes Agent: <https://github.com/NousResearch/hermes-agent>（MIT）
Electron: <https://github.com/electron/electron>（MIT）
