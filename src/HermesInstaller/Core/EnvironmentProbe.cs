using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Serialization.Json;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace HermesInstaller.Core
{
    /// <summary>
    /// Checks the machine against the requirements of the current official Hermes
    /// installer. Runs off the UI thread; never modifies the system.
    /// </summary>
    public sealed class EnvironmentProbe
    {
        private const int MinimumWindowsBuild = 17763; // Windows 10 1809
        private const double RecommendedFreeGb = 8.0;
        private const double MinimumFreeGb = 3.0;
        private const double RecommendedMemoryGb = 8.0;

        public Task<ProbeReport> RunAsync(Action<string, LogLevel> log, CancellationToken cancellationToken)
        {
            return Task.Run(() => Run(log, cancellationToken), cancellationToken);
        }

        private ProbeReport Run(Action<string, LogLevel> log, CancellationToken ct)
        {
            var report = new ProbeReport();
            Action<string> say = m => { if (log != null) log(m, LogLevel.Info); };

            say("开始环境检测…");

            // ---- OS ----
            try
            {
                int build = ReadInt(Registry.GetValue(
                    @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion",
                    "CurrentBuildNumber", null));
                string product = Convert.ToString(Registry.GetValue(
                    @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion",
                    "ProductName", "Windows"));
                string label = build >= 22000 ? "Windows 11" : product;
                bool ok = Environment.OSVersion.Platform == PlatformID.Win32NT && build >= MinimumWindowsBuild;
                report.Items.Add(new ProbeItem(
                    "系统", "Windows 版本",
                    "Windows 10 1809+ / Windows 11",
                    string.Format("{0} (build {1})", label, build),
                    ok ? ProbeStatus.Ok : ProbeStatus.Fail,
                    ok ? null : "请升级到 Windows 10 1809 或更高版本。"));
            }
            catch (Exception ex)
            {
                report.Items.Add(new ProbeItem("系统", "Windows 版本", "Windows 10/11", "读取失败: " + ex.Message, ProbeStatus.Fail));
            }

            ct.ThrowIfCancellationRequested();

            // ---- Architecture ----
            try
            {
                string arch = Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE") ?? "未知";
                bool is64 = Environment.Is64BitOperatingSystem;
                ProbeStatus status = !is64 ? ProbeStatus.Fail : (arch.Equals("ARM64", StringComparison.OrdinalIgnoreCase) ? ProbeStatus.Warn : ProbeStatus.Ok);
                report.Items.Add(new ProbeItem(
                    "系统", "处理器架构",
                    "x64",
                    arch + (is64 ? " (64 位)" : " (32 位)"),
                    status,
                    is64 ? (status == ProbeStatus.Warn ? "ARM64 依赖 x64 模拟，桌面端构建可能较慢。" : null) : "官方仅支持 64 位 Windows。"));
            }
            catch (Exception ex)
            {
                report.Items.Add(new ProbeItem("系统", "处理器架构", "x64", "读取失败: " + ex.Message, ProbeStatus.Warn));
            }

            ct.ThrowIfCancellationRequested();

            // ---- PowerShell ----
            try
            {
                string ps = RunCapture("powershell.exe", "-NoProfile -NonInteractive -Command \"$PSVersionTable.PSVersion.ToString()\"", 10000).Trim();
                Version version = null;
                Version.TryParse(ps, out version);
                bool ok = version != null && version >= new Version(5, 1);
                report.Items.Add(new ProbeItem(
                    "系统", "PowerShell",
                    ">= 5.1",
                    string.IsNullOrEmpty(ps) ? "未检测到" : ps,
                    ok ? ProbeStatus.Ok : ProbeStatus.Fail,
                    ok ? null : "请启用 Windows PowerShell 5.1（Windows 10/11 默认自带）。"));
            }
            catch (Exception ex)
            {
                report.Items.Add(new ProbeItem("系统", "PowerShell", ">= 5.1", "检测失败: " + ex.Message, ProbeStatus.Warn));
            }

            ct.ThrowIfCancellationRequested();

            // ---- Disk ----
            try
            {
                string root = HermesPaths.LocalAppDataRoot;
                var drive = new DriveInfo(root);
                double freeGb = drive.AvailableFreeSpace / 1024.0 / 1024.0 / 1024.0;
                ProbeStatus status = freeGb < MinimumFreeGb ? ProbeStatus.Fail : (freeGb < RecommendedFreeGb ? ProbeStatus.Warn : ProbeStatus.Ok);
                report.Items.Add(new ProbeItem(
                    "磁盘", "可用空间 (" + root + ")",
                    string.Format("建议 >= {0:0} GB（最低 {1:0} GB）", RecommendedFreeGb, MinimumFreeGb),
                    string.Format("{0:0.0} GB 可用 / {1:0.0} GB 总计", freeGb, drive.TotalSize / 1024.0 / 1024.0 / 1024.0),
                    status,
                    status == ProbeStatus.Fail ? "空间严重不足，请清理磁盘。" : (status == ProbeStatus.Warn ? "CLI + 桌面端实测约 5GB，建议预留 8GB。" : null)));
            }
            catch (Exception ex)
            {
                report.Items.Add(new ProbeItem("磁盘", "可用空间", ">= 8 GB", "读取失败: " + ex.Message, ProbeStatus.Warn));
            }

            // ---- Memory ----
            try
            {
                MEMORYSTATUSEX mem = GetMemory();
                double totalGb = mem.ullTotalPhys / 1024.0 / 1024.0 / 1024.0;
                report.Items.Add(new ProbeItem(
                    "系统", "物理内存",
                    string.Format("建议 >= {0:0} GB", RecommendedMemoryGb),
                    string.Format("{0:0.0} GB", totalGb),
                    totalGb < 4 ? ProbeStatus.Fail : (totalGb < RecommendedMemoryGb ? ProbeStatus.Warn : ProbeStatus.Ok),
                    totalGb < 4 ? "内存过低，构建桌面端可能失败。" : null));
            }
            catch (Exception ex)
            {
                report.Items.Add(new ProbeItem("系统", "物理内存", "建议 >= 8 GB", "读取失败: " + ex.Message, ProbeStatus.Info));
            }

            // ---- Long paths ----
            try
            {
                int enabled = ReadInt(Registry.GetValue(
                    @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\FileSystem",
                    "LongPathsEnabled", 0));
                report.Items.Add(new ProbeItem(
                    "系统", "长路径支持",
                    "建议开启（安装器会设置 git core.longpaths）",
                    enabled == 1 ? "已开启" : "未开启",
                    enabled == 1 ? ProbeStatus.Ok : ProbeStatus.Warn,
                    enabled == 1 ? null : "如遇路径过长错误，可启用 LongPathsEnabled 或使用较短的安装路径。"));
            }
            catch
            {
                report.Items.Add(new ProbeItem("系统", "长路径支持", "建议开启", "未知", ProbeStatus.Info));
            }

            // ---- .NET Framework ----
            try
            {
                int release = ReadInt(Registry.GetValue(
                    @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full", "Release", null));
                string version = release >= 528040 ? "4.8 或更高" : (release >= 461808 ? "4.7.2" : (release > 0 ? "低于 4.8" : "未检测到"));
                report.Items.Add(new ProbeItem(
                    "系统", ".NET Framework",
                    "4.8",
                    version,
                    release >= 528040 ? ProbeStatus.Ok : ProbeStatus.Warn,
                    release >= 528040 ? null : "本工具需要 .NET Framework 4.8（Windows 10 1903+ / Windows 11 自带）。"));
            }
            catch
            {
                report.Items.Add(new ProbeItem("系统", ".NET Framework", "4.8", "未知", ProbeStatus.Info));
            }

            ct.ThrowIfCancellationRequested();

            // ---- Existing install ----
            try
            {
                bool present = HermesPaths.InstallLooksPresent;
                string detail = present ? "已安装" : "未安装";
                InstallStamp stamp = ReadStamp();
                if (stamp != null)
                {
                    string v = !string.IsNullOrEmpty(stamp.DisplayVersion) ? stamp.DisplayVersion : stamp.BaseVersion;
                    string c = string.IsNullOrEmpty(stamp.Commit) ? "" : " @" + ShortCommit(stamp.Commit);
                    detail = "已安装 " + (v ?? "未知版本") + c;
                }
                else if (HermesPaths.CliLauncherPresent)
                {
                    detail = "检测到 hermes 启动器，但未读到版本信息";
                }

                report.Items.Add(new ProbeItem("Hermes", "CLI 安装状态", "用于判断新装/重装/升级", detail,
                    present ? ProbeStatus.Ok : ProbeStatus.Info,
                    present ? null : "未安装时可直接点「CLI → 新装」。"));
            }
            catch (Exception ex)
            {
                report.Items.Add(new ProbeItem("Hermes", "CLI 安装状态", "用于判断新装/重装/升级", "读取失败: " + ex.Message, ProbeStatus.Info));
            }

            try
            {
                bool built = HermesPaths.DesktopBuilt;
                string detail = built ? "已构建" : "未构建";
                if (built)
                {
                    var info = new FileInfo(HermesPaths.DesktopExe);
                    detail += string.Format("（{0:0} MB，{1:yyyy-MM-dd HH:mm}）", info.Length / 1024.0 / 1024.0, info.LastWriteTime);
                }
                report.Items.Add(new ProbeItem("Hermes", "桌面端状态", "已构建 / 未构建", detail,
                    built ? ProbeStatus.Ok : ProbeStatus.Info,
                    built ? null : "桌面端需要额外构建 Electron 应用，可点「桌面端 → 新装」。"));
            }
            catch (Exception ex)
            {
                report.Items.Add(new ProbeItem("Hermes", "桌面端状态", "已构建 / 未构建", "读取失败: " + ex.Message, ProbeStatus.Info));
            }

            // ---- Running processes ----
            try
            {
                List<string> running = FindHermesProcesses();
                report.Items.Add(new ProbeItem(
                    "Hermes", "运行中的进程",
                    "升级/重装前应关闭",
                    running.Count == 0 ? "无" : string.Join("，", running.ToArray()),
                    running.Count == 0 ? ProbeStatus.Ok : ProbeStatus.Warn,
                    running.Count == 0 ? null : "升级/重装前请关闭 Hermes 桌面端与网关，避免文件占用。"));
            }
            catch
            {
                report.Items.Add(new ProbeItem("Hermes", "运行中的进程", "升级/重装前应关闭", "未知", ProbeStatus.Info));
            }

            // ---- Admin ----
            try
            {
                bool admin = IsAdministrator();
                report.Items.Add(new ProbeItem(
                    "系统", "管理员权限",
                    "普通权限即可（hosts 兜底时才需要）",
                    admin ? "是" : "否",
                    ProbeStatus.Info,
                    admin ? null : "默认使用镜像，不需要管理员；仅自动修复 hosts 时会请求提权。"));
            }
            catch { }

            // ---- Proxy ----
            try
            {
                string proxy = ReadProxy();
                report.Items.Add(new ProbeItem(
                    "网络", "系统代理",
                    "无 / 自动沿用",
                    string.IsNullOrEmpty(proxy) ? "未配置" : proxy,
                    ProbeStatus.Info,
                    string.IsNullOrEmpty(proxy) ? null : "安装时将沿用该代理。"));
            }
            catch { }

            // ---- Security software ----
            try
            {
                bool defender = Process.GetProcessesByName("MsMpEng").Length > 0;
                if (defender)
                {
                    report.Items.Add(new ProbeItem(
                        "系统", "安全软件",
                        "安装时可能拦截 uv.exe",
                        "Windows Defender 正在运行",
                        ProbeStatus.Info,
                        "Hermes 自带的 uv.exe 为 Astral 官方二进制，ML 杀软可能误报；如被隔离请添加排除项后重试。"));
                }
            }
            catch { }

            // ---- Clock ----
            report.Items.Add(new ProbeItem(
                "系统", "系统时间",
                "准确（HTTPS 证书校验依赖）",
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"),
                ProbeStatus.Info,
                "若出现 TLS/证书错误，请先校准系统时间。"));

            say(string.Format("环境检测完成：{0} 项通过，{1} 项警告，{2} 项不通过。",
                Count(report, ProbeStatus.Ok), Count(report, ProbeStatus.Warn), Count(report, ProbeStatus.Fail)));

            return report;
        }

        public static InstallStamp ReadStamp()
        {
            try
            {
                if (!File.Exists(HermesPaths.InstallStamp))
                {
                    return null;
                }
                using (var stream = File.OpenRead(HermesPaths.InstallStamp))
                {
                    var serializer = new DataContractJsonSerializer(typeof(InstallStamp));
                    return serializer.ReadObject(stream) as InstallStamp;
                }
            }
            catch
            {
                return null;
            }
        }

        public static List<string> FindHermesProcesses()
        {
            var result = new List<string>();
            string home = HermesPaths.HermesHome.TrimEnd('\\') + "\\";
            foreach (string name in new[] { "Hermes", "hermes", "hermes-gateway" })
            {
                foreach (Process process in Process.GetProcessesByName(name))
                {
                    try
                    {
                        string path = null;
                        try { path = process.MainModule != null ? process.MainModule.FileName : null; } catch { }
                        if (path == null || path.StartsWith(home, StringComparison.OrdinalIgnoreCase))
                        {
                            string title = string.IsNullOrEmpty(process.MainWindowTitle) ? "" : " - " + process.MainWindowTitle;
                            result.Add(string.Format("{0} (PID {1}){2}", process.ProcessName, process.Id, title));
                        }
                    }
                    catch { }
                    finally
                    {
                        try { process.Dispose(); } catch { }
                    }
                }
            }
            return result;
        }

        public static bool IsAdministrator()
        {
            try
            {
                using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                {
                    return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch
            {
                return false;
            }
        }

        public static string ReadProxy()
        {
            try
            {
                string env = Environment.GetEnvironmentVariable("HTTPS_PROXY") ?? Environment.GetEnvironmentVariable("https_proxy");
                if (!string.IsNullOrWhiteSpace(env))
                {
                    return "环境变量 " + env;
                }
                object enable = Registry.GetValue(
                    @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Internet Settings",
                    "ProxyEnable", 0);
                object server = Registry.GetValue(
                    @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Internet Settings",
                    "ProxyServer", null);
                if (ReadInt(enable) == 1 && server != null)
                {
                    return "系统代理 " + Convert.ToString(server);
                }
            }
            catch { }
            return null;
        }

        private static int Count(ProbeReport report, ProbeStatus status)
        {
            int count = 0;
            foreach (ProbeItem item in report.Items)
            {
                if (item.Status == status) count++;
            }
            return count;
        }

        private static string ShortCommit(string commit)
        {
            if (string.IsNullOrEmpty(commit)) return "";
            return commit.Length > 8 ? commit.Substring(0, 8) : commit;
        }

        private static int ReadInt(object value)
        {
            if (value == null) return 0;
            int parsed;
            return int.TryParse(Convert.ToString(value), out parsed) ? parsed : 0;
        }

        private static string RunCapture(string fileName, string arguments, int timeoutMs)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };
                using (var process = Process.Start(psi))
                {
                    if (process == null) return string.Empty;
                    Task<string> read = process.StandardOutput.ReadToEndAsync();
                    if (!process.WaitForExit(timeoutMs))
                    {
                        try { process.Kill(); } catch { }
                        return string.Empty;
                    }
                    try { return read.Wait(2000) ? read.Result : string.Empty; }
                    catch { return string.Empty; }
                }
            }
            catch
            {
                return string.Empty;
            }
        }

        #region Memory status

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private sealed class MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;

            public MEMORYSTATUSEX()
            {
                dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

        private static MEMORYSTATUSEX GetMemory()
        {
            var status = new MEMORYSTATUSEX();
            if (!GlobalMemoryStatusEx(status))
            {
                throw new InvalidOperationException("GlobalMemoryStatusEx failed");
            }
            return status;
        }

        #endregion
    }
}
