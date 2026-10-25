using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using HermesInstaller.Core;

namespace HermesInstaller
{
    public partial class MainForm : Form
    {
        private readonly Settings _settings;
        private readonly Logger _logger = new Logger();

        private ProbeReport _lastReport;
        private System.Collections.Generic.List<NetworkResult> _networkResults;
        private CancellationTokenSource _cts;
        private bool _busy;
        private bool _probing;
        private bool _networkBusy;
        private bool _hostsWarningShown;

        public MainForm()
        {
            InitializeComponent();

            // 设计器会实例化本窗体：此时不要读配置、不要起后台探测。
            if (LicenseManager.UsageMode == LicenseUsageMode.Designtime)
            {
                return;
            }

            _settings = Settings.Load();
            try
            {
                Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
            }
            catch
            {
                Icon = SystemIcons.Application;
            }

            LoadSettingsToUi();
            UpdateButtons();
            WireEvents();
        }

        private void WireEvents()
        {
            btnAbout.Click += (s, e) => ShowAbout();
            btnOpenData.Click += (s, e) => OpenFolder(Directory.Exists(HermesPaths.HermesHome) ? HermesPaths.HermesHome : HermesPaths.AppDataDir);
            btnSaveLog.Click += (s, e) => SaveLog();
            btnOpenLogs.Click += (s, e) => OpenFolder(HermesPaths.LogDirectory);
            btnCancel.Click += (s, e) => CancelOperation();
            btnRefresh.Click += async (s, e) => await RefreshEnvironmentAsync();
            btnNetwork.Click += async (s, e) => await RefreshNetworkAsync();
            btnApplyMirrors.Click += (s, e) => { ApplySettingsFromUi(); _settings.Save(); AppendLog(LogLevel.Success, "已应用镜像设置。"); };
            btnFixHosts.Click += async (s, e) => await FixHostsAsync();
            btnRestoreHosts.Click += (s, e) => RestoreHosts();
            btnProxy.Click += (s, e) => EditProxySettings();
            chkHostsFallback.CheckedChanged += OnHostsFallbackChanged;

            btnCliInstall.Click += async (s, e) => await RunOperationAsync(OperationKind.CliInstall);
            btnCliRepair.Click += async (s, e) => await RunOperationAsync(OperationKind.CliRepair);
            btnCliUpgrade.Click += async (s, e) => await RunOperationAsync(OperationKind.CliUpgrade);
            btnCliUninstall.Click += async (s, e) => await RunOperationAsync(OperationKind.CliUninstall);
            btnCliLaunch.Click += (s, e) => LaunchCli();

            btnDesktopInstall.Click += async (s, e) => await RunOperationAsync(OperationKind.DesktopInstall);
            btnDesktopRepair.Click += async (s, e) => await RunOperationAsync(OperationKind.DesktopRepair);
            btnDesktopUpgrade.Click += async (s, e) => await RunOperationAsync(OperationKind.DesktopUpgrade);
            btnDesktopLaunch.Click += (s, e) => LaunchDesktop();

            rightScroll.Resize += (sender, args) =>
            {
                try { rightLayout.Width = Math.Max(340, rightScroll.ClientSize.Width - 16); } catch { }
            };

            _logger.Logged += entry => AppendLog(entry.Level, entry.Message);
            Shown += async (sender, args) =>
            {
                InitializeSplitter();
                await RefreshEnvironmentAsync();
            };
            FormClosing += OnFormClosing;
        }

        private void InitializeSplitter()
        {
            try
            {
                if (splitMain == null || splitMain.Width < 400)
                {
                    return;
                }
                int width = splitMain.Width;
                int desired = (int)(width * 0.58);
                int lower = 200;
                int upper = width - 200 - splitMain.SplitterWidth;
                if (upper > lower)
                {
                    splitMain.SplitterDistance = Math.Max(lower, Math.Min(desired, upper));
                }
            }
            catch { }
        }

        // -------------------------------------------------------------- settings

