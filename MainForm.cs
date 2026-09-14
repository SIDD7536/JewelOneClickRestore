using JewelOneClickRestore.Models;
using JewelOneClickRestore.Services;
using Microsoft.Data.SqlClient;

namespace JewelOneClickRestore;

public partial class MainForm : Form
{
    private readonly string? _startupBackupPath;
    private readonly SqlInstanceDiscoveryService _instanceDiscovery = new();
    private readonly SqlConnectionService _connectionService = new();
    private readonly DatabaseService _databaseService;
    private readonly BackupService _backupService;
    private readonly RestoreService _restoreService;
    private readonly ConfigurationService _configurationService = new();

    private AppConfiguration _configuration = new();
    private SqlInstancePaths? _instancePaths;
    private PreparedBackup? _preparedBackup;
    private BackupFileInfo? _backupInfo;
    private string? _analysisServer;
    private string? _analysisSourcePath;
    private bool _loading;
    private bool _busy;

    public MainForm(string? startupBackupPath = null)
    {
        _startupBackupPath = startupBackupPath;
        _databaseService = new DatabaseService(_connectionService);
        _backupService = new BackupService(_connectionService);
        _restoreService = new RestoreService(_connectionService, _databaseService);

        InitializeComponent();

        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
        logPathLabel.Text = $"Log: {LoggingService.CurrentLogFile}";
    }

    private async void MainForm_Shown(object? sender, EventArgs e)
    {
        _configuration = _configurationService.Load();
        ApplyConfigurationToUi();

        if (!string.IsNullOrWhiteSpace(_startupBackupPath))
            backupTextBox.Text = _startupBackupPath;

        await RefreshInstancesAsync(selectSaved: true);

        if (!string.IsNullOrWhiteSpace(backupTextBox.Text) && File.Exists(backupTextBox.Text))
            await AnalyzeBackupAsync();
    }

    private async void RefreshInstancesButton_Click(object? sender, EventArgs e)
        => await RefreshInstancesAsync(selectSaved: false);

    private async Task RefreshInstancesAsync(bool selectSaved)
    {
        if (_busy) return;

        _loading = true;
        SetBusyUi(true, "Detecting local SQL Server instances...");
        try
        {
            var current = (instanceComboBox.SelectedItem as SqlInstanceInfo)?.ServerName;
            var preferred = selectSaved ? _configuration.LastInstance : current;
            var instances = await _instanceDiscovery.DiscoverAsync();

            instanceComboBox.DataSource = null;
            instanceComboBox.DataSource = instances.ToList();

            if (instances.Count == 0)
            {
                SetStatus("No local SQL Server Database Engine instance detected.");
                MessageBox.Show(
                    "No installed SQL Server Database Engine instance was detected on this PC.",
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var selected = !string.IsNullOrWhiteSpace(preferred)
                ? instances.FirstOrDefault(i => string.Equals(i.ServerName, preferred, StringComparison.OrdinalIgnoreCase))
                : null;

            instanceComboBox.SelectedItem = selected ?? instances.FirstOrDefault(i => i.IsRunning) ?? instances[0];
        }
        catch (Exception ex)
        {
            LoggingService.Error("SQL instance discovery failed", ex);
            ShowError("SQL Server instance detection failed.", ex);
        }
        finally
        {
            _loading = false;
            SetBusyUi(false);
        }

        await LoadSelectedInstanceAsync(reanalyzeBackup: false);
    }

    private async void InstanceComboBox_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_loading || _busy) return;
        InvalidateBackupAnalysis();
        await LoadSelectedInstanceAsync(reanalyzeBackup: true);
    }

