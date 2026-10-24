using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace HermesInstaller.Core
{
    /// <summary>进程的轻量引用（G8 改造点 2）：名字 + PID + 可执行文件完整路径。</summary>
    public sealed class ProcessRef
    {
        public string ProcessName { get; set; }
        public int Pid { get; set; }
        public string ExecutablePath { get; set; }
    }

    /// <summary>
    /// 一个 Hermes 数据根的画像（DD §2.2 G4）。Processes 由 MatchProcesses 填充；
    /// Kind 是候选来源标签（"HERMES_HOME" | "LOCALAPPDATA" | "USERPROFILE"），
    /// 去重时保留先出现者的 Kind。
    /// </summary>
    public sealed class HermesHomeInfo
    {
        private readonly List<ProcessRef> _processes = new List<ProcessRef>();

        public string Path { get; set; }
        public string Kind { get; set; }
        public bool Exists { get; set; }
        public bool HasCliInstall { get; set; }   // bin\hermes.cmd 或 hermes-agent\.git 存在
        public bool IsSelected { get; set; }      // 与 HermesPaths.HermesHome 等值
        public string CliVersion { get; set; }    // 仅 IsSelected 时探测，其余为 null（省时间）

        public List<ProcessRef> Processes { get { return _processes; } }
    }

    /// <summary>
    /// 多数据根枚举器（G4 / T-HU-DEV-5）。HermesPaths.HermesHome 保持「当前生效根」语义，
    /// 多根能力只在这里：三候选根顺序固定，按 TrimEnd('\\') 的 OrdinalIgnoreCase 去重，
    /// 每根独立 try/catch，异常只记 Warn 并跳过该根（风险 R-06），绝不让枚举整体崩溃。
    /// </summary>
    public static class HomeRegistry
    {
        // 与 InstallerEngine.StopHermesProcessesAsync 一致的进程名集合（EnvironmentProbe 额外观察 hermes-gateway）。
        private static readonly string[] ProcessNames = new[] { "Hermes", "hermes", "python", "pythonw" };

        /// <summary>枚举三候选根并去重。去重时保留先出现者的 Kind。</summary>
        public static List<HermesHomeInfo> Enumerate()
        {
            var candidates = new List<HermesHomeInfo>();
            BuildCandidates(candidates);

            var seen = new List<string>();
            var homes = new List<HermesHomeInfo>();
            foreach (HermesHomeInfo candidate in candidates)
            {
                try
                {
                    if (candidate == null || string.IsNullOrWhiteSpace(candidate.Path))
                    {
                        continue;
                    }
                    // 去重键：TrimEnd('\\') 后按 OrdinalIgnoreCase 比较，保留先出现者的 Kind。
                    string normalized = candidate.Path.Trim().TrimEnd('\\');
                    bool duplicated = false;
                    foreach (string seenPath in seen)
                    {
                        if (string.Equals(seenPath, normalized, StringComparison.OrdinalIgnoreCase))
                        {
                            duplicated = true;
                            break;
                        }
                    }
                    if (duplicated)
                    {
                        continue;
                    }
                    seen.Add(normalized);

                    candidate.Path = normalized;
                    candidate.Exists = Directory.Exists(normalized);
                    candidate.IsSelected = string.Equals(
                        HermesPaths.HermesHome, normalized, StringComparison.OrdinalIgnoreCase);
                    candidate.HasCliInstall =
                        File.Exists(Path.Combine(normalized, "bin", "hermes.cmd")) ||
                        Directory.Exists(Path.Combine(normalized, "hermes-agent", ".git"));
                    if (candidate.IsSelected)
                    {
                        candidate.CliVersion = TryReadCliVersion();
                    }
                    homes.Add(candidate);
                }
                catch (Exception ex)
                {
                    WarnStatic(string.Format("[G4] 数据根枚举异常（跳过 {0}）: {1}",
                        candidate == null ? "(null)" : candidate.Path, ex.Message));
                }
            }
            return homes;
        }

        /// <summary>
        /// 把运行中的 Hermes/python 进程按可执行文件完整路径前缀归属到各根。
        /// 取不到 MainModule（权限不足/已退出）的进程进「未知归属」，不入任何根、不杀。
        /// 匹配命中即归属并停止继续比对（一个 exe 只属于一个根）。
        /// </summary>
        public static void MatchProcesses(List<HermesHomeInfo> homes)
        {
            if (homes == null)
            {
                return; // 无根可选时无事可做
            }

            int unknownCount = 0;
            var unknownDetail = new List<string>();
            // Windows 的 GetProcessesByName 不区分大小写，ProcessNames 里 "Hermes"/"hermes" 会枚举到同一批进程；
            // 按 PID 去重，同一进程只处理一次、只归属一次（不依赖名字列表去重，那个语义不可移植）。
            var seenPids = new HashSet<int>();
            foreach (string name in ProcessNames)
            {
                foreach (Process process in Process.GetProcessesByName(name))
                {
                    if (!seenPids.Add(process.Id))
                    {
                        try { process.Dispose(); } catch { }
                        continue;
                    }
                    try
                    {
                        string exe = null;
                        try { exe = process.MainModule != null ? process.MainModule.FileName : null; } catch { }

                        if (exe == null)
                        {
                            // 未知归属（权限不足/已退出）：只统计留痕，不入任何根、不杀。
                            unknownCount++;
                            unknownDetail.Add(process.ProcessName + " (PID " + process.Id + ")");
                            continue;
                        }

                        foreach (HermesHomeInfo home in homes)
                        {
                            string rootPrefix = home.Path + "\\";
                            if (exe.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
                            {
                                home.Processes.Add(new ProcessRef
                                {
                                    ProcessName = process.ProcessName,
                                    Pid = process.Id,
                                    ExecutablePath = exe
                                });
                                break;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        WarnStatic(string.Format("[G4] 进程归属判定异常（PID {0}）: {1}", process.Id, ex.Message));
                    }
                    finally
                    {
                        try { process.Dispose(); } catch { }
                    }
                }
            }

            if (unknownCount > 0)
            {
                WarnStatic("[G4] " + unknownCount + " 个进程取不到 MainModule，归入「未知归属」，不入任何根、不杀: "
                    + string.Join("，", unknownDetail.ToArray()));
            }
        }

        /// <summary>
        /// 一行中文摘要，给 UI/日志。示例：
        /// 数据根 2 个：%LOCALAPPDATA%\hermes（选中，有 CLI）｜%USERPROFILE%\.hermes（无 CLI）。
        /// </summary>
        public static string Describe(List<HermesHomeInfo> homes)
        {
            if (homes == null)
            {
                return "数据根 0 个：(传入为 null)";
            }
            var parts = new List<string>();
            foreach (HermesHomeInfo home in homes)
            {
                var flags = new List<string>();
                if (home.IsSelected)
                {
                    flags.Add(home.Exists ? "选中" : "选中(目录不存在)");
                }
                if (home.HasCliInstall)
                {
                    flags.Add("有 CLI");
                }
                else if (home.Exists)
                {
                    flags.Add("无 CLI");
                }
                else
                {
                    flags.Add("无 CLI(目录不存在)");
                }
                parts.Add(home.Path + "（" + string.Join("，", flags.ToArray()) + "）");
            }
            return "数据根 " + homes.Count + " 个：" + string.Join("｜", parts.ToArray());
        }

        /// <summary>构造候选根：HERMES_HOME 环境变量优先 → LocalAppData\hermes → UserProfile\.hermes。顺序固定。</summary>
        private static void BuildCandidates(List<HermesHomeInfo> candidates)
        {
            string env = Environment.GetEnvironmentVariable("HERMES_HOME");
            if (!string.IsNullOrWhiteSpace(env))
            {
                candidates.Add(new HermesHomeInfo { Path = env, Kind = "HERMES_HOME" });
            }

            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrWhiteSpace(localAppData))
            {
                candidates.Add(new HermesHomeInfo
                {
                    Path = Path.Combine(localAppData, "hermes"),
                    Kind = "LOCALAPPDATA"
                });
            }

            string userProfile = Environment.GetEnvironmentVariable("USERPROFILE");
            if (!string.IsNullOrWhiteSpace(userProfile) && Directory.Exists(userProfile))
            {
                candidates.Add(new HermesHomeInfo
                {
                    Path = Path.Combine(userProfile, ".hermes"),
                    Kind = "USERPROFILE"
                });
            }
        }

        /// <summary>
        /// 仅选中根探测版本：读 install-stamp.json 的 DisplayVersion（不弹子进程——
        /// Enumerate 会被 MainForm 在 UI 线程同步调用，子进程 + 超时等待会冻住界面）。
        /// </summary>
        private static string TryReadCliVersion()
        {
            try
            {
                InstallStamp stamp = EnvironmentProbe.ReadStamp();
                return (stamp != null && !string.IsNullOrWhiteSpace(stamp.DisplayVersion))
                    ? stamp.DisplayVersion
                    : null;
            }
            catch
            {
                return null; // 版本探测失败不致命，留 null
            }
        }

        /// <summary>静态路径的日志出口，仿 RunStateStore.WarnStatic：明确在 Core 内调用，无 Logger 注入。</summary>
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
                // 日志器不可用时只能放弃；枚举路径不因日志失败受阻。
            }
        }
    }
}