        private void LoadSettingsToUi()
        {
            if (!cmbChannel.Items.Contains(_settings.Channel))
            {
                cmbChannel.Items.Add(_settings.Channel);
            }
            cmbChannel.SelectedItem = _settings.Channel;

            if (!string.IsNullOrWhiteSpace(_settings.RepoMirror)) cmbRepoMirror.Text = _settings.RepoMirror;
            if (!string.IsNullOrWhiteSpace(_settings.ElectronMirror)) cmbElectronMirror.Text = _settings.ElectronMirror;
            if (!string.IsNullOrWhiteSpace(_settings.BuilderMirror)) cmbBuilderMirror.Text = _settings.BuilderMirror;
            if (!string.IsNullOrWhiteSpace(_settings.NpmRegistry)) cmbNpmRegistry.Text = _settings.NpmRegistry;
            if (!string.IsNullOrWhiteSpace(_settings.PipIndex)) cmbPipIndex.Text = _settings.PipIndex;

            chkSkipBrowser.Checked = _settings.SkipBrowser;
            chkSkipComputerUse.Checked = _settings.SkipComputerUse;
            chkBackup.Checked = _settings.BackupBeforeUpdate;
            chkHostsFallback.Checked = _settings.AllowHostsFix;
        }

        private void ApplySettingsFromUi()
        {            _settings.Channel = cmbChannel.SelectedItem as string;
            if (string.IsNullOrWhiteSpace(_settings.Channel))
            {
                _settings.Channel = cmbChannel.Text.Trim();
            }
            if (string.IsNullOrWhiteSpace(_settings.Channel))
            {
                _settings.Channel = "stable";
            }
            _settings.SkipBrowser = chkSkipBrowser.Checked;
            _settings.SkipComputerUse = chkSkipComputerUse.Checked;
            _settings.BackupBeforeUpdate = chkBackup.Checked;
            _settings.AllowHostsFix = chkHostsFallback.Checked;
            _settings.RepoMirror = cmbRepoMirror.Text.Trim();
            _settings.ElectronMirror = cmbElectronMirror.Text.Trim();
            _settings.BuilderMirror = cmbBuilderMirror.Text.Trim();
            _settings.NpmRegistry = cmbNpmRegistry.Text.Trim();
            _settings.PipIndex = cmbPipIndex.Text.Trim();
        }

        /// <summary>
        /// E14：SSH 代理配置入口。先把界面上现有选项收进 settings（避免保存代理时
        /// 把镜像等未提交的旧值写回去），再弹独立对话框。
        /// </summary>
        private void EditProxySettings()
        {
            ApplySettingsFromUi();
            if (Ui.ProxyForm.Edit(this, _settings, _logger))
            {
                _settings.Save();
                AppendLog(LogLevel.Success, _settings.SshProxyEnabled
                    ? "SSH 代理已启用: " + _settings.SshUser + "@" + _settings.SshHost
                        + "（SOCKS5 127.0.0.1:" + _settings.SshSocksPort + "）"
                    : "SSH 代理已关闭。");
            }
        }

        // ------------------------------------------------------------- detection

        private async Task RefreshEnvironmentAsync()
        {
            if (_probing || _busy)
            {
                return;
            }
            _probing = true;
            btnRefresh.Enabled = false;
            btnNetwork.Enabled = false;
            lblStatus.Text = "检测中…";
            lblStatus.ForeColor = Color.DimGray;
            try
            {
                var probe = new EnvironmentProbe();
                _lastReport = await probe.RunAsync((m, l) => AppendLog(l, m), CancellationToken.None);
                FillProbe(_lastReport);
                UpdateSummary();
                await RefreshNetworkAsync();
            }
            catch (Exception ex)
            {
                AppendLog(LogLevel.Error, "环境检测失败: " + ex.Message);
            }
            finally
            {
                _probing = false;
                btnRefresh.Enabled = true;
                btnNetwork.Enabled = true;
                UpdateButtons();
            }
        }

