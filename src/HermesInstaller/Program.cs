using System;
using System.Net;
using System.Threading;
using System.Windows.Forms;

namespace HermesInstaller
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                ServicePointManager.SecurityProtocol |= (SecurityProtocolType)12288; // TLS 1.3 when supported
            }
            catch { }

            if (HasArg(args, "--fix-hosts"))
            {
                RunHostsAction(true);
                return;
            }
            if (HasArg(args, "--restore-hosts"))
            {
                RunHostsAction(false);
                return;
            }
            if (HasArg(args, "--selftest"))
            {
                RunSelfTest();
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            bool createdNew;
            using (var mutex = new Mutex(true, "HermesInstaller.SingleInstance", out createdNew))
            {
                if (!createdNew)
                {
                    MessageBox.Show("Hermes 安装助手已经在运行。", "Hermes 安装助手",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                Application.ThreadException += (sender, e) =>
                {
                    MessageBox.Show("发生未处理错误：\r\n" + e.Exception.Message, "Hermes 安装助手",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                };
                AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
                {
                    var ex = e.ExceptionObject as Exception;
                    MessageBox.Show("发生未处理错误：\r\n" + (ex != null ? ex.Message : "未知错误"), "Hermes 安装助手",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                };

                Application.Run(new MainForm());
            }
        }

        private static bool HasArg(string[] args, string value)
        {
            if (args == null) return false;
            foreach (string arg in args)
            {
                if (string.Equals(arg, value, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static void RunSelfTest()
        {
            var report = new System.Text.StringBuilder();
            Action<string> line = text => { report.AppendLine(text); };
            try
            {
                AttachConsole(-1);
            }
            catch { }

            try
            {
                var logger = new Core.Logger();
                logger.Start("selftest");
                logger.Logged += entry => line(string.Format("[{0}] {1}", entry.Level, entry.Message));

                line("=== Hermes 安装助手 自检 ===");
                line("时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                line("HermesHome: " + Core.HermesPaths.HermesHome);
                line("CLI 启动器: " + (Core.HermesPaths.CliLauncherPresent ? "存在" : "缺失"));
                line("桌面端: " + (Core.HermesPaths.DesktopBuilt ? Core.HermesPaths.DesktopExe : "未构建"));

                var env = new Core.EnvironmentProbe();
                Core.ProbeReport envReport = env.RunAsync((m, l) => { }, System.Threading.CancellationToken.None)
                    .GetAwaiter().GetResult();
                line("");
                line("--- 环境检测 ---");
                foreach (Core.ProbeItem item in envReport.Items)
                {
                    line(string.Format("[{0}] {1} / {2}: {3} (期望 {4})",
                        item.StatusText, item.Category, item.Name, item.Actual, item.Expected));
                }

                var settings = Core.Settings.Load();
                var engine = new Core.InstallerEngine(settings, logger);
                string version = engine.GetVersionAsync(System.Threading.CancellationToken.None)
                    .GetAwaiter().GetResult();
                line("");
                line("hermes --version: " + (version ?? "(未安装)"));

                using (var probe = new Core.NetworkProbe())
                {
                    var net = probe.RunAsync(Core.Mirrors.BuildEndpoints(), (m, l) => { }, System.Threading.CancellationToken.None)
                        .GetAwaiter().GetResult();
                    line("");
                    line("--- 镜像检测 ---");
                    foreach (Core.NetworkResult result in net)
                    {
                        line(string.Format("{0,-32} {1,-6} {2,6}ms  {3}",
                            result.Endpoint.Name, result.Ok ? "可用" : "失败", result.LatencyMs, result.Detail));
                    }
                    string latest = probe.GetLatestReleaseAsync(System.Threading.CancellationToken.None)
                        .GetAwaiter().GetResult();
                    line("官方最新版本: " + (latest ?? "未知"));
                    line("自动选择源码镜像: " + (Core.Mirrors.PickRepoMirror(net) ?? "(无)"));
                    line("自动选择 Electron 镜像: " + Core.Mirrors.PickElectronMirror(net));
                }

                line("");
                line("--- 安装脚本 / 阶段清单 ---");
                try
                {
                    Core.InstallManifest manifest = engine.ProbeManifestAsync(System.Threading.CancellationToken.None)
                        .GetAwaiter().GetResult();
                    line("阶段数: " + manifest.Stages.Count);
                    foreach (Core.ManifestStage stage in manifest.Stages)
                    {
                        line(" - " + stage.Name + ": " + stage.Title);
                    }
                }
                catch (Exception ex)
                {
                    line("清单获取失败: " + ex.Message);
                }

                line("");
                line("日志: " + logger.LogPath);
                logger.Dispose();
                line("=== 自检结束 ===");
            }
            catch (Exception ex)
            {
                line("自检异常: " + ex);
            }

            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hermes-installer-selftest.txt");
            try { System.IO.File.WriteAllText(path, report.ToString(), new System.Text.UTF8Encoding(false)); } catch { }
            try { Console.WriteLine(report.ToString()); } catch { }
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern bool AttachConsole(int processId);

        private static void RunHostsAction(bool apply)
        {
            try
            {
                var logger = new Core.Logger();
                logger.Start(apply ? "hosts-fix" : "hosts-restore");
                bool ok;
                if (apply)
                {
                    ok = Core.HostsFixer.ApplyAsync(null, (m, l) => logger.Write(l, m)).GetAwaiter().GetResult();
                }
                else
                {
                    Core.HostsFixer.RestoreLatestBackup((m, l) => logger.Write(l, m));
                    ok = true;
                }
                MessageBox.Show(
                    (ok ? (apply ? "hosts 已修复。" : "hosts 已还原。") : "操作失败，请查看日志。") +
                    "\r\n日志: " + logger.LogPath,
                    "Hermes 安装助手",
                    MessageBoxButtons.OK,
                    ok ? MessageBoxIcon.Information : MessageBoxIcon.Error);
                logger.Dispose();
            }
            catch (Exception ex)
            {
                MessageBox.Show("操作失败：" + ex.Message, "Hermes 安装助手",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
