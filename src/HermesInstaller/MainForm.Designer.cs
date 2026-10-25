namespace HermesInstaller
{
    partial class MainForm
    {
        private System.ComponentModel.IContainer components = null;

        private System.Windows.Forms.TableLayoutPanel rootLayout;
        private System.Windows.Forms.TableLayoutPanel headerPanel;
        private System.Windows.Forms.Label lblTitle;
        private System.Windows.Forms.Label lblSubtitle;
        private System.Windows.Forms.Label lblStatus;

        private System.Windows.Forms.SplitContainer splitMain;

        private System.Windows.Forms.TableLayoutPanel leftLayout;
        private System.Windows.Forms.FlowLayoutPanel toolbar;
        private System.Windows.Forms.Button btnRefresh;
        private System.Windows.Forms.Button btnNetwork;
        private System.Windows.Forms.Label lblLatest;
        private System.Windows.Forms.TabControl tabs;
        private System.Windows.Forms.TabPage envTab;
        private System.Windows.Forms.TabPage netTab;
        private System.Windows.Forms.DataGridView dgvProbe;
        private System.Windows.Forms.DataGridView dgvNetwork;
        private System.Windows.Forms.DataGridViewTextBoxColumn colProbeCategory;
        private System.Windows.Forms.DataGridViewTextBoxColumn colProbeItem;
        private System.Windows.Forms.DataGridViewTextBoxColumn colProbeExpected;
        private System.Windows.Forms.DataGridViewTextBoxColumn colProbeActual;
        private System.Windows.Forms.DataGridViewTextBoxColumn colProbeStatus;
        private System.Windows.Forms.DataGridViewTextBoxColumn colProbeAdvice;
        private System.Windows.Forms.DataGridViewTextBoxColumn colNetworkCategory;
        private System.Windows.Forms.DataGridViewTextBoxColumn colNetworkName;
        private System.Windows.Forms.DataGridViewTextBoxColumn colNetworkUrl;
        private System.Windows.Forms.DataGridViewTextBoxColumn colNetworkStatus;
        private System.Windows.Forms.DataGridViewTextBoxColumn colNetworkLatency;
        private System.Windows.Forms.DataGridViewTextBoxColumn colNetworkDetail;

        private System.Windows.Forms.Panel rightScroll;
        private System.Windows.Forms.TableLayoutPanel rightLayout;

        private System.Windows.Forms.GroupBox cliGroup;
        private System.Windows.Forms.TableLayoutPanel cliLayout;
        private System.Windows.Forms.Label lblCliState;
        private System.Windows.Forms.FlowLayoutPanel cliButtons;
        private System.Windows.Forms.Button btnCliInstall;
        private System.Windows.Forms.Button btnCliRepair;
        private System.Windows.Forms.Button btnCliUpgrade;
        private System.Windows.Forms.Button btnCliUninstall;
        private System.Windows.Forms.Button btnCliLaunch;

        private System.Windows.Forms.GroupBox desktopGroup;
        private System.Windows.Forms.TableLayoutPanel desktopLayout;
        private System.Windows.Forms.Label lblDesktopState;
        private System.Windows.Forms.FlowLayoutPanel desktopButtons;
        private System.Windows.Forms.Button btnDesktopInstall;
        private System.Windows.Forms.Button btnDesktopRepair;
        private System.Windows.Forms.Button btnDesktopUpgrade;
        private System.Windows.Forms.Button btnDesktopLaunch;

        private System.Windows.Forms.GroupBox optionsGroup;
        private System.Windows.Forms.TableLayoutPanel optLayout;
        private System.Windows.Forms.Label lblCapChannel;
        private System.Windows.Forms.ComboBox cmbChannel;
        private System.Windows.Forms.Label lblCapRepo;
        private System.Windows.Forms.ComboBox cmbRepoMirror;
        private System.Windows.Forms.Label lblCapElectron;
        private System.Windows.Forms.ComboBox cmbElectronMirror;
        private System.Windows.Forms.Label lblCapBuilder;
        private System.Windows.Forms.ComboBox cmbBuilderMirror;
        private System.Windows.Forms.Label lblCapNpm;
        private System.Windows.Forms.ComboBox cmbNpmRegistry;
        private System.Windows.Forms.Label lblCapPip;
        private System.Windows.Forms.ComboBox cmbPipIndex;
        private System.Windows.Forms.CheckBox chkSkipBrowser;
        private System.Windows.Forms.CheckBox chkSkipComputerUse;
        private System.Windows.Forms.CheckBox chkBackup;
        private System.Windows.Forms.CheckBox chkHostsFallback;
        private System.Windows.Forms.FlowLayoutPanel optButtons;
        private System.Windows.Forms.Button btnApplyMirrors;
        private System.Windows.Forms.Button btnFixHosts;
        private System.Windows.Forms.Button btnRestoreHosts;
        private System.Windows.Forms.Button btnProxy;

        private System.Windows.Forms.Label hintLabel;

        private System.Windows.Forms.TableLayoutPanel bottomLayout;
        private System.Windows.Forms.TableLayoutPanel progressPanel;
        private System.Windows.Forms.ProgressBar pb;
        private System.Windows.Forms.Label lblProgress;
        private System.Windows.Forms.RichTextBox rtbLog;
        private System.Windows.Forms.FlowLayoutPanel buttonBar;
        private System.Windows.Forms.Button btnAbout;
        private System.Windows.Forms.Button btnOpenData;
        private System.Windows.Forms.Button btnSaveLog;
        private System.Windows.Forms.Button btnOpenLogs;
        private System.Windows.Forms.Button btnCancel;

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
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle1 = new System.Windows.Forms.DataGridViewCellStyle();
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle2 = new System.Windows.Forms.DataGridViewCellStyle();
            this.rootLayout = new System.Windows.Forms.TableLayoutPanel();
            this.headerPanel = new System.Windows.Forms.TableLayoutPanel();
            this.lblTitle = new System.Windows.Forms.Label();
            this.lblSubtitle = new System.Windows.Forms.Label();
            this.lblStatus = new System.Windows.Forms.Label();
            this.splitMain = new System.Windows.Forms.SplitContainer();
            this.leftLayout = new System.Windows.Forms.TableLayoutPanel();
            this.toolbar = new System.Windows.Forms.FlowLayoutPanel();
            this.btnRefresh = new System.Windows.Forms.Button();
            this.btnNetwork = new System.Windows.Forms.Button();
            this.lblLatest = new System.Windows.Forms.Label();
            this.tabs = new System.Windows.Forms.TabControl();
            this.envTab = new System.Windows.Forms.TabPage();
            this.dgvProbe = new System.Windows.Forms.DataGridView();
            this.colProbeCategory = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colProbeItem = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colProbeExpected = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colProbeActual = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colProbeStatus = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colProbeAdvice = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.netTab = new System.Windows.Forms.TabPage();
            this.dgvNetwork = new System.Windows.Forms.DataGridView();
            this.colNetworkCategory = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colNetworkName = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colNetworkUrl = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colNetworkStatus = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colNetworkLatency = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colNetworkDetail = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.rightScroll = new System.Windows.Forms.Panel();
            this.rightLayout = new System.Windows.Forms.TableLayoutPanel();
            this.cliGroup = new System.Windows.Forms.GroupBox();
            this.cliLayout = new System.Windows.Forms.TableLayoutPanel();
            this.lblCliState = new System.Windows.Forms.Label();
            this.cliButtons = new System.Windows.Forms.FlowLayoutPanel();
            this.btnCliInstall = new System.Windows.Forms.Button();
            this.btnCliRepair = new System.Windows.Forms.Button();
            this.btnCliUpgrade = new System.Windows.Forms.Button();
            this.btnCliUninstall = new System.Windows.Forms.Button();
            this.btnCliLaunch = new System.Windows.Forms.Button();
            this.desktopGroup = new System.Windows.Forms.GroupBox();
            this.desktopLayout = new System.Windows.Forms.TableLayoutPanel();
            this.lblDesktopState = new System.Windows.Forms.Label();
            this.desktopButtons = new System.Windows.Forms.FlowLayoutPanel();
            this.btnDesktopInstall = new System.Windows.Forms.Button();
            this.btnDesktopRepair = new System.Windows.Forms.Button();
            this.btnDesktopUpgrade = new System.Windows.Forms.Button();
            this.btnDesktopLaunch = new System.Windows.Forms.Button();
            this.optionsGroup = new System.Windows.Forms.GroupBox();
            this.optLayout = new System.Windows.Forms.TableLayoutPanel();
            this.lblCapChannel = new System.Windows.Forms.Label();
            this.cmbChannel = new System.Windows.Forms.ComboBox();
            this.lblCapRepo = new System.Windows.Forms.Label();
            this.cmbRepoMirror = new System.Windows.Forms.ComboBox();
            this.lblCapElectron = new System.Windows.Forms.Label();
            this.cmbElectronMirror = new System.Windows.Forms.ComboBox();
            this.lblCapBuilder = new System.Windows.Forms.Label();
            this.cmbBuilderMirror = new System.Windows.Forms.ComboBox();
            this.lblCapNpm = new System.Windows.Forms.Label();
            this.cmbNpmRegistry = new System.Windows.Forms.ComboBox();
            this.lblCapPip = new System.Windows.Forms.Label();
            this.cmbPipIndex = new System.Windows.Forms.ComboBox();
            this.chkSkipBrowser = new System.Windows.Forms.CheckBox();
            this.chkSkipComputerUse = new System.Windows.Forms.CheckBox();
            this.chkBackup = new System.Windows.Forms.CheckBox();
            this.chkHostsFallback = new System.Windows.Forms.CheckBox();
            this.optButtons = new System.Windows.Forms.FlowLayoutPanel();
            this.btnApplyMirrors = new System.Windows.Forms.Button();
            this.btnFixHosts = new System.Windows.Forms.Button();
            this.btnRestoreHosts = new System.Windows.Forms.Button();
            this.btnProxy = new System.Windows.Forms.Button();
            this.hintLabel = new System.Windows.Forms.Label();
            this.bottomLayout = new System.Windows.Forms.TableLayoutPanel();
            this.progressPanel = new System.Windows.Forms.TableLayoutPanel();
            this.pb = new System.Windows.Forms.ProgressBar();
            this.lblProgress = new System.Windows.Forms.Label();
            this.rtbLog = new System.Windows.Forms.RichTextBox();
            this.buttonBar = new System.Windows.Forms.FlowLayoutPanel();
            this.btnAbout = new System.Windows.Forms.Button();
            this.btnOpenData = new System.Windows.Forms.Button();
            this.btnSaveLog = new System.Windows.Forms.Button();
            this.btnOpenLogs = new System.Windows.Forms.Button();
            this.btnCancel = new System.Windows.Forms.Button();
            this.rootLayout.SuspendLayout();
            this.headerPanel.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.splitMain)).BeginInit();
            this.splitMain.Panel1.SuspendLayout();
            this.splitMain.Panel2.SuspendLayout();
            this.splitMain.SuspendLayout();
            this.leftLayout.SuspendLayout();
            this.toolbar.SuspendLayout();
            this.tabs.SuspendLayout();
            this.envTab.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.dgvProbe)).BeginInit();
            this.netTab.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.dgvNetwork)).BeginInit();
            this.rightScroll.SuspendLayout();
            this.rightLayout.SuspendLayout();
            this.cliGroup.SuspendLayout();
            this.cliLayout.SuspendLayout();
            this.cliButtons.SuspendLayout();
            this.desktopGroup.SuspendLayout();
            this.desktopLayout.SuspendLayout();
            this.desktopButtons.SuspendLayout();
            this.optionsGroup.SuspendLayout();
            this.optLayout.SuspendLayout();
            this.optButtons.SuspendLayout();
            this.bottomLayout.SuspendLayout();
            this.progressPanel.SuspendLayout();
            this.buttonBar.SuspendLayout();
            this.SuspendLayout();
            // 
            // rootLayout
            // 
            this.rootLayout.ColumnCount = 1;
            this.rootLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.rootLayout.Controls.Add(this.headerPanel, 0, 0);
            this.rootLayout.Controls.Add(this.splitMain, 0, 1);
            this.rootLayout.Controls.Add(this.bottomLayout, 0, 2);
            this.rootLayout.Dock = System.Windows.Forms.DockStyle.Fill;
            this.rootLayout.Location = new System.Drawing.Point(0, 0);
            this.rootLayout.Margin = new System.Windows.Forms.Padding(0);
            this.rootLayout.Name = "rootLayout";
            this.rootLayout.RowCount = 3;
            this.rootLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 66F));
            this.rootLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 56F));
            this.rootLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 44F));
            this.rootLayout.Size = new System.Drawing.Size(1200, 860);
            this.rootLayout.TabIndex = 0;
            // 
            // headerPanel
            // 
            this.headerPanel.ColumnCount = 2;
            this.headerPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.headerPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            this.headerPanel.Controls.Add(this.lblTitle, 0, 0);
            this.headerPanel.Controls.Add(this.lblSubtitle, 0, 1);
            this.headerPanel.Controls.Add(this.lblStatus, 1, 0);
            this.headerPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.headerPanel.Location = new System.Drawing.Point(0, 0);
            this.headerPanel.Margin = new System.Windows.Forms.Padding(0);
            this.headerPanel.Name = "headerPanel";
            this.headerPanel.Padding = new System.Windows.Forms.Padding(14, 6, 14, 4);
            this.headerPanel.RowCount = 2;
            this.headerPanel.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.headerPanel.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.headerPanel.Size = new System.Drawing.Size(1200, 66);
            this.headerPanel.TabIndex = 0;
            // 
            // lblTitle
            // 
            this.lblTitle.AutoSize = true;
            this.lblTitle.Font = new System.Drawing.Font("Microsoft YaHei UI", 17F, System.Drawing.FontStyle.Bold);
            this.lblTitle.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(38)))), ((int)(((byte)(38)))), ((int)(((byte)(38)))));
            this.lblTitle.Location = new System.Drawing.Point(17, 9);
            this.lblTitle.Margin = new System.Windows.Forms.Padding(3);
            this.lblTitle.Name = "lblTitle";
            this.lblTitle.Size = new System.Drawing.Size(202, 31);
            this.lblTitle.TabIndex = 0;
            this.lblTitle.Text = "Hermes 安装助手";
            // 
            // lblSubtitle
            // 
            this.lblSubtitle.AutoSize = true;
            this.lblSubtitle.ForeColor = System.Drawing.Color.DimGray;
            this.lblSubtitle.Location = new System.Drawing.Point(16, 45);
            this.lblSubtitle.Margin = new System.Windows.Forms.Padding(2, 2, 0, 0);
            this.lblSubtitle.Name = "lblSubtitle";
            this.lblSubtitle.Size = new System.Drawing.Size(332, 17);
            this.lblSubtitle.TabIndex = 1;
            this.lblSubtitle.Text = "Hermes CLI + 桌面端 · 新装 / 重装 / 升级 · 环境与镜像检测";
            // 
            // lblStatus
            // 
            this.lblStatus.Anchor = System.Windows.Forms.AnchorStyles.Right;
            this.lblStatus.AutoSize = true;
            this.lblStatus.Font = new System.Drawing.Font("Microsoft YaHei UI", 11F, System.Drawing.FontStyle.Bold);
            this.lblStatus.ForeColor = System.Drawing.Color.DimGray;
            this.lblStatus.Location = new System.Drawing.Point(1118, 27);
            this.lblStatus.Margin = new System.Windows.Forms.Padding(12, 6, 0, 0);
            this.lblStatus.Name = "lblStatus";
            this.headerPanel.SetRowSpan(this.lblStatus, 2);
            this.lblStatus.Size = new System.Drawing.Size(68, 19);
            this.lblStatus.TabIndex = 2;
            this.lblStatus.Text = "检测中…";
            this.lblStatus.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            // 
            // splitMain
            // 
            this.splitMain.Dock = System.Windows.Forms.DockStyle.Fill;
            this.splitMain.Location = new System.Drawing.Point(0, 66);
            this.splitMain.Margin = new System.Windows.Forms.Padding(0);
            this.splitMain.Name = "splitMain";
            // 
            // splitMain.Panel1
            // 
            this.splitMain.Panel1.Controls.Add(this.leftLayout);
            // 
            // splitMain.Panel2
            // 
            this.splitMain.Panel2.Controls.Add(this.rightScroll);
            this.splitMain.Size = new System.Drawing.Size(1200, 444);
            this.splitMain.SplitterDistance = 696;
            this.splitMain.TabIndex = 1;
            // 
            // leftLayout
            // 
            this.leftLayout.ColumnCount = 1;
            this.leftLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.leftLayout.Controls.Add(this.toolbar, 0, 0);
            this.leftLayout.Controls.Add(this.tabs, 0, 1);
            this.leftLayout.Dock = System.Windows.Forms.DockStyle.Fill;
            this.leftLayout.Location = new System.Drawing.Point(0, 0);
            this.leftLayout.Name = "leftLayout";
            this.leftLayout.Padding = new System.Windows.Forms.Padding(10, 4, 6, 8);
            this.leftLayout.RowCount = 2;
            this.leftLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 34F));
            this.leftLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.leftLayout.Size = new System.Drawing.Size(696, 444);
            this.leftLayout.TabIndex = 0;
            // 
            // toolbar
            // 
            this.toolbar.Controls.Add(this.btnRefresh);
            this.toolbar.Controls.Add(this.btnNetwork);
            this.toolbar.Controls.Add(this.lblLatest);
            this.toolbar.Dock = System.Windows.Forms.DockStyle.Fill;
            this.toolbar.Location = new System.Drawing.Point(10, 4);
            this.toolbar.Margin = new System.Windows.Forms.Padding(0);
            this.toolbar.Name = "toolbar";
            this.toolbar.Size = new System.Drawing.Size(680, 34);
            this.toolbar.TabIndex = 0;
            this.toolbar.WrapContents = false;
            // 
            // btnRefresh
            // 
            this.btnRefresh.Location = new System.Drawing.Point(0, 0);
            this.btnRefresh.Margin = new System.Windows.Forms.Padding(0, 0, 8, 8);
            this.btnRefresh.Name = "btnRefresh";
            this.btnRefresh.Size = new System.Drawing.Size(96, 30);
            this.btnRefresh.TabIndex = 0;
            this.btnRefresh.Text = "重新检测";
            this.btnRefresh.UseVisualStyleBackColor = true;
            // 
            // btnNetwork
            // 
            this.btnNetwork.Location = new System.Drawing.Point(104, 0);
            this.btnNetwork.Margin = new System.Windows.Forms.Padding(0, 0, 8, 8);
            this.btnNetwork.Name = "btnNetwork";
            this.btnNetwork.Size = new System.Drawing.Size(96, 30);
            this.btnNetwork.TabIndex = 1;
            this.btnNetwork.Text = "检测镜像";
            this.btnNetwork.UseVisualStyleBackColor = true;
            // 
            // lblLatest
            // 
            this.lblLatest.AutoSize = true;
            this.lblLatest.ForeColor = System.Drawing.Color.DimGray;
            this.lblLatest.Location = new System.Drawing.Point(218, 8);
            this.lblLatest.Margin = new System.Windows.Forms.Padding(10, 8, 0, 0);
            this.lblLatest.Name = "lblLatest";
            this.lblLatest.Size = new System.Drawing.Size(68, 17);
            this.lblLatest.TabIndex = 2;
            this.lblLatest.Text = "官方最新: -";
            // 
            // tabs
            // 
            this.tabs.Controls.Add(this.envTab);
            this.tabs.Controls.Add(this.netTab);
            this.tabs.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tabs.Location = new System.Drawing.Point(13, 41);
            this.tabs.Name = "tabs";
            this.tabs.SelectedIndex = 0;
            this.tabs.Size = new System.Drawing.Size(674, 392);
            this.tabs.TabIndex = 1;
            // 
            // envTab
            // 
            this.envTab.BackColor = System.Drawing.Color.White;
            this.envTab.Controls.Add(this.dgvProbe);
            this.envTab.Location = new System.Drawing.Point(4, 26);
            this.envTab.Name = "envTab";
            this.envTab.Padding = new System.Windows.Forms.Padding(3);
            this.envTab.Size = new System.Drawing.Size(666, 362);
            this.envTab.TabIndex = 0;
            this.envTab.Text = "环境检测";
            // 
            // dgvProbe
            // 
            this.dgvProbe.AllowUserToAddRows = false;
            this.dgvProbe.AllowUserToDeleteRows = false;
            this.dgvProbe.AllowUserToResizeRows = false;
            this.dgvProbe.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.Fill;
            this.dgvProbe.BackgroundColor = System.Drawing.Color.White;
            this.dgvProbe.BorderStyle = System.Windows.Forms.BorderStyle.None;
            dataGridViewCellStyle1.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F);
            this.dgvProbe.ColumnHeadersDefaultCellStyle = dataGridViewCellStyle1;
            this.dgvProbe.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.colProbeCategory,
            this.colProbeItem,
            this.colProbeExpected,
            this.colProbeActual,
            this.colProbeStatus,
            this.colProbeAdvice});
            this.dgvProbe.Dock = System.Windows.Forms.DockStyle.Fill;
            this.dgvProbe.Location = new System.Drawing.Point(3, 3);
            this.dgvProbe.MultiSelect = false;
            this.dgvProbe.Name = "dgvProbe";
            this.dgvProbe.ReadOnly = true;
            this.dgvProbe.RowHeadersVisible = false;
            this.dgvProbe.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            this.dgvProbe.Size = new System.Drawing.Size(660, 356);
            this.dgvProbe.TabIndex = 0;
            // 
            // colProbeCategory
            // 
            this.colProbeCategory.FillWeight = 8F;
            this.colProbeCategory.HeaderText = "类别";
            this.colProbeCategory.Name = "colProbeCategory";
            this.colProbeCategory.ReadOnly = true;
            this.colProbeCategory.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            // 
            // colProbeItem
            // 
            this.colProbeItem.FillWeight = 16F;
            this.colProbeItem.HeaderText = "检查项";
            this.colProbeItem.Name = "colProbeItem";
            this.colProbeItem.ReadOnly = true;
            this.colProbeItem.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            // 
            // colProbeExpected
            // 
            this.colProbeExpected.FillWeight = 20F;
            this.colProbeExpected.HeaderText = "期望";
            this.colProbeExpected.Name = "colProbeExpected";
            this.colProbeExpected.ReadOnly = true;
            this.colProbeExpected.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            // 
            // colProbeActual
            // 
            this.colProbeActual.FillWeight = 24F;
            this.colProbeActual.HeaderText = "实际";
            this.colProbeActual.Name = "colProbeActual";
            this.colProbeActual.ReadOnly = true;
            this.colProbeActual.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            // 
            // colProbeStatus
            // 
            this.colProbeStatus.FillWeight = 8F;
            this.colProbeStatus.HeaderText = "状态";
            this.colProbeStatus.Name = "colProbeStatus";
            this.colProbeStatus.ReadOnly = true;
            this.colProbeStatus.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            // 
            // colProbeAdvice
            // 
            this.colProbeAdvice.FillWeight = 30F;
            this.colProbeAdvice.HeaderText = "建议";
            this.colProbeAdvice.Name = "colProbeAdvice";
            this.colProbeAdvice.ReadOnly = true;
            this.colProbeAdvice.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            // 
            // netTab
            // 
            this.netTab.BackColor = System.Drawing.Color.White;
            this.netTab.Controls.Add(this.dgvNetwork);
            this.netTab.Location = new System.Drawing.Point(4, 22);
            this.netTab.Name = "netTab";
            this.netTab.Padding = new System.Windows.Forms.Padding(3);
            this.netTab.Size = new System.Drawing.Size(666, 404);
            this.netTab.TabIndex = 1;
            this.netTab.Text = "镜像检测";
            // 
            // dgvNetwork
            // 
            this.dgvNetwork.AllowUserToAddRows = false;
            this.dgvNetwork.AllowUserToDeleteRows = false;
            this.dgvNetwork.AllowUserToResizeRows = false;
            this.dgvNetwork.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.Fill;
            this.dgvNetwork.BackgroundColor = System.Drawing.Color.White;
            this.dgvNetwork.BorderStyle = System.Windows.Forms.BorderStyle.None;
            dataGridViewCellStyle2.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F);
            this.dgvNetwork.ColumnHeadersDefaultCellStyle = dataGridViewCellStyle2;
            this.dgvNetwork.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.colNetworkCategory,
            this.colNetworkName,
            this.colNetworkUrl,
            this.colNetworkStatus,
            this.colNetworkLatency,
            this.colNetworkDetail});
            this.dgvNetwork.Dock = System.Windows.Forms.DockStyle.Fill;
            this.dgvNetwork.Location = new System.Drawing.Point(3, 3);
            this.dgvNetwork.MultiSelect = false;
            this.dgvNetwork.Name = "dgvNetwork";
            this.dgvNetwork.ReadOnly = true;
            this.dgvNetwork.RowHeadersVisible = false;
            this.dgvNetwork.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            this.dgvNetwork.Size = new System.Drawing.Size(660, 398);
            this.dgvNetwork.TabIndex = 0;
            // 
            // colNetworkCategory
            // 
            this.colNetworkCategory.FillWeight = 8F;
            this.colNetworkCategory.HeaderText = "类别";
            this.colNetworkCategory.Name = "colNetworkCategory";
            this.colNetworkCategory.ReadOnly = true;
            this.colNetworkCategory.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            // 
            // colNetworkName
            // 
            this.colNetworkName.FillWeight = 18F;
            this.colNetworkName.HeaderText = "用途";
            this.colNetworkName.Name = "colNetworkName";
            this.colNetworkName.ReadOnly = true;
            this.colNetworkName.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            // 
            // colNetworkUrl
            // 
            this.colNetworkUrl.FillWeight = 38F;
            this.colNetworkUrl.HeaderText = "端点";
            this.colNetworkUrl.Name = "colNetworkUrl";
            this.colNetworkUrl.ReadOnly = true;
            this.colNetworkUrl.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            // 
            // colNetworkStatus
            // 
            this.colNetworkStatus.FillWeight = 8F;
            this.colNetworkStatus.HeaderText = "状态";
            this.colNetworkStatus.Name = "colNetworkStatus";
            this.colNetworkStatus.ReadOnly = true;
            this.colNetworkStatus.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            // 
            // colNetworkLatency
            // 
            this.colNetworkLatency.FillWeight = 8F;
            this.colNetworkLatency.HeaderText = "延迟";
            this.colNetworkLatency.Name = "colNetworkLatency";
            this.colNetworkLatency.ReadOnly = true;
            this.colNetworkLatency.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            // 
            // colNetworkDetail
            // 
            this.colNetworkDetail.FillWeight = 26F;
            this.colNetworkDetail.HeaderText = "说明";
            this.colNetworkDetail.Name = "colNetworkDetail";
            this.colNetworkDetail.ReadOnly = true;
            this.colNetworkDetail.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            // 
            // rightScroll
            // 
            this.rightScroll.AutoScroll = true;
            this.rightScroll.Controls.Add(this.rightLayout);
            this.rightScroll.Dock = System.Windows.Forms.DockStyle.Fill;
            this.rightScroll.Location = new System.Drawing.Point(0, 0);
            this.rightScroll.Name = "rightScroll";
            this.rightScroll.Padding = new System.Windows.Forms.Padding(0, 4, 8, 8);
            this.rightScroll.Size = new System.Drawing.Size(500, 444);
            this.rightScroll.TabIndex = 0;
            // 
            // rightLayout
            // 
            this.rightLayout.AutoSize = true;
            this.rightLayout.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.rightLayout.ColumnCount = 1;
            this.rightLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.rightLayout.Controls.Add(this.cliGroup, 0, 0);
            this.rightLayout.Controls.Add(this.desktopGroup, 0, 1);
            this.rightLayout.Controls.Add(this.optionsGroup, 0, 2);
            this.rightLayout.Controls.Add(this.hintLabel, 0, 3);
            this.rightLayout.Dock = System.Windows.Forms.DockStyle.Top;
            this.rightLayout.Location = new System.Drawing.Point(0, 4);
            this.rightLayout.Name = "rightLayout";
            this.rightLayout.RowCount = 4;
            this.rightLayout.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.rightLayout.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.rightLayout.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.rightLayout.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.rightLayout.Size = new System.Drawing.Size(475, 838);
            this.rightLayout.TabIndex = 0;
            // 
            // cliGroup
            // 
            this.cliGroup.AutoSize = true;
            this.cliGroup.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.cliGroup.Controls.Add(this.cliLayout);
            this.cliGroup.Dock = System.Windows.Forms.DockStyle.Fill;
            this.cliGroup.Location = new System.Drawing.Point(3, 3);
            this.cliGroup.Name = "cliGroup";
            this.cliGroup.Padding = new System.Windows.Forms.Padding(8);
            this.cliGroup.Size = new System.Drawing.Size(469, 249);
            this.cliGroup.TabIndex = 0;
            this.cliGroup.TabStop = false;
            this.cliGroup.Text = "Hermes CLI";
            // 
            // cliLayout
            // 
            this.cliLayout.AutoSize = true;
            this.cliLayout.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.cliLayout.ColumnCount = 1;
            this.cliLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.cliLayout.Controls.Add(this.lblCliState, 0, 0);
            this.cliLayout.Controls.Add(this.cliButtons, 0, 1);
            this.cliLayout.Dock = System.Windows.Forms.DockStyle.Top;
            this.cliLayout.Location = new System.Drawing.Point(8, 24);
            this.cliLayout.Name = "cliLayout";
            this.cliLayout.RowCount = 2;
            this.cliLayout.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.cliLayout.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.cliLayout.Size = new System.Drawing.Size(453, 103);
            this.cliLayout.TabIndex = 0;
            // 
            // lblCliState
            // 
            this.lblCliState.AutoSize = true;
            this.lblCliState.ForeColor = System.Drawing.Color.DimGray;
            this.lblCliState.Location = new System.Drawing.Point(0, 0);
            this.lblCliState.Margin = new System.Windows.Forms.Padding(0, 0, 0, 4);
            this.lblCliState.Name = "lblCliState";
            this.lblCliState.Size = new System.Drawing.Size(44, 17);
            this.lblCliState.TabIndex = 0;
            this.lblCliState.Text = "状态: -";
            // 
            // cliButtons
            // 
            this.cliButtons.AutoSize = true;
            this.cliButtons.Controls.Add(this.btnCliInstall);
            this.cliButtons.Controls.Add(this.btnCliRepair);
            this.cliButtons.Controls.Add(this.btnCliUpgrade);
            this.cliButtons.Controls.Add(this.btnCliUninstall);
            this.cliButtons.Controls.Add(this.btnCliLaunch);
            this.cliButtons.Location = new System.Drawing.Point(3, 24);
            this.cliButtons.Name = "cliButtons";
            this.cliButtons.Size = new System.Drawing.Size(404, 76);
            this.cliButtons.TabIndex = 1;
            // 
            // btnCliInstall
            // 
            this.btnCliInstall.Location = new System.Drawing.Point(0, 0);
            this.btnCliInstall.Margin = new System.Windows.Forms.Padding(0, 0, 8, 8);
            this.btnCliInstall.Name = "btnCliInstall";
            this.btnCliInstall.Size = new System.Drawing.Size(92, 30);
            this.btnCliInstall.TabIndex = 0;
            this.btnCliInstall.Text = "新装";
            this.btnCliInstall.UseVisualStyleBackColor = true;
            // 
            // btnCliRepair
            // 
            this.btnCliRepair.Location = new System.Drawing.Point(100, 0);
            this.btnCliRepair.Margin = new System.Windows.Forms.Padding(0, 0, 8, 8);
            this.btnCliRepair.Name = "btnCliRepair";
            this.btnCliRepair.Size = new System.Drawing.Size(96, 30);
            this.btnCliRepair.TabIndex = 1;
            this.btnCliRepair.Text = "修复重装";
            this.btnCliRepair.UseVisualStyleBackColor = true;
            // 
            // btnCliUpgrade
            // 
            this.btnCliUpgrade.Location = new System.Drawing.Point(204, 0);
            this.btnCliUpgrade.Margin = new System.Windows.Forms.Padding(0, 0, 8, 8);
            this.btnCliUpgrade.Name = "btnCliUpgrade";
            this.btnCliUpgrade.Size = new System.Drawing.Size(92, 30);
            this.btnCliUpgrade.TabIndex = 2;
            this.btnCliUpgrade.Text = "升级";
            this.btnCliUpgrade.UseVisualStyleBackColor = true;
            // 
            // btnCliUninstall
            // 
            this.btnCliUninstall.Location = new System.Drawing.Point(304, 0);
            this.btnCliUninstall.Margin = new System.Windows.Forms.Padding(0, 0, 8, 8);
            this.btnCliUninstall.Name = "btnCliUninstall";
            this.btnCliUninstall.Size = new System.Drawing.Size(92, 30);
            this.btnCliUninstall.TabIndex = 3;
            this.btnCliUninstall.Text = "卸载";
            this.btnCliUninstall.UseVisualStyleBackColor = true;
            // 
            // btnCliLaunch
            // 
            this.btnCliLaunch.Location = new System.Drawing.Point(0, 38);
            this.btnCliLaunch.Margin = new System.Windows.Forms.Padding(0, 0, 8, 8);
            this.btnCliLaunch.Name = "btnCliLaunch";
            this.btnCliLaunch.Size = new System.Drawing.Size(92, 30);
            this.btnCliLaunch.TabIndex = 4;
            this.btnCliLaunch.Text = "启动";
            this.btnCliLaunch.UseVisualStyleBackColor = true;
            // 
            // desktopGroup
            // 
            this.desktopGroup.AutoSize = true;
            this.desktopGroup.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.desktopGroup.Controls.Add(this.desktopLayout);
            this.desktopGroup.Dock = System.Windows.Forms.DockStyle.Fill;
            this.desktopGroup.Location = new System.Drawing.Point(3, 258);
            this.desktopGroup.Name = "desktopGroup";
            this.desktopGroup.Padding = new System.Windows.Forms.Padding(8);
            this.desktopGroup.Size = new System.Drawing.Size(469, 211);
            this.desktopGroup.TabIndex = 1;
            this.desktopGroup.TabStop = false;
            this.desktopGroup.Text = "Hermes 桌面端";
            // 
            // desktopLayout
            // 
            this.desktopLayout.AutoSize = true;
            this.desktopLayout.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.desktopLayout.ColumnCount = 1;
            this.desktopLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.desktopLayout.Controls.Add(this.lblDesktopState, 0, 0);
            this.desktopLayout.Controls.Add(this.desktopButtons, 0, 1);
            this.desktopLayout.Dock = System.Windows.Forms.DockStyle.Top;
            this.desktopLayout.Location = new System.Drawing.Point(8, 24);
            this.desktopLayout.Name = "desktopLayout";
            this.desktopLayout.RowCount = 2;
            this.desktopLayout.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.desktopLayout.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.desktopLayout.Size = new System.Drawing.Size(453, 65);
            this.desktopLayout.TabIndex = 0;
            // 
            // lblDesktopState
            // 
            this.lblDesktopState.AutoSize = true;
            this.lblDesktopState.ForeColor = System.Drawing.Color.DimGray;
            this.lblDesktopState.Location = new System.Drawing.Point(0, 0);
            this.lblDesktopState.Margin = new System.Windows.Forms.Padding(0, 0, 0, 4);
            this.lblDesktopState.Name = "lblDesktopState";
            this.lblDesktopState.Size = new System.Drawing.Size(44, 17);
            this.lblDesktopState.TabIndex = 0;
            this.lblDesktopState.Text = "状态: -";
            // 
            // desktopButtons
            // 
            this.desktopButtons.AutoSize = true;
            this.desktopButtons.Controls.Add(this.btnDesktopInstall);
            this.desktopButtons.Controls.Add(this.btnDesktopRepair);
            this.desktopButtons.Controls.Add(this.btnDesktopUpgrade);
            this.desktopButtons.Controls.Add(this.btnDesktopLaunch);
            this.desktopButtons.Location = new System.Drawing.Point(3, 24);
            this.desktopButtons.Name = "desktopButtons";
            this.desktopButtons.Size = new System.Drawing.Size(404, 38);
            this.desktopButtons.TabIndex = 1;
            // 
            // btnDesktopInstall
            // 
            this.btnDesktopInstall.Location = new System.Drawing.Point(0, 0);
            this.btnDesktopInstall.Margin = new System.Windows.Forms.Padding(0, 0, 8, 8);
            this.btnDesktopInstall.Name = "btnDesktopInstall";
            this.btnDesktopInstall.Size = new System.Drawing.Size(92, 30);
            this.btnDesktopInstall.TabIndex = 0;
            this.btnDesktopInstall.Text = "新装";
            this.btnDesktopInstall.UseVisualStyleBackColor = true;
            // 
            // btnDesktopRepair
            // 
            this.btnDesktopRepair.Location = new System.Drawing.Point(100, 0);
            this.btnDesktopRepair.Margin = new System.Windows.Forms.Padding(0, 0, 8, 8);
            this.btnDesktopRepair.Name = "btnDesktopRepair";
            this.btnDesktopRepair.Size = new System.Drawing.Size(96, 30);
            this.btnDesktopRepair.TabIndex = 1;
            this.btnDesktopRepair.Text = "修复重装";
            this.btnDesktopRepair.UseVisualStyleBackColor = true;
            // 
            // btnDesktopUpgrade
            // 
            this.btnDesktopUpgrade.Location = new System.Drawing.Point(204, 0);
            this.btnDesktopUpgrade.Margin = new System.Windows.Forms.Padding(0, 0, 8, 8);
            this.btnDesktopUpgrade.Name = "btnDesktopUpgrade";
            this.btnDesktopUpgrade.Size = new System.Drawing.Size(92, 30);
            this.btnDesktopUpgrade.TabIndex = 2;
            this.btnDesktopUpgrade.Text = "升级";
            this.btnDesktopUpgrade.UseVisualStyleBackColor = true;
            // 
            // btnDesktopLaunch
            // 
            this.btnDesktopLaunch.Location = new System.Drawing.Point(304, 0);
            this.btnDesktopLaunch.Margin = new System.Windows.Forms.Padding(0, 0, 8, 8);
            this.btnDesktopLaunch.Name = "btnDesktopLaunch";
            this.btnDesktopLaunch.Size = new System.Drawing.Size(92, 30);
            this.btnDesktopLaunch.TabIndex = 3;
            this.btnDesktopLaunch.Text = "启动";
            this.btnDesktopLaunch.UseVisualStyleBackColor = true;
            // 
            // optionsGroup
            // 
            this.optionsGroup.AutoSize = true;
            this.optionsGroup.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.optionsGroup.Controls.Add(this.optLayout);
            this.optionsGroup.Dock = System.Windows.Forms.DockStyle.Fill;
            this.optionsGroup.Location = new System.Drawing.Point(3, 475);
            this.optionsGroup.Name = "optionsGroup";
            this.optionsGroup.Padding = new System.Windows.Forms.Padding(8);
            this.optionsGroup.Size = new System.Drawing.Size(469, 335);
            this.optionsGroup.TabIndex = 2;
            this.optionsGroup.TabStop = false;
            this.optionsGroup.Text = "安装选项";
            // 
            // optLayout
            // 
            this.optLayout.AutoSize = true;
            this.optLayout.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.optLayout.ColumnCount = 2;
            this.optLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 92F));
            this.optLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.optLayout.Controls.Add(this.lblCapChannel, 0, 0);
            this.optLayout.Controls.Add(this.cmbChannel, 1, 0);
            this.optLayout.Controls.Add(this.lblCapRepo, 0, 1);
            this.optLayout.Controls.Add(this.cmbRepoMirror, 1, 1);
            this.optLayout.Controls.Add(this.lblCapElectron, 0, 2);
            this.optLayout.Controls.Add(this.cmbElectronMirror, 1, 2);
            this.optLayout.Controls.Add(this.lblCapBuilder, 0, 3);
            this.optLayout.Controls.Add(this.cmbBuilderMirror, 1, 3);
            this.optLayout.Controls.Add(this.lblCapNpm, 0, 4);
            this.optLayout.Controls.Add(this.cmbNpmRegistry, 1, 4);
            this.optLayout.Controls.Add(this.lblCapPip, 0, 5);
            this.optLayout.Controls.Add(this.cmbPipIndex, 1, 5);
            this.optLayout.Controls.Add(this.chkSkipBrowser, 0, 6);
            this.optLayout.Controls.Add(this.chkSkipComputerUse, 0, 7);
            this.optLayout.Controls.Add(this.chkBackup, 0, 8);
            this.optLayout.Controls.Add(this.chkHostsFallback, 0, 9);
            this.optLayout.Controls.Add(this.optButtons, 0, 10);
            this.optLayout.Dock = System.Windows.Forms.DockStyle.Top;
            this.optLayout.Location = new System.Drawing.Point(8, 24);
            this.optLayout.Name = "optLayout";
            this.optLayout.RowCount = 11;
            this.optLayout.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.optLayout.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.optLayout.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.optLayout.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.optLayout.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.optLayout.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.optLayout.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.optLayout.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.optLayout.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.optLayout.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.optLayout.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.optLayout.Size = new System.Drawing.Size(453, 303);
            this.optLayout.TabIndex = 0;
            // 
            // lblCapChannel
            // 
            this.lblCapChannel.AutoSize = true;
            this.lblCapChannel.Location = new System.Drawing.Point(0, 6);
            this.lblCapChannel.Margin = new System.Windows.Forms.Padding(0, 6, 6, 3);
            this.lblCapChannel.Name = "lblCapChannel";
            this.lblCapChannel.Size = new System.Drawing.Size(56, 17);
            this.lblCapChannel.TabIndex = 0;
            this.lblCapChannel.Text = "发布通道";
            this.lblCapChannel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // cmbChannel
            // 
            this.cmbChannel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.cmbChannel.Items.AddRange(new object[] {
            "stable",
            "main"});
            this.cmbChannel.Location = new System.Drawing.Point(92, 3);
            this.cmbChannel.Margin = new System.Windows.Forms.Padding(0, 3, 0, 3);
            this.cmbChannel.Name = "cmbChannel";
            this.cmbChannel.Size = new System.Drawing.Size(361, 25);
            this.cmbChannel.TabIndex = 1;
            // 
            // lblCapRepo
            // 
            this.lblCapRepo.AutoSize = true;
            this.lblCapRepo.Location = new System.Drawing.Point(0, 37);
            this.lblCapRepo.Margin = new System.Windows.Forms.Padding(0, 6, 6, 3);
            this.lblCapRepo.Name = "lblCapRepo";
            this.lblCapRepo.Size = new System.Drawing.Size(56, 17);
            this.lblCapRepo.TabIndex = 2;
            this.lblCapRepo.Text = "源码镜像";
            this.lblCapRepo.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // cmbRepoMirror
            // 
            this.cmbRepoMirror.Dock = System.Windows.Forms.DockStyle.Fill;
            this.cmbRepoMirror.Items.AddRange(new object[] {
            "https://github.com/NousResearch/hermes-agent.git",
            "https://ghproxy.net/https://github.com/NousResearch/hermes-agent.git",
            "https://gh-proxy.com/https://github.com/NousResearch/hermes-agent.git"});
            this.cmbRepoMirror.Location = new System.Drawing.Point(92, 34);
            this.cmbRepoMirror.Margin = new System.Windows.Forms.Padding(0, 3, 0, 3);
            this.cmbRepoMirror.Name = "cmbRepoMirror";
            this.cmbRepoMirror.Size = new System.Drawing.Size(361, 25);
            this.cmbRepoMirror.TabIndex = 3;
            // 
            // lblCapElectron
            // 
            this.lblCapElectron.AutoSize = true;
            this.lblCapElectron.Location = new System.Drawing.Point(0, 63);
            this.lblCapElectron.Margin = new System.Windows.Forms.Padding(0, 6, 6, 3);
            this.lblCapElectron.Name = "lblCapElectron";
            this.lblCapElectron.Size = new System.Drawing.Size(55, 17);
            this.lblCapElectron.TabIndex = 4;
            this.lblCapElectron.Text = "Electron";
            this.lblCapElectron.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // cmbElectronMirror
            // 
            this.cmbElectronMirror.Dock = System.Windows.Forms.DockStyle.Fill;
            this.cmbElectronMirror.Items.AddRange(new object[] {
            "https://npmmirror.com/mirrors/electron/"});
            this.cmbElectronMirror.Location = new System.Drawing.Point(92, 60);
            this.cmbElectronMirror.Margin = new System.Windows.Forms.Padding(0, 3, 0, 3);
            this.cmbElectronMirror.Name = "cmbElectronMirror";
            this.cmbElectronMirror.Size = new System.Drawing.Size(361, 25);
            this.cmbElectronMirror.TabIndex = 5;
            // 
            // lblCapBuilder
            // 
            this.lblCapBuilder.AutoSize = true;
            this.lblCapBuilder.Location = new System.Drawing.Point(0, 89);
            this.lblCapBuilder.Margin = new System.Windows.Forms.Padding(0, 6, 6, 3);
            this.lblCapBuilder.Name = "lblCapBuilder";
            this.lblCapBuilder.Size = new System.Drawing.Size(56, 17);
            this.lblCapBuilder.TabIndex = 6;
            this.lblCapBuilder.Text = "构建工具";
            this.lblCapBuilder.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // cmbBuilderMirror
            // 
            this.cmbBuilderMirror.Dock = System.Windows.Forms.DockStyle.Fill;
            this.cmbBuilderMirror.Items.AddRange(new object[] {
            "https://npmmirror.com/mirrors/electron-builder-binaries/"});
            this.cmbBuilderMirror.Location = new System.Drawing.Point(92, 86);
            this.cmbBuilderMirror.Margin = new System.Windows.Forms.Padding(0, 3, 0, 3);
            this.cmbBuilderMirror.Name = "cmbBuilderMirror";
            this.cmbBuilderMirror.Size = new System.Drawing.Size(361, 25);
            this.cmbBuilderMirror.TabIndex = 7;
            // 
            // lblCapNpm
            // 
            this.lblCapNpm.AutoSize = true;
            this.lblCapNpm.Location = new System.Drawing.Point(0, 115);
            this.lblCapNpm.Margin = new System.Windows.Forms.Padding(0, 6, 6, 3);
            this.lblCapNpm.Name = "lblCapNpm";
            this.lblCapNpm.Size = new System.Drawing.Size(62, 17);
            this.lblCapNpm.TabIndex = 8;
            this.lblCapNpm.Text = "npm 镜像";
            this.lblCapNpm.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // cmbNpmRegistry
            // 
            this.cmbNpmRegistry.Dock = System.Windows.Forms.DockStyle.Fill;
            this.cmbNpmRegistry.Items.AddRange(new object[] {
            "https://registry.npmmirror.com",
            "https://registry.npmjs.org"});
            this.cmbNpmRegistry.Location = new System.Drawing.Point(92, 112);
            this.cmbNpmRegistry.Margin = new System.Windows.Forms.Padding(0, 3, 0, 3);
            this.cmbNpmRegistry.Name = "cmbNpmRegistry";
            this.cmbNpmRegistry.Size = new System.Drawing.Size(361, 25);
            this.cmbNpmRegistry.TabIndex = 9;
            // 
            // lblCapPip
            // 
            this.lblCapPip.AutoSize = true;
            this.lblCapPip.Location = new System.Drawing.Point(0, 141);
            this.lblCapPip.Margin = new System.Windows.Forms.Padding(0, 6, 6, 3);
            this.lblCapPip.Name = "lblCapPip";
            this.lblCapPip.Size = new System.Drawing.Size(60, 17);
            this.lblCapPip.TabIndex = 10;
            this.lblCapPip.Text = "PyPI 镜像";
            this.lblCapPip.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // cmbPipIndex
            // 
            this.cmbPipIndex.Dock = System.Windows.Forms.DockStyle.Fill;
            this.cmbPipIndex.Items.AddRange(new object[] {
            "https://pypi.tuna.tsinghua.edu.cn/simple",
            "https://mirrors.aliyun.com/pypi/simple/",
            "https://pypi.org/simple"});
            this.cmbPipIndex.Location = new System.Drawing.Point(92, 138);
            this.cmbPipIndex.Margin = new System.Windows.Forms.Padding(0, 3, 0, 3);
            this.cmbPipIndex.Name = "cmbPipIndex";
            this.cmbPipIndex.Size = new System.Drawing.Size(361, 25);
            this.cmbPipIndex.TabIndex = 11;
            // 
            // chkSkipBrowser
            // 
            this.chkSkipBrowser.AutoSize = true;
            this.optLayout.SetColumnSpan(this.chkSkipBrowser, 2);
            this.chkSkipBrowser.Location = new System.Drawing.Point(0, 167);
            this.chkSkipBrowser.Margin = new System.Windows.Forms.Padding(0, 6, 0, 0);
            this.chkSkipBrowser.Name = "chkSkipBrowser";
            this.chkSkipBrowser.Size = new System.Drawing.Size(201, 21);
            this.chkSkipBrowser.TabIndex = 12;
            this.chkSkipBrowser.Text = "跳过浏览器工具（省 ~500MB）";
            this.chkSkipBrowser.UseVisualStyleBackColor = true;
            // 
            // chkSkipComputerUse
            // 
            this.chkSkipComputerUse.AutoSize = true;
            this.optLayout.SetColumnSpan(this.chkSkipComputerUse, 2);
            this.chkSkipComputerUse.Location = new System.Drawing.Point(0, 190);
            this.chkSkipComputerUse.Margin = new System.Windows.Forms.Padding(0, 2, 0, 0);
            this.chkSkipComputerUse.Name = "chkSkipComputerUse";
            this.chkSkipComputerUse.Size = new System.Drawing.Size(123, 21);
            this.chkSkipComputerUse.TabIndex = 13;
            this.chkSkipComputerUse.Text = "跳过电脑操作驱动";
            this.chkSkipComputerUse.UseVisualStyleBackColor = true;
            // 
            // chkBackup
            // 
            this.chkBackup.AutoSize = true;
            this.optLayout.SetColumnSpan(this.chkBackup, 2);
            this.chkBackup.Location = new System.Drawing.Point(0, 213);
            this.chkBackup.Margin = new System.Windows.Forms.Padding(0, 2, 0, 0);
            this.chkBackup.Name = "chkBackup";
            this.chkBackup.Size = new System.Drawing.Size(111, 21);
            this.chkBackup.TabIndex = 14;
            this.chkBackup.Text = "升级前完整备份";
            this.chkBackup.UseVisualStyleBackColor = true;
            // 
            // chkHostsFallback
            // 
            this.chkHostsFallback.AutoSize = true;
            this.optLayout.SetColumnSpan(this.chkHostsFallback, 2);
            this.chkHostsFallback.Location = new System.Drawing.Point(0, 236);
            this.chkHostsFallback.Margin = new System.Windows.Forms.Padding(0, 2, 0, 0);
            this.chkHostsFallback.Name = "chkHostsFallback";
            this.chkHostsFallback.Size = new System.Drawing.Size(266, 21);
            this.chkHostsFallback.TabIndex = 15;
            this.chkHostsFallback.Text = "镜像失败时允许尝试修复 hosts（需管理员）";
            this.chkHostsFallback.UseVisualStyleBackColor = true;
            // 
            // optButtons
            // 
            this.optButtons.AutoSize = true;
            this.optLayout.SetColumnSpan(this.optButtons, 2);
            this.optButtons.Controls.Add(this.btnApplyMirrors);
            this.optButtons.Controls.Add(this.btnFixHosts);
            this.optButtons.Controls.Add(this.btnRestoreHosts);
            this.optButtons.Controls.Add(this.btnProxy);
            this.optButtons.Location = new System.Drawing.Point(0, 265);
            this.optButtons.Margin = new System.Windows.Forms.Padding(0, 8, 0, 0);
            this.optButtons.Name = "optButtons";
            this.optButtons.Size = new System.Drawing.Size(400, 38);
            this.optButtons.TabIndex = 16;
            // 
            // btnApplyMirrors
            // 
            this.btnApplyMirrors.Location = new System.Drawing.Point(0, 0);
            this.btnApplyMirrors.Margin = new System.Windows.Forms.Padding(0, 0, 8, 8);
            this.btnApplyMirrors.Name = "btnApplyMirrors";
            this.btnApplyMirrors.Size = new System.Drawing.Size(88, 30);
            this.btnApplyMirrors.TabIndex = 0;
            this.btnApplyMirrors.Text = "应用镜像";
            this.btnApplyMirrors.UseVisualStyleBackColor = true;
            // 
            // btnFixHosts
            // 
            this.btnFixHosts.Location = new System.Drawing.Point(96, 0);
            this.btnFixHosts.Margin = new System.Windows.Forms.Padding(0, 0, 8, 8);
            this.btnFixHosts.Name = "btnFixHosts";
            this.btnFixHosts.Size = new System.Drawing.Size(96, 30);
            this.btnFixHosts.TabIndex = 1;
            this.btnFixHosts.Text = "修复 hosts";
            this.btnFixHosts.UseVisualStyleBackColor = true;
            // 
            // btnRestoreHosts
            // 
            this.btnRestoreHosts.Location = new System.Drawing.Point(200, 0);
            this.btnRestoreHosts.Margin = new System.Windows.Forms.Padding(0, 0, 8, 8);
            this.btnRestoreHosts.Name = "btnRestoreHosts";
            this.btnRestoreHosts.Size = new System.Drawing.Size(96, 30);
            this.btnRestoreHosts.TabIndex = 2;
            this.btnRestoreHosts.Text = "还原 hosts";
            this.btnRestoreHosts.UseVisualStyleBackColor = true;
            // 
            // btnProxy
            // 
            this.btnProxy.Location = new System.Drawing.Point(304, 0);
            this.btnProxy.Margin = new System.Windows.Forms.Padding(0, 0, 8, 8);
            this.btnProxy.Name = "btnProxy";
            this.btnProxy.Size = new System.Drawing.Size(88, 30);
            this.btnProxy.TabIndex = 3;
            this.btnProxy.Text = "SSH 代理";
            this.btnProxy.UseVisualStyleBackColor = true;
            // 
            // hintLabel
            // 
            this.hintLabel.AutoSize = true;
            this.hintLabel.Dock = System.Windows.Forms.DockStyle.Top;
            this.hintLabel.ForeColor = System.Drawing.Color.Gray;
            this.hintLabel.Location = new System.Drawing.Point(3, 813);
            this.hintLabel.Name = "hintLabel";
            this.hintLabel.Padding = new System.Windows.Forms.Padding(2, 8, 0, 0);
            this.hintLabel.Size = new System.Drawing.Size(469, 25);
            this.hintLabel.TabIndex = 3;
            this.hintLabel.Text = "提示：默认使用镜像，无需管理员权限。只有镜像全部失败时才会提示修复 hosts。";
            // 
            // bottomLayout
            // 
            this.bottomLayout.ColumnCount = 1;
            this.bottomLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.bottomLayout.Controls.Add(this.progressPanel, 0, 0);
            this.bottomLayout.Controls.Add(this.rtbLog, 0, 1);
            this.bottomLayout.Controls.Add(this.buttonBar, 0, 2);
            this.bottomLayout.Dock = System.Windows.Forms.DockStyle.Fill;
            this.bottomLayout.Location = new System.Drawing.Point(3, 513);
            this.bottomLayout.Name = "bottomLayout";
            this.bottomLayout.Padding = new System.Windows.Forms.Padding(10, 4, 10, 8);
            this.bottomLayout.RowCount = 3;
            this.bottomLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 40F));
            this.bottomLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.bottomLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 38F));
            this.bottomLayout.Size = new System.Drawing.Size(1194, 344);
            this.bottomLayout.TabIndex = 2;
            // 
            // progressPanel
            // 
            this.progressPanel.ColumnCount = 2;
            this.progressPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 300F));
            this.progressPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.progressPanel.Controls.Add(this.pb, 0, 0);
            this.progressPanel.Controls.Add(this.lblProgress, 1, 0);
            this.progressPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.progressPanel.Location = new System.Drawing.Point(13, 7);
            this.progressPanel.Name = "progressPanel";
            this.progressPanel.RowCount = 1;
            this.progressPanel.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.progressPanel.Size = new System.Drawing.Size(1168, 34);
            this.progressPanel.TabIndex = 0;
            // 
            // pb
            // 
            this.pb.Dock = System.Windows.Forms.DockStyle.Fill;
            this.pb.Location = new System.Drawing.Point(0, 8);
            this.pb.Margin = new System.Windows.Forms.Padding(0, 8, 10, 8);
            this.pb.Name = "pb";
            this.pb.Size = new System.Drawing.Size(290, 18);
            this.pb.Style = System.Windows.Forms.ProgressBarStyle.Continuous;
            this.pb.TabIndex = 0;
            // 
            // lblProgress
            // 
            this.lblProgress.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblProgress.ForeColor = System.Drawing.Color.DimGray;
            this.lblProgress.Location = new System.Drawing.Point(303, 0);
            this.lblProgress.Name = "lblProgress";
            this.lblProgress.Size = new System.Drawing.Size(862, 34);
            this.lblProgress.TabIndex = 1;
            this.lblProgress.Text = "就绪";
            this.lblProgress.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // rtbLog
            // 
            this.rtbLog.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(250)))), ((int)(((byte)(250)))), ((int)(((byte)(250)))));
            this.rtbLog.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.rtbLog.DetectUrls = false;
            this.rtbLog.Dock = System.Windows.Forms.DockStyle.Fill;
            this.rtbLog.Font = new System.Drawing.Font("Consolas", 9F);
            this.rtbLog.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(40)))), ((int)(((byte)(40)))), ((int)(((byte)(40)))));
            this.rtbLog.HideSelection = false;
            this.rtbLog.Location = new System.Drawing.Point(13, 47);
            this.rtbLog.Name = "rtbLog";
            this.rtbLog.ReadOnly = true;
            this.rtbLog.Size = new System.Drawing.Size(1168, 248);
            this.rtbLog.TabIndex = 1;
            this.rtbLog.Text = "";
            this.rtbLog.WordWrap = false;
            // 
            // buttonBar
            // 
            this.buttonBar.Controls.Add(this.btnAbout);
            this.buttonBar.Controls.Add(this.btnOpenData);
            this.buttonBar.Controls.Add(this.btnSaveLog);
            this.buttonBar.Controls.Add(this.btnOpenLogs);
            this.buttonBar.Controls.Add(this.btnCancel);
            this.buttonBar.Dock = System.Windows.Forms.DockStyle.Fill;
            this.buttonBar.FlowDirection = System.Windows.Forms.FlowDirection.RightToLeft;
            this.buttonBar.Location = new System.Drawing.Point(13, 301);
            this.buttonBar.Name = "buttonBar";
            this.buttonBar.Padding = new System.Windows.Forms.Padding(0, 4, 0, 0);
            this.buttonBar.Size = new System.Drawing.Size(1168, 32);
            this.buttonBar.TabIndex = 2;
            this.buttonBar.WrapContents = false;
            // 
            // btnAbout
            // 
            this.btnAbout.Location = new System.Drawing.Point(1088, 4);
            this.btnAbout.Margin = new System.Windows.Forms.Padding(0, 0, 8, 8);
            this.btnAbout.Name = "btnAbout";
            this.btnAbout.Size = new System.Drawing.Size(72, 30);
            this.btnAbout.TabIndex = 0;
            this.btnAbout.Text = "关于";
            this.btnAbout.UseVisualStyleBackColor = true;
            // 
            // btnOpenData
            // 
            this.btnOpenData.Location = new System.Drawing.Point(972, 4);
            this.btnOpenData.Margin = new System.Windows.Forms.Padding(0, 0, 8, 8);
            this.btnOpenData.Name = "btnOpenData";
            this.btnOpenData.Size = new System.Drawing.Size(108, 30);
            this.btnOpenData.TabIndex = 1;
            this.btnOpenData.Text = "打开数据目录";
            this.btnOpenData.UseVisualStyleBackColor = true;
            // 
            // btnSaveLog
            // 
            this.btnSaveLog.Location = new System.Drawing.Point(876, 4);
            this.btnSaveLog.Margin = new System.Windows.Forms.Padding(0, 0, 8, 8);
            this.btnSaveLog.Name = "btnSaveLog";
            this.btnSaveLog.Size = new System.Drawing.Size(88, 30);
            this.btnSaveLog.TabIndex = 2;
            this.btnSaveLog.Text = "保存日志";
            this.btnSaveLog.UseVisualStyleBackColor = true;
            // 
            // btnOpenLogs
            // 
            this.btnOpenLogs.Location = new System.Drawing.Point(760, 4);
            this.btnOpenLogs.Margin = new System.Windows.Forms.Padding(0, 0, 8, 8);
            this.btnOpenLogs.Name = "btnOpenLogs";
            this.btnOpenLogs.Size = new System.Drawing.Size(108, 30);
            this.btnOpenLogs.TabIndex = 3;
            this.btnOpenLogs.Text = "打开日志目录";
            this.btnOpenLogs.UseVisualStyleBackColor = true;
            // 
            // btnCancel
            // 
            this.btnCancel.Enabled = false;
            this.btnCancel.Location = new System.Drawing.Point(680, 4);
            this.btnCancel.Margin = new System.Windows.Forms.Padding(0, 0, 8, 8);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Size = new System.Drawing.Size(72, 30);
            this.btnCancel.TabIndex = 4;
            this.btnCancel.Text = "取消";
            this.btnCancel.UseVisualStyleBackColor = true;
            // 
            // MainForm
            // 
            this.ClientSize = new System.Drawing.Size(1200, 860);
            this.Controls.Add(this.rootLayout);
            this.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F);
            this.MinimumSize = new System.Drawing.Size(1000, 700);
            this.Name = "MainForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "Hermes 安装助手";
            this.rootLayout.ResumeLayout(false);
            this.headerPanel.ResumeLayout(false);
            this.headerPanel.PerformLayout();
            this.splitMain.Panel1.ResumeLayout(false);
            this.splitMain.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.splitMain)).EndInit();
            this.splitMain.ResumeLayout(false);
            this.leftLayout.ResumeLayout(false);
            this.toolbar.ResumeLayout(false);
            this.toolbar.PerformLayout();
            this.tabs.ResumeLayout(false);
            this.envTab.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.dgvProbe)).EndInit();
            this.netTab.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.dgvNetwork)).EndInit();
            this.rightScroll.ResumeLayout(false);
            this.rightScroll.PerformLayout();
            this.rightLayout.ResumeLayout(false);
            this.rightLayout.PerformLayout();
            this.cliGroup.ResumeLayout(false);
            this.cliGroup.PerformLayout();
            this.cliLayout.ResumeLayout(false);
            this.cliLayout.PerformLayout();
            this.cliButtons.ResumeLayout(false);
            this.desktopGroup.ResumeLayout(false);
            this.desktopGroup.PerformLayout();
            this.desktopLayout.ResumeLayout(false);
            this.desktopLayout.PerformLayout();
            this.desktopButtons.ResumeLayout(false);
            this.optionsGroup.ResumeLayout(false);
            this.optionsGroup.PerformLayout();
            this.optLayout.ResumeLayout(false);
            this.optLayout.PerformLayout();
            this.optButtons.ResumeLayout(false);
            this.bottomLayout.ResumeLayout(false);
            this.progressPanel.ResumeLayout(false);
            this.buttonBar.ResumeLayout(false);
            this.ResumeLayout(false);

        }
    }
}
