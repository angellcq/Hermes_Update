using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace HermesInstaller.Core
{
    /// <summary>
    /// Last-resort network workaround. Only runs when the user explicitly opts in
    /// and only after elevation. Backs up hosts before touching it and offers
    /// restore. Never runs without permission.
    /// </summary>
    public static class HostsFixer
    {
        public const string HostsPath = @"C:\Windows\System32\drivers\etc\hosts";
        public const string BackupSuffix = ".bak";

        public static string BackupPath
        {
            get
            {
                try
                {
                    string backupDir = Path.Combine(HermesPaths.AppDataDir, "hosts-backups");
                    Directory.CreateDirectory(backupDir);
                    return Path.Combine(backupDir, "hosts.bak-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
                }
                catch
                {
                    return null;
                }
            }
        }

        public static bool IsHostsBlocked
        {
            get
            {
                try
                {
                    if (!File.Exists(HostsPath))
                    {
                        return false;
                    }
                    string content = File.ReadAllText(HostsPath);
                    return content.Contains("0.0.0.0") && content.Contains("raw.githubusercontent.com");
                }
                catch
                {
                    return false;
                }
            }
        }

        public static string ReadHosts()
        {
            try
            {
                return File.ReadAllText(HostsPath);
            }
            catch
            {
                return "(无法读取 hosts 文件)";
            }
        }

        public static bool TryBackup(out string backupPath)
        {
            backupPath = null;
            try
            {
                if (!File.Exists(HostsPath))
                {
                    return true;
                }
                backupPath = BackupPath;
                File.Copy(HostsPath, backupPath, true);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static Task<bool> ApplyAsync(string repoMirror, Action<string, LogLevel> log)
        {
            return Task.Run(() => Apply(repoMirror, log));
        }

        private static bool Apply(string repoMirror, Action<string, LogLevel> log)
        {
            if (log != null) log("正在写入 hosts 映射…", LogLevel.Info);
            try
            {
                string backupPath;
                if (!TryBackup(out backupPath))
                {
                    if (log != null) log("备份 hosts 失败，取消写入。", LogLevel.Error);
                    return false;
                }
                if (log != null) log("已备份 hosts 到: " + backupPath, LogLevel.Info);

                var lines = new List<string>();
                if (File.Exists(HostsPath))
                {
                    lines.AddRange(File.ReadAllLines(HostsPath));
                }

                RemoveExisting(lines, "raw.githubusercontent.com");
                RemoveExisting(lines, "github.com");

                lines.Add(string.Empty);
                lines.Add("# Hermes 安装助手: 绕过 DNS 封锁，指向 GitHub 可用 IP");
                lines.Add("185.199.108.133 raw.githubusercontent.com");
                lines.Add("185.199.111.133 raw.githubusercontent.com");
                lines.Add("20.205.243.166 github.com");

                File.WriteAllText(HostsPath, string.Join("\r\n", lines) + "\r\n", new UTF8Encoding(false));

                Run("ipconfig", "/flushdns", log);
                Run("ipconfig", "/registerdns", log);

                if (log != null) log("hosts 已更新，DNS 已刷新。", LogLevel.Success);
                return true;
            }
            catch (Exception ex)
            {
                if (log != null) log("写入 hosts 失败: " + ex.Message, LogLevel.Error);
                return false;
            }
        }

        public static void RestoreLatestBackup(Action<string, LogLevel> log)
        {
            try
            {
                string backupDir = Path.Combine(HermesPaths.AppDataDir, "hosts-backups");
                if (!Directory.Exists(backupDir))
                {
                    if (log != null) log("没有找到 hosts 备份。", LogLevel.Info);
                    return;
                }
                string[] files = Directory.GetFiles(backupDir, "hosts.bak-*");
                Array.Sort(files);
                Array.Reverse(files);
                if (files.Length == 0)
                {
                    if (log != null) log("没有找到 hosts 备份。", LogLevel.Info);
                    return;
                }
                File.Copy(files[0], HostsPath, true);
                Run("ipconfig", "/flushdns", log);
                if (log != null) log("已还原 hosts 到: " + files[0], LogLevel.Success);
            }
            catch (Exception ex)
            {
                if (log != null) log("还原 hosts 失败: " + ex.Message, LogLevel.Error);
            }
        }

        private static void RemoveExisting(List<string> lines, string host)
        {
            for (int i = lines.Count - 1; i >= 0; i--)
            {
                string line = lines[i];
                string trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith("#"))
                {
                    continue;
                }
                string[] parts = trimmed.Replace("\t", " ").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    string ip = parts[0];
                    string target = parts[1];
                    if (target.Equals(host, StringComparison.OrdinalIgnoreCase))
                    {
                        if (ip == "0.0.0.0" || ip == "127.0.0.1")
                        {
                            lines[i] = "# 已移除: " + trimmed;
                        }
                    }
                }
            }
        }

        private static void Run(string fileName, string args, Action<string, LogLevel> log)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using (var process = Process.Start(psi))
                {
                    if (process != null)
                    {
                        process.WaitForExit(5000);
                    }
                }
            }
            catch
            {
                // ipconfig is best-effort.
            }
        }
    }
}