    private async Task LoadSelectedInstanceAsync(bool reanalyzeBackup)
    {
        if (instanceComboBox.SelectedItem is not SqlInstanceInfo instance)
            return;

        if (!instance.IsRunning)
        {
            _instancePaths = null;
            databaseComboBox.DataSource = null;
            dataPathValueLabel.Text = "Instance is not running";
            logPathValueLabel.Text = "Instance is not running";
            SetStatus($"{instance.ServerName} is {instance.Status}. Start SQL Server service '{instance.ServiceName}'.");
            UpdateRestoreAvailability();
            return;
        }

        _loading = true;
        SetBusyUi(true, $"Connecting to {instance.ServerName}...");
        try
        {
            await _connectionService.TestConnectionAsync(instance.ServerName);
            var dbTask = _databaseService.GetDatabasesAsync(instance.ServerName, showSystemDatabasesCheckBox.Checked);
            var pathTask = _databaseService.GetInstancePathsAsync(instance.ServerName);
            await Task.WhenAll(dbTask, pathTask);

            var databases = await dbTask;
            _instancePaths = await pathTask;

            databaseComboBox.DataSource = null;
            databaseComboBox.DataSource = databases.ToList();
            databaseComboBox.DisplayMember = nameof(DatabaseInfo.Name);

            dataPathValueLabel.Text = _instancePaths.DataPath;
            logPathValueLabel.Text = _instancePaths.LogPath;

            if (!string.IsNullOrWhiteSpace(_configuration.LastDatabase))
            {
                var match = databases.FirstOrDefault(d =>
                    string.Equals(d.Name, _configuration.LastDatabase, StringComparison.OrdinalIgnoreCase));
                if (match is not null) databaseComboBox.SelectedItem = match;
            }

            LoggingService.Info($"Selected local SQL instance: {instance.ServerName}");
            SetStatus("Local SQL Server connected. Select a backup.");
        }
        catch (Exception ex)
        {
            _instancePaths = null;
            LoggingService.Error($"Could not configure instance {instance.ServerName}", ex);
            ShowError($"Could not connect/configure {instance.ServerName}.", ex);
            SetStatus("SQL Server connection/configuration failed.");
        }
        finally
        {
            _loading = false;
            SetBusyUi(false);
        }

        if (reanalyzeBackup && File.Exists(backupTextBox.Text.Trim()))
            await AnalyzeBackupAsync();
    }

    private async void ShowSystemDatabasesCheckBox_CheckedChanged(object? sender, EventArgs e)
    {
        if (_loading || _busy) return;
        await LoadSelectedInstanceAsync(reanalyzeBackup: false);
    }