        private async Task RefreshNetworkAsync()
        {
            if (_networkBusy || _busy)
            {
                return;
            }
            _networkBusy = true;
            btnNetwork.Enabled = false;
            try
            {
                AppendLog(LogLevel.Stage, "开始镜像检测…");
                using (var probe = new NetworkProbe())
                {
                    _networkResults = await probe.RunAsync(Mirrors.BuildEndpoints(), (m, l) => AppendLog(l, m), CancellationToken.None);
                    FillNetwork(_networkResults);
                    string latest = await probe.GetLatestReleaseAsync(CancellationToken.None);
                    lblLatest.Text = string.IsNullOrEmpty(latest) ? "官方最新: 未知" : "官方最新: " + latest;
                }
                AutoPickMirrors();
                UpdateSummary();
            }
            catch (Exception ex)
            {
                AppendLog(LogLevel.Error, "镜像检测失败: " + ex.Message);
            }
            finally
            {
                _networkBusy = false;
                btnNetwork.Enabled = true;
            }
        }

        private void FillProbe(ProbeReport report)
        {
            dgvProbe.SuspendLayout();
            dgvProbe.Rows.Clear();
            foreach (ProbeItem item in report.Items)
            {
                int index = dgvProbe.Rows.Add(item.Category, item.Name, item.Expected, item.Actual, item.StatusText, item.Advice);
                DataGridViewRow row = dgvProbe.Rows[index];
                row.DefaultCellStyle.ForeColor = StatusColor(item.Status);
            }
            dgvProbe.ResumeLayout();
        }

        private void FillNetwork(System.Collections.Generic.List<NetworkResult> results)
        {
            dgvNetwork.SuspendLayout();
            dgvNetwork.Rows.Clear();
            foreach (NetworkResult result in results)
            {
                int index = dgvNetwork.Rows.Add(
                    result.Endpoint.Category,
                    result.Endpoint.Name,
                    result.Endpoint.Url,
                    result.StatusText,
                    result.Ok ? (result.LatencyMs + " ms") : "-",
                    result.Detail);
                dgvNetwork.Rows[index].DefaultCellStyle.ForeColor =
                    result.Ok ? Color.FromArgb(0, 128, 0) : Color.Firebrick;
            }
            dgvNetwork.ResumeLayout();
        }

        private void UpdateSummary()
        {
            if (_lastReport == null)
            {
                return;
            }
            int fail = _lastReport.Items.Count(i => i.Status == ProbeStatus.Fail);
            int warn = _lastReport.Items.Count(i => i.Status == ProbeStatus.Warn);
            if (fail > 0)
            {
                lblStatus.Text = "环境不满足（" + fail + " 项）";
                lblStatus.ForeColor = Color.Firebrick;
            }
            else if (warn > 0)
            {
                lblStatus.Text = "环境可用（" + warn + " 项警告）";
                lblStatus.ForeColor = Color.FromArgb(191, 96, 0);
            }
            else
            {
                lblStatus.Text = "环境就绪";
                lblStatus.ForeColor = Color.SeaGreen;
            }

            string network = "未检测";
            if (_networkResults != null)
            {
                int ok = _networkResults.Count(r => r.Ok);
                int total = _networkResults.Count;
                network = ok + "/" + total + " 可用";
            }

            lblSubtitle.Text = string.Format(
                "Hermes CLI + 桌面端 · CLI: {0} · 桌面端: {1} · 镜像: {2}",
                HermesPaths.CliLauncherPresent ? "已安装" : "未安装",
                HermesPaths.DesktopBuilt ? "已构建" : "未构建",
                network);
        }

        private void AutoPickMirrors()
        {
            if (_networkResults == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(_settings.RepoMirror) || _settings.AutoDetectMirrors)
            {
                string repo = Mirrors.PickRepoMirror(_networkResults);
                if (string.IsNullOrEmpty(repo))
                {
                    repo = Mirrors.RepoCandidates[0];
                }
                cmbRepoMirror.Text = repo;
                _settings.RepoMirror = repo;
            }

            string electron = Mirrors.PickElectronMirror(_networkResults);
            if (!string.IsNullOrEmpty(electron) && (string.IsNullOrWhiteSpace(_settings.ElectronMirror) || _settings.AutoDetectMirrors))
            {
                cmbElectronMirror.Text = electron;
                _settings.ElectronMirror = electron;
            }

            string builder = Mirrors.PickBuilderMirror(_networkResults);
            if (!string.IsNullOrEmpty(builder) && (string.IsNullOrWhiteSpace(_settings.BuilderMirror) || _settings.AutoDetectMirrors))
            {
                cmbBuilderMirror.Text = builder;
                _settings.BuilderMirror = builder;
            }

            string npm = Mirrors.PickNpmRegistry(_networkResults);
            if (!string.IsNullOrEmpty(npm) && (string.IsNullOrWhiteSpace(_settings.NpmRegistry) || _settings.AutoDetectMirrors))
            {
                cmbNpmRegistry.Text = npm;
                _settings.NpmRegistry = npm;
            }

            string pip = Mirrors.PickPipIndex(_networkResults);
            if (!string.IsNullOrEmpty(pip) && (string.IsNullOrWhiteSpace(_settings.PipIndex) || _settings.AutoDetectMirrors))
            {
                cmbPipIndex.Text = pip;
                _settings.PipIndex = pip;
            }
        }

