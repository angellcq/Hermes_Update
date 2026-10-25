using System;
using System.ComponentModel;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using HermesInstaller.Core;

namespace HermesInstaller.Ui
{
    /// <summary>
    /// E14：SSH 代理配置对话框（独立文件，不往 1,345 行的 MainForm 里继续堆控件）。
    /// 只收「跳板机 + 端口 + 账号 + 密码」，密码用 DPAPI 加密后随 settings.json 落盘；
    /// 「测试连接」会真实起一次隧道并取出口 IP，验证完立刻收摊，不影响当前会话状态。
    /// </summary>
    public partial class ProxyForm : Form
    {
        private Settings _settings;
        private Logger _logger;

        public ProxyForm()
        {
            InitializeComponent();
            if (LicenseManager.UsageMode == LicenseUsageMode.Designtime)
            {
                return;
            }
            _btnTest.Click += async (s, e) => await TestAsync();
            _btnOk.Click += (s, e) => SaveValues();
        }

        /// <summary>打开配置对话框；返回 true 表示用户点了保存（值已写回 settings，未落盘）。</summary>
        public static bool Edit(IWin32Window owner, Settings settings, Logger logger)
        {
            using (var form = new ProxyForm())
            {
                form._settings = settings;
                form._logger = logger;
                form.LoadValues();
                return form.ShowDialog(owner) == DialogResult.OK;
            }
        }

        private void LoadValues()
        {
            _chkEnabled.Checked = _settings.SshProxyEnabled;
            _txtHost.Text = _settings.SshHost ?? string.Empty;
            _txtPort.Text = _settings.SshPort.ToString();
            _txtUser.Text = string.IsNullOrEmpty(_settings.SshUser) ? "root" : _settings.SshUser;
            _txtPassword.Text = _settings.GetSshPassword();
            _txtSocksPort.Text = _settings.SshSocksPort.ToString();
        }

        private void SaveValues()
        {
            string host = (_txtHost.Text ?? string.Empty).Trim();
            string user = (_txtUser.Text ?? string.Empty).Trim();
            int port;
            int socksPort;
            if (_chkEnabled.Checked && (host.Length == 0 || user.Length == 0))
            {
                MessageBox.Show(this, "启用代理时主机与用户名不能为空。", Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!int.TryParse((_txtPort.Text ?? string.Empty).Trim(), out port) || port <= 0 || port > 65535)
            {
                MessageBox.Show(this, "SSH 端口必须是 1–65535 的整数。", Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!int.TryParse((_txtSocksPort.Text ?? string.Empty).Trim(), out socksPort) || socksPort <= 0 || socksPort > 65535)
            {
                MessageBox.Show(this, "SOCKS 端口必须是 1–65535 的整数。", Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _settings.SshProxyEnabled = _chkEnabled.Checked;
            _settings.SshHost = host;
            _settings.SshPort = port;
            _settings.SshUser = user;
            _settings.SshSocksPort = socksPort;
            _settings.SshHttpPort = socksPort < 65535 ? socksPort + 1 : 1081;
            _settings.SetSshPassword((_txtPassword.Text ?? string.Empty).Trim());
            DialogResult = DialogResult.OK;
            Close();
        }

        private async Task TestAsync()
        {
            string host = (_txtHost.Text ?? string.Empty).Trim();
            string user = (_txtUser.Text ?? string.Empty).Trim();
            int port;
            int socksPort;
            if (host.Length == 0 || user.Length == 0
                || !int.TryParse((_txtPort.Text ?? string.Empty).Trim(), out port)
                || !int.TryParse((_txtSocksPort.Text ?? string.Empty).Trim(), out socksPort))
            {
                _lblStatus.ForeColor = Color.Firebrick;
                _lblStatus.Text = "请先填好主机、用户名、SSH 端口与 SOCKS 端口。";
                return;
            }

            _btnTest.Enabled = false;
            _btnOk.Enabled = false;
            _lblStatus.ForeColor = Color.DimGray;
            _lblStatus.Text = "正在起隧道（首次会用密码装公钥，约 10–30 秒）…";

            var probe = new Settings
            {
                SshProxyEnabled = true,
                SshHost = host,
                SshPort = port,
                SshUser = user,
                SshSocksPort = socksPort,
                SshHttpPort = socksPort < 65535 ? socksPort + 1 : 1081
            };
            probe.SetSshPassword((_txtPassword.Text ?? string.Empty).Trim());

            try
            {
                ProxyRuntime.Stop();
                bool ok = await ProxyRuntime.EnsureStartedAsync(probe, _logger, System.Threading.CancellationToken.None);
                if (!ok)
                {
                    _lblStatus.ForeColor = Color.Firebrick;
                    _lblStatus.Text = "失败: " + ProxyRuntime.LastError;
                    return;
                }
                string exitIp = await System.Threading.Tasks.Task.Run(() => ProxyRuntime.TestExitIp());
                _lblStatus.ForeColor = Color.SeaGreen;
                _lblStatus.Text = "成功 — " + exitIp;
            }
            catch (Exception ex)
            {
                _lblStatus.ForeColor = Color.Firebrick;
                _lblStatus.Text = "失败: " + ex.Message;
            }
            finally
            {
                ProxyRuntime.Stop();
                _btnTest.Enabled = true;
                _btnOk.Enabled = true;
            }
        }
    }
}
