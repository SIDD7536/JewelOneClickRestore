namespace JewelOneClickRestore;

partial class MainForm
{
    private System.ComponentModel.IContainer? components = null;

    private Panel headerPanel = null!;
    private Label titleLabel = null!;
    private Label subtitleLabel = null!;
    private TableLayoutPanel contentLayout = null!;
    private GroupBox instanceGroup = null!;
    private ComboBox instanceComboBox = null!;
    private Button refreshInstancesButton = null!;
    private Label instanceHintLabel = null!;
    private GroupBox backupGroup = null!;
    private TextBox backupTextBox = null!;
    private Button browseButton = null!;
    private Label backupInfoLabel = null!;
    private GroupBox databaseGroup = null!;
    private ComboBox databaseComboBox = null!;
    private CheckBox showSystemDatabasesCheckBox = null!;
    private Label databaseHintLabel = null!;
    private GroupBox sqlLocationGroup = null!;
    private Label dataPathValueLabel = null!;
    private Label logPathValueLabel = null!;
    private GroupBox optionsGroup = null!;
    private CheckBox replaceCheckBox = null!;
    private CheckBox createIfMissingCheckBox = null!;
    private Button registerFileHandlerButton = null!;
    private Button restoreButton = null!;
    private Panel footerPanel = null!;
    private Label statusCaptionLabel = null!;
    private Label statusLabel = null!;
    private ProgressBar restoreProgressBar = null!;
    private Label logPathLabel = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            components?.Dispose();
            DisposePreparedBackup();
        }
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        SuspendLayout();

        BackColor = Color.FromArgb(246, 248, 252);
        ClientSize = new Size(920, 790);
        MinimumSize = new Size(800, 700);
        StartPosition = FormStartPosition.Manual;
        Text = "JEWEL One Click Restore";
        Font = new Font("Segoe UI", 9F);
        AutoScaleMode = AutoScaleMode.Dpi;

        headerPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 104,
            BackColor = Color.FromArgb(24, 76, 145),
            Padding = new Padding(28, 18, 28, 12)
        };

        titleLabel = new Label
        {
            AutoSize = true,
            Text = "JEWEL One Click Restore",
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 20F, FontStyle.Bold),
            Location = new Point(26, 17)
        };

        subtitleLabel = new Label
        {
            AutoSize = true,
            Text = "Offline local SQL Server restore • auto DATA/LOG paths • verified restore",
            ForeColor = Color.FromArgb(220, 232, 247),
            Font = new Font("Segoe UI", 9.5F),
            Location = new Point(29, 63)
        };

        headerPanel.Controls.Add(titleLabel);
        headerPanel.Controls.Add(subtitleLabel);

        contentLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(24, 16, 24, 10),
            ColumnCount = 1,
            RowCount = 6,
            AutoScroll = true
        };
        contentLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        contentLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 110F));
        contentLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 122F));
        contentLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 126F));
        contentLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 130F));
        contentLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 130F));
        contentLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 82F));

        instanceGroup = CreateGroup("1   LOCAL SQL SERVER INSTANCE");
        var instancePanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Padding = new Padding(10, 6, 10, 8)
        };
        instancePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        instancePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 108F));
        instancePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
        instancePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));

        instanceComboBox = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            IntegralHeight = false,
            Font = new Font("Segoe UI", 10F),
            Margin = new Padding(0, 2, 10, 2)
        };
        refreshInstancesButton = CreateSecondaryButton("Refresh");
        refreshInstancesButton.Dock = DockStyle.Fill;
        refreshInstancesButton.Margin = new Padding(0, 1, 0, 1);
        instanceHintLabel = new Label
        {
            Text = "Detected from SQL Server services/registry on this PC only.",
            ForeColor = Color.DimGray,
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft
        };
        instancePanel.SetColumnSpan(instanceHintLabel, 2);
        instancePanel.Controls.Add(instanceComboBox, 0, 0);
        instancePanel.Controls.Add(refreshInstancesButton, 1, 0);
        instancePanel.Controls.Add(instanceHintLabel, 0, 1);
        instanceGroup.Controls.Add(instancePanel);

        backupGroup = CreateGroup("2   BACKUP FILE");
        var backupPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Padding = new Padding(10, 6, 10, 8)
        };
        backupPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        backupPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 108F));
        backupPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
        backupPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
        backupTextBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 10F),
            PlaceholderText = @"Select .BAK or .ZIP",
            Margin = new Padding(0, 4, 10, 4)
        };
        browseButton = CreateSecondaryButton("Browse");
        browseButton.Dock = DockStyle.Fill;
        browseButton.Margin = new Padding(0, 1, 0, 1);
        backupInfoLabel = new Label
        {
            Text = "Choose a backup. ZIP is extracted automatically; no internet is used.",
            ForeColor = Color.DimGray,
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft
        };
        backupPanel.SetColumnSpan(backupInfoLabel, 2);
        backupPanel.Controls.Add(backupTextBox, 0, 0);
        backupPanel.Controls.Add(browseButton, 1, 0);
        backupPanel.Controls.Add(backupInfoLabel, 0, 1);
        backupGroup.Controls.Add(backupPanel);

        databaseGroup = CreateGroup("3   TARGET DATABASE");
        var databasePanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(10, 6, 10, 8)
        };
        databasePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
        databasePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 29F));
        databasePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
        databaseComboBox = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDown,
            Font = new Font("Segoe UI", 10F),
            Margin = new Padding(0, 2, 0, 2),
            AutoCompleteMode = AutoCompleteMode.SuggestAppend,
            AutoCompleteSource = AutoCompleteSource.ListItems
        };
        showSystemDatabasesCheckBox = new CheckBox
        {
            Text = "Show system databases",
            AutoSize = true,
            Dock = DockStyle.Fill
        };
        databaseHintLabel = new Label
        {
            Text = "After backup analysis, the database name stored inside the backup is selected automatically.",
            ForeColor = Color.DimGray,
            Dock = DockStyle.Fill,
            AutoEllipsis = true
        };
        databasePanel.Controls.Add(databaseComboBox, 0, 0);
        databasePanel.Controls.Add(showSystemDatabasesCheckBox, 0, 1);
        databasePanel.Controls.Add(databaseHintLabel, 0, 2);
        databaseGroup.Controls.Add(databasePanel);

        sqlLocationGroup = CreateGroup("4   SQL SERVER FILE LOCATION — AUTOMATIC");
        var locationPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Padding = new Padding(10, 7, 10, 8)
        };
        locationPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 78F));
        locationPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        locationPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
        locationPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
        locationPanel.Controls.Add(new Label { Text = "DATA:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI Semibold", 9F) }, 0, 0);
        locationPanel.Controls.Add(new Label { Text = "LOG:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI Semibold", 9F) }, 0, 1);
        dataPathValueLabel = new Label { Text = "Detecting...", Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
        logPathValueLabel = new Label { Text = "Detecting...", Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
        locationPanel.Controls.Add(dataPathValueLabel, 1, 0);
        locationPanel.Controls.Add(logPathValueLabel, 1, 1);
        sqlLocationGroup.Controls.Add(locationPanel);

        optionsGroup = CreateGroup("5   OPTIONS");
        var optionsPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Padding = new Padding(12, 8, 12, 8)
        };
        optionsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        optionsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190F));
        optionsPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
        optionsPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
        replaceCheckBox = new CheckBox { Text = "Replace existing database", AutoSize = true, Checked = true, Dock = DockStyle.Fill };
        createIfMissingCheckBox = new CheckBox { Text = "Create database from backup if it does not exist", AutoSize = true, Checked = true, Dock = DockStyle.Fill };
        registerFileHandlerButton = CreateSecondaryButton("Enable .BAK Double-Click");
        registerFileHandlerButton.Dock = DockStyle.Fill;
        registerFileHandlerButton.Margin = new Padding(6, 2, 0, 2);
        optionsPanel.Controls.Add(replaceCheckBox, 0, 0);
        optionsPanel.Controls.Add(createIfMissingCheckBox, 0, 1);
        optionsPanel.Controls.Add(registerFileHandlerButton, 1, 0);
        optionsPanel.SetRowSpan(registerFileHandlerButton, 2);
        optionsGroup.Controls.Add(optionsPanel);

        var restorePanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 10, 0, 6) };
        restoreButton = new Button
        {
            Text = "RESTORE DATABASE",
            Dock = DockStyle.Fill,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(24, 76, 145),
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 12F, FontStyle.Bold),
            Cursor = Cursors.Hand
        };
        restoreButton.FlatAppearance.BorderSize = 0;
        restorePanel.Controls.Add(restoreButton);

        contentLayout.Controls.Add(instanceGroup, 0, 0);
        contentLayout.Controls.Add(backupGroup, 0, 1);
        contentLayout.Controls.Add(databaseGroup, 0, 2);
        contentLayout.Controls.Add(sqlLocationGroup, 0, 3);
        contentLayout.Controls.Add(optionsGroup, 0, 4);
        contentLayout.Controls.Add(restorePanel, 0, 5);

        footerPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 94,
            BackColor = Color.White,
            Padding = new Padding(24, 10, 24, 10)
        };
        statusCaptionLabel = new Label { Text = "Status:", AutoSize = true, Font = new Font("Segoe UI Semibold", 9F), Location = new Point(24, 10) };
        statusLabel = new Label { Text = "Ready", AutoSize = false, AutoEllipsis = true, Location = new Point(78, 10), Size = new Size(780, 20), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
        restoreProgressBar = new ProgressBar { Location = new Point(24, 38), Size = new Size(860, 18), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, Minimum = 0, Maximum = 100, Value = 0, Style = ProgressBarStyle.Continuous };
        logPathLabel = new Label { Text = "", ForeColor = Color.Gray, AutoSize = false, AutoEllipsis = true, Location = new Point(24, 64), Size = new Size(860, 18), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
        footerPanel.Controls.Add(statusCaptionLabel);
        footerPanel.Controls.Add(statusLabel);
        footerPanel.Controls.Add(restoreProgressBar);
        footerPanel.Controls.Add(logPathLabel);

        Controls.Add(contentLayout);
        Controls.Add(footerPanel);
        Controls.Add(headerPanel);

        FormClosing += MainForm_FormClosing;
        Shown += MainForm_Shown;
        instanceComboBox.SelectedIndexChanged += InstanceComboBox_SelectedIndexChanged;
        refreshInstancesButton.Click += RefreshInstancesButton_Click;
        showSystemDatabasesCheckBox.CheckedChanged += ShowSystemDatabasesCheckBox_CheckedChanged;
        browseButton.Click += BrowseButton_Click;
        backupTextBox.TextChanged += BackupTextBox_TextChanged;
        backupTextBox.KeyDown += BackupTextBox_KeyDown;
        restoreButton.Click += RestoreButton_Click;
        databaseComboBox.TextChanged += DatabaseComboBox_TextChanged;
        registerFileHandlerButton.Click += RegisterFileHandlerButton_Click;

        ResumeLayout(false);
    }

    private static GroupBox CreateGroup(string text)
    {
        return new GroupBox
        {
            Text = text,
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Semibold", 9.5F),
            ForeColor = Color.FromArgb(40, 48, 60),
            BackColor = Color.White,
            Padding = new Padding(8),
            Margin = new Padding(0, 0, 0, 10)
        };
    }

    private static Button CreateSecondaryButton(string text)
    {
        var button = new Button
        {
            Text = text,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.White,
            ForeColor = Color.FromArgb(24, 76, 145),
            Font = new Font("Segoe UI Semibold", 9F),
            Cursor = Cursors.Hand
        };
        button.FlatAppearance.BorderColor = Color.FromArgb(175, 190, 210);
        button.FlatAppearance.BorderSize = 1;
        return button;
    }
}