        private static Color StatusColor(ProbeStatus status)
        {
            switch (status)
            {
                case ProbeStatus.Ok: return Color.FromArgb(0, 128, 0);
                case ProbeStatus.Warn: return Color.FromArgb(191, 96, 0);
                case ProbeStatus.Fail: return Color.Firebrick;
                default: return Color.DimGray;
            }
        }

        // ------------------------------------------------------------- operations

        private async Task RunOperationAsync(OperationKind kind)
        {
            if (_busy)
            {
                return;
            }
            if (!ConfirmOperation(kind))
            {
                return;
            }

            ApplySettingsFromUi();
            _settings.LastOperation = kind.ToString();
            _settings.Save();

            _logger.Start(kind.ToString());
            rtbLog.Clear();
            _busy = true;
            SetBusy(true);
            pb.Style = ProgressBarStyle.Continuous;
            pb.Value = 0;
            lblProgress.Text = "正在准备…";
            _cts = new CancellationTokenSource();

            var homes = HomeRegistry.Enumerate();
            HomeRegistry.MatchProcesses(homes);
            AppendLog(LogLevel.Info, "[G4] " + HomeRegistry.Describe(homes));
            var progress = new Progress<ProgressInfo>(OnProgress);
            var engine = new InstallerEngine(_settings, _logger);
            OperationResult result = null;

            try
            {
                switch (kind)
                {
                    case OperationKind.CliInstall:
                    case OperationKind.CliRepair:
                        result = await engine.RunInstallAsync(false, kind, progress, _cts.Token);
                        break;
                    case OperationKind.DesktopInstall:
                    case OperationKind.DesktopRepair:
                        result = await engine.RunInstallAsync(true, kind, progress, _cts.Token);
                        break;
                    case OperationKind.CliUpgrade:
                        result = await engine.RunUpgradeAsync(false, progress, _cts.Token);
                        engine.ReportRunStage("CLI 升级结束");
                        break;
                    case OperationKind.DesktopUpgrade:
                        result = await engine.RunUpgradeAsync(true, progress, _cts.Token);
                        engine.ReportRunStage("桌面端升级结束");
                        break;
                    case OperationKind.CliUninstall:
                        result = await engine.RunUninstallAsync(progress, _cts.Token);
                        break;
                    case OperationKind.DesktopLaunch:
                        engine.LaunchDesktop();
                        result = OperationResult.Ok("已启动桌面端。");
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                result = OperationResult.Error("操作已取消。", _logger.LogPath);
            }
            catch (Exception ex)
            {
                AppendLog(LogLevel.Error, ex.Message);
                result = OperationResult.Error(ex.Message, _logger.LogPath);
            }
            finally
            {
                // G6（T-HU-DEV-4）：所有操作路径收尾都要清掉 run.json（含取消/异常）。
                InstallerEngine.ClearRunState();
                _busy = false;
                SetBusy(false);
                if (_cts != null)
                {
                    _cts.Dispose();
                    _cts = null;
                }
            }

            if (result != null)
            {
                LogLevel level = AppendVerification(result);
                AppendLog(level, result.Message);
                lblProgress.Text = result.Message;
                MessageBoxIcon icon;
                switch (result.Outcome)
                {
                    case OperationOutcome.Success: icon = MessageBoxIcon.Information; break;
                    case OperationOutcome.Degraded: icon = MessageBoxIcon.Warning; break;
                    default: icon = MessageBoxIcon.Error; break;
                }
                MessageBox.Show(this, result.Message + (string.IsNullOrEmpty(result.LogPath) ? "" : "\r\n\r\n日志: " + result.LogPath),
                    "Hermes 安装助手",
                    MessageBoxButtons.OK,
                    icon);

                if (!result.Success && LooksLikeNetworkFailure(result.Message))
                {
                    await OfferHostsFixAsync(result.Message);
                }
            }

            await RefreshEnvironmentAsync();
        }

        /// <summary>补写断言汇总行（逐条断言已由 Logger 出口实时落日志），返回结论行应用的日志级别（文案逻辑在 UiTextBuilder）。</summary>
        private LogLevel AppendVerification(OperationResult result)
        {
            if (result.Verification == null)
            {
                return result.Success ? LogLevel.Success : LogLevel.Error;
            }

            LogLevel level;
            switch (result.Outcome)
            {
                case OperationOutcome.Success: level = LogLevel.Success; break;
                case OperationOutcome.Degraded: level = LogLevel.Warn; break;
                default: level = LogLevel.Error; break;
            }

            AppendLog(LogLevel.Info, UiTextBuilder.VerificationSummary(result.Verification));
            return level;
        }

        private bool ConfirmOperation(OperationKind kind)
        {
            switch (kind)
            {
                case OperationKind.CliInstall:
                    if (HermesPaths.CliLauncherPresent)
                    {
                        return Confirm("已检测到 Hermes CLI，将重新运行官方安装器并保留现有数据。继续？");
                    }
                    break;
                case OperationKind.CliRepair:
                    return Confirm("将重新运行官方安装器进行修复/更新，现有配置、会话和密钥会保留。继续？");
                case OperationKind.DesktopInstall:
                case OperationKind.DesktopRepair:
                    if (HermesPaths.DesktopBuilt)
                    {
                        return Confirm("桌面端已存在，将重新构建 Electron 应用。继续？");
                    }
                    break;
                case OperationKind.CliUninstall:
                    return Confirm("将卸载 Hermes 程序（默认保留用户数据、配置和会话）。继续？");
                case OperationKind.CliUpgrade:
                case OperationKind.DesktopUpgrade:
                    if (EnvironmentProbe.FindHermesProcesses().Count > 0)
                    {
                        return Confirm("检测到 Hermes 正在运行，将先停止桌面端/网关再升级，然后需要手动重新启动。继续？");
                    }
                    break;
            }
            return true;
        }

        private bool Confirm(string text)
        {
            return MessageBox.Show(this, text, "请确认", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
        }

        private void OnProgress(ProgressInfo info)
        {
            if (info == null)
            {
                return;
            }
            if (info.Total > 0)
            {
                pb.Style = ProgressBarStyle.Continuous;
                pb.Maximum = info.Total;
                pb.Value = Math.Max(0, Math.Min(info.Index, info.Total));
            }
            else
            {
                pb.Style = ProgressBarStyle.Marquee;
            }
            if (!string.IsNullOrEmpty(info.Message))
            {
                lblProgress.Text = info.Message;
            }
        }

        private void SetBusy(bool busy)
        {
            btnCliInstall.Enabled = !busy;
            btnCliRepair.Enabled = !busy;
            btnCliUpgrade.Enabled = !busy;
            btnCliUninstall.Enabled = !busy;
            btnCliLaunch.Enabled = !busy;
            btnDesktopInstall.Enabled = !busy;
            btnDesktopRepair.Enabled = !busy;
            btnDesktopUpgrade.Enabled = !busy;
            btnDesktopLaunch.Enabled = !busy;
            btnRefresh.Enabled = !busy;
            btnNetwork.Enabled = !busy;
            btnApplyMirrors.Enabled = !busy;
            btnFixHosts.Enabled = !busy;
            btnRestoreHosts.Enabled = !busy;
            cmbChannel.Enabled = !busy;
            cmbRepoMirror.Enabled = !busy;
            cmbElectronMirror.Enabled = !busy;
            cmbBuilderMirror.Enabled = !busy;
            cmbNpmRegistry.Enabled = !busy;
            cmbPipIndex.Enabled = !busy;
            chkSkipBrowser.Enabled = !busy;
            chkSkipComputerUse.Enabled = !busy;
            chkBackup.Enabled = !busy;
            chkHostsFallback.Enabled = !busy;
            btnCancel.Enabled = busy;
            if (!busy)
            {
                UpdateButtons();
            }
        }

        private void UpdateButtons()
        {
            bool cli = HermesPaths.CliLauncherPresent;
            bool desktop = HermesPaths.DesktopBuilt;

            lblCliState.Text = cli ? "状态: 已安装" : "状态: 未安装";
            lblCliState.ForeColor = cli ? Color.SeaGreen : Color.DimGray;
            lblDesktopState.Text = desktop ? "状态: 已构建" : "状态: 未构建";
            lblDesktopState.ForeColor = desktop ? Color.SeaGreen : Color.DimGray;

            if (_busy)
            {
                return;
            }

            btnCliRepair.Enabled = cli;
            btnCliUpgrade.Enabled = cli;
            btnCliUninstall.Enabled = cli;
            btnCliLaunch.Enabled = cli;
            btnDesktopUpgrade.Enabled = cli;
            btnDesktopLaunch.Enabled = desktop;
        }

        private void CancelOperation()
        {
            if (_cts != null && !_cts.IsCancellationRequested)
            {
                AppendLog(LogLevel.Warn, "正在取消操作…");
                _cts.Cancel();
            }
        }

        // ---------------------------------------------------------------- actions

        private void LaunchCli()
        {
            try
            {
                var engine = new InstallerEngine(_settings, _logger);
                engine.LaunchCli();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Hermes 安装助手", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void LaunchDesktop()
        {
            try
            {
                var engine = new InstallerEngine(_settings, _logger);
                engine.LaunchDesktop();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Hermes 安装助手", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private async Task OfferHostsFixAsync(string failure)
        {
            if (!LooksLikeNetworkFailure(failure))
            {
                return;
            }

            DialogResult answer = MessageBox.Show(this,
                "操作因网络问题失败。\r\n\r\n是否尝试自动修复 hosts（把 GitHub 域名指向可用 IP）？\r\n" +
                "这会请求管理员权限，并在修改前备份 hosts。\r\n\r\n" +
                "建议先确认镜像设置，或使用代理后再试。",
                "网络修复",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (answer != DialogResult.Yes)
            {
                return;
            }

            if (!_settings.AllowHostsFix)
            {
                _settings.AllowHostsFix = true;
                chkHostsFallback.Checked = true;
                _settings.Save();
            }
            await FixHostsAsync();
        }

        private async Task FixHostsAsync()
        {
            if (!EnvironmentProbe.IsAdministrator())
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = Application.ExecutablePath,
                        Arguments = "--fix-hosts",
                        Verb = "runas",
                        UseShellExecute = true
                    });
                    AppendLog(LogLevel.Info, "已请求管理员权限，请在弹出窗口中完成 hosts 修复。");
                }
                catch (Win32Exception)
                {
                    AppendLog(LogLevel.Warn, "用户取消了管理员授权。");
                }
                catch (Exception ex)
                {
                    AppendLog(LogLevel.Error, "启动提权进程失败: " + ex.Message);
                }
                return;
            }

            DialogResult answer = MessageBox.Show(this,
                "将备份当前 hosts 并写入 GitHub 可用 IP。继续？",
                "修复 hosts",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (answer != DialogResult.Yes)
            {
                return;
            }

            btnFixHosts.Enabled = false;
            try
            {
                bool ok = await HostsFixer.ApplyAsync(_settings.RepoMirror, (m, l) => AppendLog(l, m));
                if (ok)
                {
                    await RefreshNetworkAsync();
                }
            }
            finally
            {
                btnFixHosts.Enabled = true;
            }
        }

        private void RestoreHosts()
        {
            if (!EnvironmentProbe.IsAdministrator())
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = Application.ExecutablePath,
                        Arguments = "--restore-hosts",
                        Verb = "runas",
                        UseShellExecute = true
                    });
                }
                catch (Win32Exception) { }
                return;
            }

            if (MessageBox.Show(this, "将用最近的备份还原 hosts。继续？", "还原 hosts",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }
            HostsFixer.RestoreLatestBackup((m, l) => AppendLog(l, m));
        }

        private void OnHostsFallbackChanged(object sender, EventArgs e)
        {
            if (chkHostsFallback.Checked && !_hostsWarningShown)
            {
                _hostsWarningShown = true;
                MessageBox.Show(this,
                    "hosts 修复是最后手段：仅在镜像全部失败时使用。\r\n" +
                    "程序会在修改前备份 hosts，并提供还原按钮。",
                    "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private static bool LooksLikeNetworkFailure(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return false;
            }
            string text = message.ToLowerInvariant();
            return text.Contains("git") || text.Contains("fetch") || text.Contains("clone") ||
                   text.Contains("timeout") || text.Contains("timed out") || text.Contains("超时") ||
                   text.Contains("网络") || text.Contains("dns") || text.Contains("connect") ||
                   text.Contains("raw.githubusercontent") || text.Contains("github") ||
                   text.Contains("ssl") || text.Contains("tls");
        }

        // ------------------------------------------------------------------ misc

        private void AppendLog(LogLevel level, string message)
        {
            if (message == null)
            {
                return;
            }
            if (IsDisposed || !IsHandleCreated)
            {
                return;
            }
            if (InvokeRequired)
            {
                try { BeginInvoke(new Action<LogLevel, string>(AppendLog), level, message); } catch { }
                return;
            }

            Color color;
            switch (level)
            {
                case LogLevel.Warn: color = Color.FromArgb(191, 96, 0); break;
                case LogLevel.Error: color = Color.Firebrick; break;
                case LogLevel.Success: color = Color.SeaGreen; break;
                case LogLevel.Stage: color = Color.RoyalBlue; break;
                default: color = Color.FromArgb(40, 40, 40); break;
            }

            if (rtbLog.TextLength > 2000000)
            {
                rtbLog.Clear();
            }

            rtbLog.SelectionStart = rtbLog.TextLength;
            rtbLog.SelectionLength = 0;
            rtbLog.SelectionColor = color;
            rtbLog.AppendText(string.Format("[{0:HH:mm:ss}] {1}\r\n", DateTime.Now, message));
            rtbLog.SelectionColor = rtbLog.ForeColor;
            rtbLog.ScrollToCaret();
        }

        private void SaveLog()
        {
            using (var dialog = new SaveFileDialog
            {
                Filter = "日志文件|*.log|文本文件|*.txt|所有文件|*.*",
                FileName = "hermes-installer-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log"
            })
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    File.WriteAllText(dialog.FileName, rtbLog.Text, new System.Text.UTF8Encoding(false));
                    MessageBox.Show(this, "日志已保存。", "Hermes 安装助手", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }

        private void OpenFolder(string path)
        {
            try
            {
                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                }
                Process.Start("explorer.exe", "\"" + path + "\"");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Hermes 安装助手", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void ShowAbout()
        {
            MessageBox.Show(this,
                "Hermes 安装助手 1.0\r\n\r\n" +
                "社区维护的非官方工具，用于安装/升级 Nous Research 的 Hermes Agent 与桌面端。\r\n" +
                "Hermes 与 Electron 均以 MIT 许可证发布。\r\n\r\n" +
                "默认使用镜像下载，不修改系统文件；hosts 修复需手动确认并提权。\r\n\r\n" +
                "官方项目: https://github.com/NousResearch/hermes-agent\r\n" +
                "官方文档: https://hermes-agent.nousresearch.com/docs",
                "关于 Hermes 安装助手",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (_busy)
            {
                DialogResult answer = MessageBox.Show(this,
                    "操作正在进行，退出会中断安装。确定退出？",
                    "Hermes 安装助手",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);
                if (answer != DialogResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }
                if (_cts != null)
                {
                    _cts.Cancel();
                }
            }

            ApplySettingsFromUi();
            _settings.Save();
            _logger.Dispose();
        }
    }
}
