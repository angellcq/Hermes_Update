using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace HermesInstaller.Core
{
    public sealed class InstallerException : Exception
    {
        public InstallerException(string message) : base(message) { }
    }

    /// <summary>
    /// Drives the official install.ps1 stage protocol and the installed CLI.
    /// All network work goes through the mirror environment built by NetworkPreparer.
    /// </summary>
    public sealed class InstallerEngine
    {
        private readonly Settings _settings;
        private readonly Logger _logger;
        private readonly ProcessRunner _runner;
        private readonly NetworkPreparer _network;

        public InstallerEngine(Settings settings, Logger logger)
        {
            _settings = settings ?? new Settings();
            _logger = logger;
            _runner = new ProcessRunner();
            _network = new NetworkPreparer(_settings, logger);
        }

        public string InstallerScriptPath { get { return HermesPaths.CachedInstallerScript; } }

        /// <summary>Downloads the official script and returns its stage manifest (used by self-test).</summary>
        public async Task<InstallManifest> ProbeManifestAsync(CancellationToken cancellationToken)
        {
            await DownloadInstallerScriptAsync(cancellationToken).ConfigureAwait(false);
            return await GetManifestAsync(true, cancellationToken).ConfigureAwait(false);
        }

        // ---------------------------------------------------------------- install

        public async Task<OperationResult> RunInstallAsync(
            bool includeDesktop,
            OperationKind kind,
            IProgress<ProgressInfo> progress,
            CancellationToken cancellationToken)
        {
            try
            {
                Report(progress, 0, 1, "准备", "正在获取官方安装脚本…", false);
                await DownloadInstallerScriptAsync(cancellationToken).ConfigureAwait(false);

                _logger.Stage("读取安装阶段清单…");
                InstallManifest manifest = await GetManifestAsync(includeDesktop, cancellationToken).ConfigureAwait(false);
                if (manifest.Stages.Count == 0)
                {
                    throw new InstallerException("安装脚本没有返回任何阶段。");
                }

                int total = manifest.Stages.Count;
                for (int i = 0; i < manifest.Stages.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ManifestStage stage = manifest.Stages[i];
                    Report(progress, i + 1, total, stage.Title, "正在执行: " + stage.Title, true);
                    _logger.Stage(string.Format("[{0}/{1}] {2}", i + 1, total, stage.Title));

                    StageResult result = await RunStageAsync(stage.Name, includeDesktop, cancellationToken).ConfigureAwait(false);
                    if (!result.Ok)
                    {
                        string reason = string.IsNullOrWhiteSpace(result.Reason) ? "未知错误" : result.Reason;
                        throw new InstallerException(string.Format("阶段「{0}」失败: {1}", stage.Title, reason));
                    }

                    Report(progress, i + 1, total, stage.Title,
                        result.Skipped ? "已跳过: " + stage.Title : "完成: " + stage.Title, false);
                }

                _logger.Success("安装流程结束。");
                return OperationResult.Ok(
                    includeDesktop ? "Hermes CLI + 桌面端安装完成。" : "Hermes CLI 安装完成。",
                    _logger.LogPath);
            }
            catch (OperationCanceledException)
            {
                _logger.Warn("操作已取消。");
                return OperationResult.Error("操作已取消。", _logger.LogPath);
            }
            catch (Exception ex)
            {
                _logger.Error(ex.Message);
                return OperationResult.Error(ex.Message, _logger.LogPath);
            }
            finally
            {
                AppendHermesLogs();
            }
        }

        private async Task DownloadInstallerScriptAsync(CancellationToken cancellationToken)
        {
            HermesPaths.EnsureAppData();
            string target = HermesPaths.CachedInstallerScript;
            Exception lastError = null;

            _logger.Info("下载官方安装脚本: " + Mirrors.OfficialInstaller);
            try
            {
                using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(90) })
                {
                    byte[] bytes = await client.GetByteArrayAsync(Mirrors.OfficialInstaller).ConfigureAwait(false);
                    if (bytes == null || bytes.Length < 10000)
                    {
                        throw new InstallerException("下载到的 install.ps1 内容异常。");
                    }
                    File.WriteAllBytes(target, bytes);
                    _logger.Success(string.Format("安装脚本已保存（{0:0.0} KB）。", bytes.Length / 1024.0));
                    return;
                }
            }
            catch (Exception ex)
            {
                lastError = ex;
                _logger.Warn("在线获取安装脚本失败: " + ex.Message);
            }

            if (File.Exists(target))
            {
                _logger.Warn("使用缓存的安装脚本: " + target);
                return;
            }

            throw new InstallerException("无法获取官方安装脚本，且没有缓存副本。" +
                (lastError != null ? " (" + lastError.Message + ")" : string.Empty));
        }

        private async Task<InstallManifest> GetManifestAsync(bool includeDesktop, CancellationToken cancellationToken)
        {
            var env = _network.BuildEnvironment();
            string invocation = "& " + ProcessRunner.Quote(InstallerScriptPath) + " -Manifest" +
                                (includeDesktop ? " -IncludeDesktop" : string.Empty);

            ProcessResult result = await _runner.RunPowerShellAsync(
                invocation,
                HermesPaths.HermesHome,
                env,
                (line, level) => { if (level == LogLevel.Error) _logger.Error(line); },
                cancellationToken).ConfigureAwait(false);

            string json = ExtractJsonLine(result.StandardOutput, "\"stages\"");
            if (json == null)
            {
                throw new InstallerException("安装脚本未返回阶段清单。输出: " + Truncate(result.StandardOutput, 500));
            }

            var manifest = new InstallManifest();
            var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            var root = serializer.Deserialize<Dictionary<string, object>>(json);
            if (root == null || !root.ContainsKey("stages"))
            {
                throw new InstallerException("阶段清单格式无法识别。");
            }

            if (root.ContainsKey("protocol_version"))
            {
                manifest.ProtocolVersion = Convert.ToInt32(root["protocol_version"]);
            }

            var stages = root["stages"] as ArrayList;
            if (stages != null)
            {
                foreach (object entry in stages)
                {
                    var dict = entry as Dictionary<string, object>;
                    if (dict == null)
                    {
                        continue;
                    }
                    manifest.Stages.Add(new ManifestStage
                    {
                        Name = GetString(dict, "name"),
                        Title = GetString(dict, "title"),
                        Category = GetString(dict, "category"),
                        NeedsUserInput = GetBool(dict, "needs_user_input")
                    });
                }
            }

            _logger.Info(string.Format("安装阶段: {0}", string.Join(" → ",
                manifest.Stages.ConvertAll(s => s.Title).ToArray())));
            return manifest;
        }

        private async Task<StageResult> RunStageAsync(string stage, bool includeDesktop, CancellationToken cancellationToken)
        {
            var env = _network.BuildEnvironment();
            string channel = string.IsNullOrWhiteSpace(_settings.Channel) ? "stable" : _settings.Channel;
            string invocation = "& " + ProcessRunner.Quote(InstallerScriptPath) +
                                " -Stage " + ProcessRunner.Quote(stage) +
                                " -NonInteractive -Json -Channel " + ProcessRunner.Quote(channel) +
                                (includeDesktop ? " -IncludeDesktop" : string.Empty) +
                                (_settings.SkipBrowser ? " -SkipBrowser" : string.Empty) +
                                (_settings.SkipComputerUse ? " -SkipComputerUse" : string.Empty);

            ProcessResult result = await _runner.RunPowerShellAsync(
                invocation,
                HermesPaths.HermesHome,
                env,
                (line, level) => _logger.Write(level, line),
                cancellationToken).ConfigureAwait(false);

            if (result.Cancelled)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            string json = ExtractLastJsonLine(result.StandardOutput, "\"ok\"");
            if (json != null)
            {
                try
                {
                    var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                    var dict = serializer.Deserialize<Dictionary<string, object>>(json);
                    if (dict != null)
                    {
                        return new StageResult
                        {
                            Ok = GetBool(dict, "ok"),
                            Skipped = GetBool(dict, "skipped"),
                            Reason = GetString(dict, "reason")
                        };
                    }
                }
                catch
                {
                    // Fall through to the exit code.
                }
            }

            return new StageResult
            {
                Ok = result.ExitCode == 0,
                Skipped = false,
                Reason = result.ExitCode == 0 ? null : "退出码 " + result.ExitCode + " " + Truncate(result.StandardError, 200)
            };
        }

        // ----------------------------------------------------------------- update

        public async Task<OperationResult> RunUpgradeAsync(
            bool includeDesktop,
            IProgress<ProgressInfo> progress,
            CancellationToken cancellationToken)
        {
            try
            {
                if (!HermesPaths.CliLauncherPresent)
                {
                    throw new InstallerException("未检测到 Hermes CLI，请先执行「新装」。");
                }

                _logger.Stage("停止运行中的 Hermes 进程…");
                await StopHermesProcessesAsync(cancellationToken).ConfigureAwait(false);

                Report(progress, 0, 1, "升级", "正在执行 hermes update…", true);
                var env = _network.BuildEnvironment();
                string invocation = "& " + ProcessRunner.Quote(HermesPaths.HermesCmd) + " update --yes" +
                                    (_settings.BackupBeforeUpdate ? " --backup" : string.Empty);

                ProcessResult result = await _runner.RunPowerShellAsync(
                    invocation,
                    HermesPaths.HermesHome,
                    env,
                    (line, level) => _logger.Write(level, line),
                    cancellationToken).ConfigureAwait(false);

                if (result.Cancelled)
                {
                    throw new OperationCanceledException(cancellationToken);
                }
                if (result.ExitCode != 0)
                {
                    throw new InstallerException("hermes update 失败（退出码 " + result.ExitCode + "）。" +
                        Truncate(result.StandardError, 300));
                }

                if (includeDesktop)
                {
                    if (!HermesPaths.DesktopBuilt)
                    {
                        _logger.Warn("未找到桌面端产物，单独重建…");
                        Report(progress, 0, 1, "桌面端", "正在重建桌面端…", true);
                        ProcessResult build = await _runner.RunPowerShellAsync(
                            "& " + ProcessRunner.Quote(HermesPaths.HermesCmd) + " desktop --build-only --force-build",
                            HermesPaths.HermesHome,
                            env,
                            (line, level) => _logger.Write(level, line),
                            cancellationToken).ConfigureAwait(false);
                        if (build.ExitCode != 0 || !HermesPaths.DesktopBuilt)
                        {
                            throw new InstallerException("桌面端重建失败。请查看日志。");
                        }
                    }
                    else
                    {
                        _logger.Success("桌面端已随更新重建。");
                    }
                }

                Report(progress, 1, 1, "升级", "升级完成", false);
                _logger.Success("升级完成。");
                return OperationResult.Ok("升级完成。", _logger.LogPath);
            }
            catch (OperationCanceledException)
            {
                _logger.Warn("操作已取消。");
                return OperationResult.Error("操作已取消。", _logger.LogPath);
            }
            catch (Exception ex)
            {
                _logger.Error(ex.Message);
                return OperationResult.Error(ex.Message, _logger.LogPath);
            }
            finally
            {
                AppendHermesLogs();
            }
        }

        public async Task<OperationResult> RunUninstallAsync(
            IProgress<ProgressInfo> progress,
            CancellationToken cancellationToken)
        {
            try
            {
                if (!HermesPaths.CliLauncherPresent)
                {
                    throw new InstallerException("未检测到 Hermes CLI。");
                }
                await StopHermesProcessesAsync(cancellationToken).ConfigureAwait(false);
                Report(progress, 0, 1, "卸载", "正在卸载（保留用户数据）…", true);

                ProcessResult result = await _runner.RunPowerShellAsync(
                    "& " + ProcessRunner.Quote(HermesPaths.HermesCmd) + " uninstall --yes",
                    HermesPaths.HermesHome,
                    _network.BuildEnvironment(),
                    (line, level) => _logger.Write(level, line),
                    cancellationToken).ConfigureAwait(false);

                if (result.ExitCode != 0)
                {
                    throw new InstallerException("卸载失败（退出码 " + result.ExitCode + "）。");
                }
                Report(progress, 1, 1, "卸载", "卸载完成", false);
                return OperationResult.Ok("卸载完成（用户数据已保留）。", _logger.LogPath);
            }
            catch (OperationCanceledException)
            {
                return OperationResult.Error("操作已取消。", _logger.LogPath);
            }
            catch (Exception ex)
            {
                _logger.Error(ex.Message);
                return OperationResult.Error(ex.Message, _logger.LogPath);
            }
            finally
            {
                AppendHermesLogs();
            }
        }

        // ---------------------------------------------------------------- helpers

        public async Task<string> GetVersionAsync(CancellationToken cancellationToken)
        {
            if (!HermesPaths.CliLauncherPresent)
            {
                return null;
            }
            try
            {
                ProcessResult result = await _runner.RunPowerShellAsync(
                    "& " + ProcessRunner.Quote(HermesPaths.HermesCmd) + " --version",
                    HermesPaths.HermesHome,
                    _network.BuildEnvironment(),
                    null,
                    cancellationToken).ConfigureAwait(false);
                string text = (result.StandardOutput ?? string.Empty).Trim();
                if (text.Length == 0)
                {
                    return null;
                }
                string[] lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                return lines.Length > 0 ? lines[0].Trim() : null;
            }
            catch
            {
                return null;
            }
        }

        public async Task StopHermesProcessesAsync(CancellationToken cancellationToken)
        {
            string home = HermesPaths.HermesHome.TrimEnd('\\') + "\\";
            var killed = new List<string>();

            foreach (string name in new[] { "Hermes", "hermes", "python", "pythonw" })
            {
                foreach (Process process in Process.GetProcessesByName(name))
                {
                    try
                    {
                        string path = null;
                        try { path = process.MainModule != null ? process.MainModule.FileName : null; } catch { }
                        if (path == null || !path.StartsWith(home, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                        if (process.Id == Process.GetCurrentProcess().Id)
                        {
                            continue;
                        }
                        string label = process.ProcessName + " (PID " + process.Id + ")";
                        try
                        {
                            process.Kill();
                            process.WaitForExit(8000);
                            killed.Add(label);
                        }
                        catch
                        {
                            ProcessRunner.KillTree(process);
                            killed.Add(label);
                        }
                    }
                    catch { }
                    finally
                    {
                        try { process.Dispose(); } catch { }
                    }
                }
            }

            if (killed.Count > 0)
            {
                _logger.Warn("已停止: " + string.Join("，", killed.ToArray()));
                await Task.Delay(1500, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                _logger.Info("没有需要停止的 Hermes 进程。");
            }
        }

        public void LaunchCli()
        {
            if (!HermesPaths.CliLauncherPresent)
            {
                throw new InstallerException("未检测到 hermes 启动器。");
            }
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/k \"" + HermesPaths.HermesCmd + "\"",
                UseShellExecute = true,
                WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            });
        }

        public void LaunchDesktop()
        {
            if (!HermesPaths.DesktopBuilt)
            {
                throw new InstallerException("桌面端尚未构建，请先执行「桌面端 → 新装」。");
            }
            Process.Start(new ProcessStartInfo
            {
                FileName = HermesPaths.DesktopExe,
                WorkingDirectory = Path.GetDirectoryName(HermesPaths.DesktopExe) ?? Environment.CurrentDirectory,
                UseShellExecute = true
            });
        }

        private void AppendHermesLogs()
        {
            try
            {
                _logger.AppendExternalLog("Hermes install.log", HermesPaths.InstallLog, 120);
                _logger.AppendExternalLog("Hermes bootstrap.log", HermesPaths.BootstrapLog, 80);
            }
            catch { }
        }

        private static void Report(IProgress<ProgressInfo> progress, int index, int total, string stage, string message, bool stageChange)
        {
            if (progress != null)
            {
                try
                {
                    progress.Report(new ProgressInfo
                    {
                        Index = index,
                        Total = total,
                        StageName = stage,
                        Message = message,
                        IsStageChange = stageChange
                    });
                }
                catch { }
            }
        }

        private static string ExtractJsonLine(string output, string marker)
        {
            if (string.IsNullOrEmpty(output)) return null;
            foreach (string line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith("{") && trimmed.Contains(marker))
                {
                    return trimmed;
                }
            }
            return null;
        }

        private static string ExtractLastJsonLine(string output, string marker)
        {
            if (string.IsNullOrEmpty(output)) return null;
            string found = null;
            foreach (string line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith("{") && trimmed.Contains(marker))
                {
                    found = trimmed;
                }
            }
            return found;
        }

        private static string GetString(Dictionary<string, object> dict, string key)
        {
            if (dict != null && dict.ContainsKey(key) && dict[key] != null)
            {
                return Convert.ToString(dict[key]);
            }
            return null;
        }

        private static bool GetBool(Dictionary<string, object> dict, string key)
        {
            if (dict != null && dict.ContainsKey(key) && dict[key] != null)
            {
                try { return Convert.ToBoolean(dict[key]); } catch { }
            }
            return false;
        }

        private static string Truncate(string text, int max)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            text = text.Trim();
            return text.Length <= max ? text : text.Substring(0, max) + "…";
        }

        private sealed class StageResult
        {
            public bool Ok { get; set; }
            public bool Skipped { get; set; }
            public string Reason { get; set; }
        }
    }
}
