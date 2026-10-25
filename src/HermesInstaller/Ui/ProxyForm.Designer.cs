namespace HermesInstaller.Ui
{
    partial class ProxyForm
    {
        private System.ComponentModel.IContainer components = null;

        private System.Windows.Forms.CheckBox _chkEnabled;
        private System.Windows.Forms.TableLayoutPanel _grid;
        private System.Windows.Forms.Label _lblHost;
        private System.Windows.Forms.TextBox _txtHost;
        private System.Windows.Forms.Label _lblPort;
        private System.Windows.Forms.TextBox _txtPort;
        private System.Windows.Forms.Label _lblUser;
        private System.Windows.Forms.TextBox _txtUser;
        private System.Windows.Forms.Label _lblPassword;
        private System.Windows.Forms.TextBox _txtPassword;
        private System.Windows.Forms.Label _lblSocks;
        private System.Windows.Forms.TextBox _txtSocksPort;
        private System.Windows.Forms.Label _lblHint;
        private System.Windows.Forms.Label _lblStatus;
        private System.Windows.Forms.Button _btnTest;
        private System.Windows.Forms.Button _btnOk;
        private System.Windows.Forms.Button _btnCancel;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this._chkEnabled = new System.Windows.Forms.CheckBox();
            this._grid = new System.Windows.Forms.TableLayoutPanel();
            this._lblHost = new System.Windows.Forms.Label();
            this._txtHost = new System.Windows.Forms.TextBox();
            this._lblPort = new System.Windows.Forms.Label();
            this._txtPort = new System.Windows.Forms.TextBox();
            this._lblUser = new System.Windows.Forms.Label();
            this._txtUser = new System.Windows.Forms.TextBox();
            this._lblPassword = new System.Windows.Forms.Label();
            this._txtPassword = new System.Windows.Forms.TextBox();
            this._lblSocks = new System.Windows.Forms.Label();
            this._txtSocksPort = new System.Windows.Forms.TextBox();
            this._lblHint = new System.Windows.Forms.Label();
            this._lblStatus = new System.Windows.Forms.Label();
            this._btnTest = new System.Windows.Forms.Button();
            this._btnOk = new System.Windows.Forms.Button();
            this._btnCancel = new System.Windows.Forms.Button();
            this._grid.SuspendLayout();
            this.SuspendLayout();
            //
            // _chkEnabled
            //
            this._chkEnabled.AutoSize = true;
            this._chkEnabled.Location = new System.Drawing.Point(16, 14);
            this._chkEnabled.Name = "_chkEnabled";
            this._chkEnabled.Size = new System.Drawing.Size(405, 21);
            this._chkEnabled.TabIndex = 0;
            this._chkEnabled.Text = "启用（安装/升级开工前自动起隧道，走不通时不影响直连）";
            this._chkEnabled.UseVisualStyleBackColor = true;
            //
            // _grid
            //
            this._grid.ColumnCount = 2;
            this._grid.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 96F));
            this._grid.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this._grid.Controls.Add(this._lblHost, 0, 0);
            this._grid.Controls.Add(this._txtHost, 1, 0);
            this._grid.Controls.Add(this._lblPort, 0, 1);
            this._grid.Controls.Add(this._txtPort, 1, 1);
            this._grid.Controls.Add(this._lblUser, 0, 2);
            this._grid.Controls.Add(this._txtUser, 1, 2);
            this._grid.Controls.Add(this._lblPassword, 0, 3);
            this._grid.Controls.Add(this._txtPassword, 1, 3);
            this._grid.Controls.Add(this._lblSocks, 0, 4);
            this._grid.Controls.Add(this._txtSocksPort, 1, 4);
            this._grid.Location = new System.Drawing.Point(16, 46);
            this._grid.Name = "_grid";
            this._grid.RowCount = 5;
            this._grid.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 30F));
            this._grid.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 30F));
            this._grid.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 30F));
            this._grid.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 30F));
            this._grid.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 30F));
            this._grid.Size = new System.Drawing.Size(454, 150);
            this._grid.TabIndex = 1;
            //
            // _lblHost
            //
            this._lblHost.AutoSize = false;
            this._lblHost.Dock = System.Windows.Forms.DockStyle.Fill;
            this._lblHost.Location = new System.Drawing.Point(3, 0);
            this._lblHost.Name = "_lblHost";
            this._lblHost.Size = new System.Drawing.Size(90, 30);
            this._lblHost.TabIndex = 0;
            this._lblHost.Text = "主机 / IP";
            this._lblHost.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            //
            // _txtHost
            //
            this._txtHost.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this._txtHost.Location = new System.Drawing.Point(99, 4);
            this._txtHost.Margin = new System.Windows.Forms.Padding(0, 3, 0, 3);
            this._txtHost.Name = "_txtHost";
            this._txtHost.Size = new System.Drawing.Size(355, 23);
            this._txtHost.TabIndex = 1;
            //
            // _lblPort
            //
            this._lblPort.AutoSize = false;
            this._lblPort.Dock = System.Windows.Forms.DockStyle.Fill;
            this._lblPort.Location = new System.Drawing.Point(3, 30);
            this._lblPort.Name = "_lblPort";
            this._lblPort.Size = new System.Drawing.Size(90, 30);
            this._lblPort.TabIndex = 2;
            this._lblPort.Text = "SSH 端口";
            this._lblPort.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            //
            // _txtPort
            //
            this._txtPort.Location = new System.Drawing.Point(99, 34);
            this._txtPort.Margin = new System.Windows.Forms.Padding(0, 3, 0, 3);
            this._txtPort.Name = "_txtPort";
            this._txtPort.Size = new System.Drawing.Size(70, 23);
            this._txtPort.TabIndex = 3;
            //
            // _lblUser
            //
            this._lblUser.AutoSize = false;
            this._lblUser.Dock = System.Windows.Forms.DockStyle.Fill;
            this._lblUser.Location = new System.Drawing.Point(3, 60);
            this._lblUser.Name = "_lblUser";
            this._lblUser.Size = new System.Drawing.Size(90, 30);
            this._lblUser.TabIndex = 4;
            this._lblUser.Text = "用户名";
            this._lblUser.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            //
            // _txtUser
            //
            this._txtUser.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this._txtUser.Location = new System.Drawing.Point(99, 64);
            this._txtUser.Margin = new System.Windows.Forms.Padding(0, 3, 0, 3);
            this._txtUser.Name = "_txtUser";
            this._txtUser.Size = new System.Drawing.Size(355, 23);
            this._txtUser.TabIndex = 5;
            //
            // _lblPassword
            //
            this._lblPassword.AutoSize = false;
            this._lblPassword.Dock = System.Windows.Forms.DockStyle.Fill;
            this._lblPassword.Location = new System.Drawing.Point(3, 90);
            this._lblPassword.Name = "_lblPassword";
            this._lblPassword.Size = new System.Drawing.Size(90, 30);
            this._lblPassword.TabIndex = 6;
            this._lblPassword.Text = "密码";
            this._lblPassword.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            //
            // _txtPassword
            //
            this._txtPassword.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this._txtPassword.Location = new System.Drawing.Point(99, 94);
            this._txtPassword.Margin = new System.Windows.Forms.Padding(0, 3, 0, 3);
            this._txtPassword.Name = "_txtPassword";
            this._txtPassword.Size = new System.Drawing.Size(355, 23);
            this._txtPassword.TabIndex = 7;
            this._txtPassword.UseSystemPasswordChar = true;
            //
            // _lblSocks
            //
            this._lblSocks.AutoSize = false;
            this._lblSocks.Dock = System.Windows.Forms.DockStyle.Fill;
            this._lblSocks.Location = new System.Drawing.Point(3, 120);
            this._lblSocks.Name = "_lblSocks";
            this._lblSocks.Size = new System.Drawing.Size(90, 30);
            this._lblSocks.TabIndex = 8;
            this._lblSocks.Text = "SOCKS 端口";
            this._lblSocks.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            //
            // _txtSocksPort
            //
            this._txtSocksPort.Location = new System.Drawing.Point(99, 124);
            this._txtSocksPort.Margin = new System.Windows.Forms.Padding(0, 3, 0, 3);
            this._txtSocksPort.Name = "_txtSocksPort";
            this._txtSocksPort.Size = new System.Drawing.Size(70, 23);
            this._txtSocksPort.TabIndex = 9;
            //
            // _lblHint
            //
            this._lblHint.AutoSize = false;
            this._lblHint.ForeColor = System.Drawing.Color.DimGray;
            this._lblHint.Location = new System.Drawing.Point(16, 200);
            this._lblHint.Name = "_lblHint";
            this._lblHint.Size = new System.Drawing.Size(454, 32);
            this._lblHint.TabIndex = 2;
            this._lblHint.Text = "密码仅用于首次把公钥装到跳板机，之后走密钥免密登录；密码以当前用户 DPAPI 加密保存。";
            //
            // _lblStatus
            //
            this._lblStatus.AutoSize = false;
            this._lblStatus.ForeColor = System.Drawing.Color.DimGray;
            this._lblStatus.Location = new System.Drawing.Point(16, 232);
            this._lblStatus.Name = "_lblStatus";
            this._lblStatus.Size = new System.Drawing.Size(454, 20);
            this._lblStatus.TabIndex = 3;
            this._lblStatus.Text = "未测试";
            //
            // _btnTest
            //
            this._btnTest.Location = new System.Drawing.Point(16, 256);
            this._btnTest.Name = "_btnTest";
            this._btnTest.Size = new System.Drawing.Size(96, 27);
            this._btnTest.TabIndex = 4;
            this._btnTest.Text = "测试连接";
            this._btnTest.UseVisualStyleBackColor = true;
            //
            // _btnOk
            //
            this._btnOk.Location = new System.Drawing.Point(306, 256);
            this._btnOk.Name = "_btnOk";
            this._btnOk.Size = new System.Drawing.Size(75, 27);
            this._btnOk.TabIndex = 5;
            this._btnOk.Text = "保存";
            this._btnOk.UseVisualStyleBackColor = true;
            //
            // _btnCancel
            //
            this._btnCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this._btnCancel.Location = new System.Drawing.Point(389, 256);
            this._btnCancel.Name = "_btnCancel";
            this._btnCancel.Size = new System.Drawing.Size(75, 27);
            this._btnCancel.TabIndex = 6;
            this._btnCancel.Text = "取消";
            this._btnCancel.UseVisualStyleBackColor = true;
            //
            // ProxyForm
            //
            this.AcceptButton = this._btnOk;
            this.CancelButton = this._btnCancel;
            this.ClientSize = new System.Drawing.Size(486, 292);
            this.Controls.Add(this._chkEnabled);
            this.Controls.Add(this._grid);
            this.Controls.Add(this._lblHint);
            this.Controls.Add(this._lblStatus);
            this.Controls.Add(this._btnTest);
            this.Controls.Add(this._btnOk);
            this.Controls.Add(this._btnCancel);
            this.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "ProxyForm";
            this.ShowInTaskbar = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "SSH 代理设置";
            this._grid.ResumeLayout(false);
            this._grid.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();
        }
    }
}