    private async void BrowseButton_Click(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Select SQL Server Backup",
            Filter = "SQL Server backup or ZIP (*.bak;*.zip)|*.bak;*.zip|SQL Server backup (*.bak)|*.bak|ZIP archive (*.zip)|*.zip",
            CheckFileExists = true,
            Multiselect = false,
            RestoreDirectory = true
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            backupTextBox.Text = dialog.FileName;
            await AnalyzeBackupAsync();
        }
    }

    private void BackupTextBox_TextChanged(object? sender, EventArgs e)
    {
        if (_loading) return;
        InvalidateBackupAnalysis();
        UpdateRestoreAvailability();
    }

    private async void BackupTextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            e.SuppressKeyPress = true;
            await AnalyzeBackupAsync();
        }
    }

    private async Task AnalyzeBackupAsync()
    {
        if (_busy) return;
        if (instanceComboBox.SelectedItem is not SqlInstanceInfo instance || !instance.IsRunning)
        {
            MessageBox.Show("Select a running local SQL Server instance first.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (_instancePaths is null)
        {
            MessageBox.Show("SQL Server DATA/LOG paths are not configured yet.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var source = backupTextBox.Text.Trim();
        if (!File.Exists(source))
        {
            MessageBox.Show("The selected backup file does not exist.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _busy = true;
        SetBusyUi(true, "Preparing backup...");
        restoreProgressBar.Value = 0;
        DisposePreparedBackup();

        try
        {
            var operationProgress = new Progress<string>(SetStatus);
            _preparedBackup = await _backupService.PrepareBackupAsync(source, SelectBakFromZipAsync, operationProgress);
            _backupInfo = await _backupService.ReadBackupInfoReliableAsync(
                instance.ServerName,
                instance.ServiceName,
                _instancePaths,
                _preparedBackup,
                operationProgress);

            _analysisServer = instance.ServerName;
            _analysisSourcePath = Path.GetFullPath(source);

            if (string.IsNullOrWhiteSpace(_backupInfo.OriginalDatabaseName))
                throw new InvalidDataException("The database name could not be read from the SQL backup.");

            databaseComboBox.Text = _backupInfo.OriginalDatabaseName;
            var exists = await _databaseService.DatabaseExistsAsync(instance.ServerName, _backupInfo.OriginalDatabaseName);

            var sizeMb = _backupInfo.Files.Sum(f => Math.Max(0L, f.SizeBytes)) / (1024d * 1024d);
            backupInfoLabel.Text =
                $"Backup database: {_backupInfo.OriginalDatabaseName} • {sizeMb:0.#} MB logical files • " +
                (exists ? "Database exists — will replace after confirmation" : "Database not present — will create");

            databaseHintLabel.Text = exists
                ? $"Auto target: {_backupInfo.OriginalDatabaseName} already exists on {instance.ServerName}."
                : $"Auto target: {_backupInfo.OriginalDatabaseName} will be created on {instance.ServerName}.";

            SetStatus("Backup ready. Click RESTORE DATABASE.");
            LoggingService.Info($"Backup analysis completed. Source={source}; Database={_backupInfo.OriginalDatabaseName}; Instance={instance.ServerName}");
        }
        catch (OperationCanceledException)
        {
            SetStatus("Backup selection cancelled.");
            InvalidateBackupAnalysis();
        }
        catch (Exception ex)
        {
            LoggingService.Error($"Backup analysis failed: {source}", ex);
            ShowError("Backup could not be prepared/read by the selected local SQL Server.", ex);
            SetStatus("Backup analysis failed.");
            InvalidateBackupAnalysis();
        }
        finally
        {
            _busy = false;
            SetBusyUi(false);
            UpdateRestoreAvailability();
        }
    }

    private async void RestoreButton_Click(object? sender, EventArgs e)
    {
        if (_busy) return;
        if (instanceComboBox.SelectedItem is not SqlInstanceInfo instance || !instance.IsRunning || _instancePaths is null)
            return;

        if (!AnalysisMatchesCurrentSelection())
        {
            await AnalyzeBackupAsync();
            if (!AnalysisMatchesCurrentSelection()) return;
        }

        var backupInfo = _backupInfo!;
        var prepared = _preparedBackup!;
        var destinationDatabase = databaseComboBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(destinationDatabase))
        {
            MessageBox.Show("Target database name is empty.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (!string.Equals(destinationDatabase, backupInfo.OriginalDatabaseName, StringComparison.OrdinalIgnoreCase))
        {
            var mismatch = MessageBox.Show(
                $"Backup database: {backupInfo.OriginalDatabaseName}\n\n" +
                $"Target database: {destinationDatabase}\n\n" +
                "For the safest automatic restore, the target should normally match the backup database name.\n\n" +
                "Continue with the different target name?",
                "Database Name Mismatch",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (mismatch != DialogResult.Yes) return;
        }

        var exists = await _databaseService.DatabaseExistsAsync(instance.ServerName, destinationDatabase);
        if (exists && !replaceCheckBox.Checked)
        {
            MessageBox.Show("The target database already exists. Enable 'Replace existing database'.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (!exists && !createIfMissingCheckBox.Checked)
        {
            MessageBox.Show("The target database does not exist. Enable 'Create database from backup'.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var confirmationText =
            $"LOCAL SQL SERVER:\n{instance.ServerName}\n\n" +
            $"BACKUP DATABASE:\n{backupInfo.OriginalDatabaseName}\n\n" +
            $"TARGET DATABASE:\n{destinationDatabase}\n\n" +
            $"SQL DATA LOCATION:\n{_instancePaths.DataPath}\n\n" +
            $"SQL LOG LOCATION:\n{_instancePaths.LogPath}\n\n" +
            (exists ? "The existing database will be REPLACED.\n\n" : "A new database will be CREATED.\n\n") +
            "Continue?";

        if (MessageBox.Show(
                confirmationText,
                "Confirm Local SQL Restore",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;

        _busy = true;
        SetBusyUi(true, "Starting restore...");
        restoreProgressBar.Value = 0;

        try
        {
            var progress = new Progress<RestoreProgressInfo>(p =>
            {
                restoreProgressBar.Value = Math.Clamp(p.Percent, 0, 100);
                SetStatus(p.Message);
            });

            var request = new RestoreRequest
            {
                ServerName = instance.ServerName,
                DestinationDatabase = destinationDatabase,
                BackupPath = prepared.BakPath,
                ReplaceExistingDatabase = replaceCheckBox.Checked,
                CreateDatabaseIfMissing = createIfMissingCheckBox.Checked,
                BackupInfo = backupInfo,
                InstancePaths = _instancePaths
            };

            var verification = await _restoreService.RestoreAsync(request, progress);
            restoreProgressBar.Value = 100;
            SetStatus("Restore completed and verified in local SQL Server.");

            _configuration.LastInstance = instance.ServerName;
            _configuration.LastDatabase = destinationDatabase;
            SaveConfiguration();

            var files = string.Join(Environment.NewLine, verification.PhysicalFiles.Select(path => "• " + path));
            MessageBox.Show(
                $"DATABASE RESTORED AND VERIFIED.\n\n" +
                $"Instance:\n{instance.ServerName}\n\n" +
                $"Database:\n{destinationDatabase}\n\n" +
                $"State: {verification.StateDescription}\n" +
                $"User tables detected: {verification.UserTableCount}\n\n" +
                $"SQL Server physical files:\n{files}",
                "Restore Verified",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            await LoadSelectedInstanceAsync(reanalyzeBackup: false);
            databaseComboBox.Text = destinationDatabase;
        }
        catch (Exception ex)
        {
            restoreProgressBar.Value = 0;
            SetStatus("Restore failed or verification failed.");
            LoggingService.Error(
                $"Restore failure. Instance={instance.ServerName}; Database={destinationDatabase}; Backup={prepared.BakPath}", ex);
            ShowError("Database was NOT accepted as successfully restored.", ex);
        }
        finally
        {
            _busy = false;
            SetBusyUi(false);
            DisposePreparedBackup();
            _backupInfo = null;
            _analysisServer = null;
            _analysisSourcePath = null;
            UpdateRestoreAvailability();
        }
    }

    private void RegisterFileHandlerButton_Click(object? sender, EventArgs e)
    {
        try
        {
            FileAssociationService.RegisterForCurrentUser();
            MessageBox.Show(
                ".BAK double-click is now linked to JEWEL One Click Restore.\n\n" +
                ".ZIP files also get a 'Restore with JEWEL One Click Restore' right-click option.\n\n" +
                "The app accepts both .BAK and .ZIP paths directly and works offline.",
                "Backup File Handler Enabled",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            LoggingService.Error("File association registration failed", ex);
            ShowError("Could not register the backup file handler.", ex);
        }
    }

    private void DatabaseComboBox_TextChanged(object? sender, EventArgs e)
        => UpdateRestoreAvailability();

    private bool AnalysisMatchesCurrentSelection()
    {
        if (_backupInfo is null || _preparedBackup is null || _instancePaths is null)
            return false;
        if (instanceComboBox.SelectedItem is not SqlInstanceInfo instance)
            return false;
        if (!string.Equals(_analysisServer, instance.ServerName, StringComparison.OrdinalIgnoreCase))
            return false;
        if (!File.Exists(backupTextBox.Text.Trim()))
            return false;
        return string.Equals(
            _analysisSourcePath,
            Path.GetFullPath(backupTextBox.Text.Trim()),
            StringComparison.OrdinalIgnoreCase);
    }

    private void InvalidateBackupAnalysis()
    {
        DisposePreparedBackup();
        _backupInfo = null;
        _analysisServer = null;
        _analysisSourcePath = null;
        backupInfoLabel.Text = "Choose a backup. ZIP is extracted automatically; no internet is used.";
    }

    private void DisposePreparedBackup()
    {
        try { _preparedBackup?.Dispose(); } catch { }
        _preparedBackup = null;
    }

    private Task<string?> SelectBakFromZipAsync(IReadOnlyList<string> files)
    {
        if (InvokeRequired)
        {
            var tcs = new TaskCompletionSource<string?>();
            BeginInvoke(new Action(() =>
            {
                try
                {
                    using var dialog = new BakSelectionForm(files);
                    tcs.SetResult(dialog.ShowDialog(this) == DialogResult.OK ? dialog.SelectedBakPath : null);
                }
                catch (Exception ex) { tcs.SetException(ex); }
            }));
            return tcs.Task;
        }

        using var form = new BakSelectionForm(files);
        return Task.FromResult(form.ShowDialog(this) == DialogResult.OK ? form.SelectedBakPath : null);
    }

    private void UpdateRestoreAvailability()
    {
        var running = instanceComboBox.SelectedItem is SqlInstanceInfo instance && instance.IsRunning;
        restoreButton.Enabled = !_busy && running && AnalysisMatchesCurrentSelection() &&
                                !string.IsNullOrWhiteSpace(databaseComboBox.Text);
    }

    private void SetBusyUi(bool busy, string? status = null)
    {
        instanceComboBox.Enabled = !busy;
        refreshInstancesButton.Enabled = !busy;
        databaseComboBox.Enabled = !busy;
        showSystemDatabasesCheckBox.Enabled = !busy;
        backupTextBox.Enabled = !busy;
        browseButton.Enabled = !busy;
        replaceCheckBox.Enabled = !busy;
        createIfMissingCheckBox.Enabled = !busy;
        registerFileHandlerButton.Enabled = !busy;
        if (!string.IsNullOrWhiteSpace(status)) SetStatus(status);
        UpdateRestoreAvailability();
    }

    private void SetStatus(string message)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => SetStatus(message)));
            return;
        }
        statusLabel.Text = message;
    }

    private void ShowError(string heading, Exception ex)
    {
        var root = ex;
        while (root.InnerException is not null) root = root.InnerException;

        var message = root.Message;
        if (root is SqlException sqlEx)
        {
            var lower = sqlEx.Message.ToLowerInvariant();
            if (lower.Contains("login failed") || lower.Contains("permission"))
                message = "The Windows user does not have sufficient SQL Server restore permission.\n\n" + sqlEx.Message;
            else if (lower.Contains("cannot open backup device") || lower.Contains("operating system error 5"))
                message = "SQL Server cannot read the backup path. The app tried local permission/staging fallback.\n\n" + sqlEx.Message;
            else if (lower.Contains("version") && (lower.Contains("incompatible") || lower.Contains("earlier version")))
                message = "This backup cannot be restored to the selected SQL Server version.\n\n" + sqlEx.Message;
            else if (lower.Contains("exclusive access") || lower.Contains("currently in use"))
                message = "SQL Server could not get exclusive access to the target database.\n\n" + sqlEx.Message;
        }

        MessageBox.Show(
            $"{heading}\n\n{message}\n\nFull technical details are in the daily log.",
            Text,
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }

    private void ApplyConfigurationToUi()
    {
        replaceCheckBox.Checked = _configuration.ReplaceExistingDatabase;
        createIfMissingCheckBox.Checked = _configuration.CreateDatabaseIfMissing;
        showSystemDatabasesCheckBox.Checked = _configuration.ShowSystemDatabases;

        var width = Math.Max(MinimumSize.Width, _configuration.WindowWidth);
        var height = Math.Max(MinimumSize.Height, _configuration.WindowHeight);
        Size = new Size(width, height);

        if (_configuration.WindowX >= 0 && _configuration.WindowY >= 0)
        {
            var bounds = new Rectangle(_configuration.WindowX, _configuration.WindowY, width, height);
            if (ConfigurationService.IsVisibleOnAnyScreen(bounds))
                Location = new Point(_configuration.WindowX, _configuration.WindowY);
            else
                StartPosition = FormStartPosition.CenterScreen;
        }
        else
        {
            StartPosition = FormStartPosition.CenterScreen;
        }
    }

    private void SaveConfiguration()
    {
        _configuration.ReplaceExistingDatabase = replaceCheckBox.Checked;
        _configuration.CreateDatabaseIfMissing = createIfMissingCheckBox.Checked;
        _configuration.ShowSystemDatabases = showSystemDatabasesCheckBox.Checked;
        _configuration.LastInstance = (instanceComboBox.SelectedItem as SqlInstanceInfo)?.ServerName;
        _configuration.LastDatabase = databaseComboBox.Text.Trim();

        if (WindowState == FormWindowState.Normal)
        {
            _configuration.WindowWidth = Width;
            _configuration.WindowHeight = Height;
            _configuration.WindowX = Left;
            _configuration.WindowY = Top;
        }
        _configurationService.Save(_configuration);
    }

    private void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_busy)
        {
            var answer = MessageBox.Show(
                "A SQL restore operation is running. Closing the application can interrupt it.\n\nClose anyway?",
                "Restore In Progress",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes)
            {
                e.Cancel = true;
                return;
            }
        }
        SaveConfiguration();
    }
}
