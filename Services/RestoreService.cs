using System.Text.RegularExpressions;
using JewelOneClickRestore.Models;
using Microsoft.Data.SqlClient;

namespace JewelOneClickRestore.Services;

public sealed class RestoreProgressInfo
{
    public int Percent { get; init; }
    public string Message { get; init; } = string.Empty;
}

public sealed class RestoreRequest
{
    public string ServerName { get; init; } = string.Empty;
    public string DestinationDatabase { get; init; } = string.Empty;
    public string BackupPath { get; init; } = string.Empty;
    public bool ReplaceExistingDatabase { get; init; }
    public bool CreateDatabaseIfMissing { get; init; }
    public BackupFileInfo BackupInfo { get; init; } = new();
    public SqlInstancePaths InstancePaths { get; init; } = new();
}

public sealed class RestoreService
{
    private readonly SqlConnectionService _connectionService;
    private readonly DatabaseService _databaseService;

    public RestoreService(SqlConnectionService connectionService, DatabaseService databaseService)
    {
        _connectionService = connectionService;
        _databaseService = databaseService;
    }

    public async Task<RestoreVerificationResult> RestoreAsync(
        RestoreRequest request,
        IProgress<RestoreProgressInfo>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);

        var restoreStartedUtc = DateTime.UtcNow;
        var exists = await _databaseService.DatabaseExistsAsync(
            request.ServerName, request.DestinationDatabase, cancellationToken);

        if (exists && !request.ReplaceExistingDatabase)
            throw new InvalidOperationException("The destination database already exists. Enable 'Replace existing database'.");

        if (!exists && !request.CreateDatabaseIfMissing)
            throw new InvalidOperationException("The destination database does not exist. Enable 'Create database from backup'.");

        var owners = await _databaseService.GetPhysicalFileOwnersAsync(request.ServerName, cancellationToken);
        AssignDestinationPaths(
            request.BackupInfo.Files,
            request.DestinationDatabase,
            request.InstancePaths,
            owners);

        CheckLocalFreeSpace(request.BackupInfo.Files);

        await using var connection = _connectionService.CreateMasterConnection(request.ServerName, 15);
        connection.FireInfoMessageEventOnUserErrors = true;

        var lastPercent = 0;
        connection.InfoMessage += (_, e) =>
        {
            var text = e.Message ?? string.Empty;
            var match = Regex.Match(text, @"(?<p>\d+)\s+percent processed", RegexOptions.IgnoreCase);
            if (match.Success && int.TryParse(match.Groups["p"].Value, out var percent))
            {
                percent = Math.Clamp(percent, 0, 100);
                if (percent >= lastPercent)
                {
                    lastPercent = percent;
                    progress?.Report(new RestoreProgressInfo
                    {
                        Percent = percent,
                        Message = $"Restoring database... {percent}%"
                    });
                    LoggingService.Info($"Restore progress: {percent}%");
                }
            }
            else if (!string.IsNullOrWhiteSpace(text))
            {
                LoggingService.Info($"SQL message: {text}");
            }
        };

        await connection.OpenAsync(cancellationToken);
        var singleUserApplied = false;

        try
        {
            if (exists)
            {
                progress?.Report(new RestoreProgressInfo { Percent = 0, Message = "Closing active database connections..." });
                var singleUserSql =
                    $"ALTER DATABASE {SqlText.Identifier(request.DestinationDatabase)} " +
                    "SET SINGLE_USER WITH ROLLBACK IMMEDIATE;";
                await ExecuteNonQueryAsync(connection, singleUserSql, 60, cancellationToken);
                singleUserApplied = true;
            }

            var moveClauses = request.BackupInfo.Files.Select(file =>
                $"MOVE {SqlText.StringLiteral(file.LogicalName)} TO {SqlText.StringLiteral(file.DestinationPath)}");

            var options = new List<string>(moveClauses)
            {
                "STATS = 5"
            };

            if (exists && request.ReplaceExistingDatabase)
                options.Add("REPLACE");

            var restoreSql =
                $"RESTORE DATABASE {SqlText.Identifier(request.DestinationDatabase)}\n" +
                $"FROM DISK = {SqlText.StringLiteral(request.BackupPath)}\n" +
                "WITH " + string.Join(",\n     ", options) + ";";

            LoggingService.Info(
                $"Restore start. Instance={request.ServerName}; Destination={request.DestinationDatabase}; " +
                $"Backup={request.BackupPath}; BackupDatabase={request.BackupInfo.OriginalDatabaseName}; " +
                $"Moves={string.Join(" | ", request.BackupInfo.Files.Select(f => $"{f.LogicalName}->{f.DestinationPath}"))}");

            progress?.Report(new RestoreProgressInfo { Percent = 0, Message = "Restoring into local SQL Server..." });
            await ExecuteNonQueryAsync(connection, restoreSql, 0, cancellationToken);
        }
        finally
        {
            if (singleUserApplied)
            {
                try
                {
                    var multiUserSql =
                        $"IF DB_ID(@DatabaseName) IS NOT NULL " +
                        $"ALTER DATABASE {SqlText.Identifier(request.DestinationDatabase)} SET MULTI_USER;";
                    await using var command = new SqlCommand(multiUserSql, connection) { CommandTimeout = 60 };
                    command.Parameters.AddWithValue("@DatabaseName", request.DestinationDatabase);
                    await command.ExecuteNonQueryAsync(CancellationToken.None);
                }
                catch (Exception ex)
                {
                    LoggingService.Error($"IMPORTANT: Could not restore MULTI_USER state for {request.DestinationDatabase}", ex);
                }
            }
        }

        progress?.Report(new RestoreProgressInfo { Percent = 100, Message = "Verifying database inside SQL Server..." });

        var verification = await _databaseService.VerifyRestoredDatabaseAsync(
            request.ServerName,
            request.DestinationDatabase,
            restoreStartedUtc,
            cancellationToken);

        if (!verification.DatabaseExists)
            throw new InvalidOperationException("Restore command finished, but the database is not present in the selected SQL Server instance.");

        if (!verification.IsOnline)
            throw new InvalidOperationException($"The database was created but is not ONLINE. Current state: {verification.StateDescription}.");

        if (verification.PhysicalFiles.Count == 0)
            throw new InvalidOperationException("The database exists, but SQL Server returned no physical database files.");

        if (!verification.PhysicalFiles.All(path => IsInsideSqlLocation(path, request.InstancePaths)))
        {
            throw new InvalidOperationException(
                "The database is ONLINE, but one or more files are outside the selected SQL instance DATA/LOG folders. " +
                string.Join(" | ", verification.PhysicalFiles));
        }

        progress?.Report(new RestoreProgressInfo { Percent = 100, Message = "Restore completed and verified in SQL Server." });
        LoggingService.Info($"Restore verified successfully: {request.ServerName} / {request.DestinationDatabase}");
        return verification;
    }

    private static bool IsInsideSqlLocation(string filePath, SqlInstancePaths paths)
    {
        var full = Path.GetFullPath(filePath);
        var data = EnsureTrailingSeparator(Path.GetFullPath(paths.DataPath));
        var log = EnsureTrailingSeparator(Path.GetFullPath(paths.LogPath));
        return full.StartsWith(data, StringComparison.OrdinalIgnoreCase) ||
               full.StartsWith(log, StringComparison.OrdinalIgnoreCase);
    }

    private static string EnsureTrailingSeparator(string path)
        => path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

    private static async Task ExecuteNonQueryAsync(
        SqlConnection connection,
        string sql,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = timeoutSeconds };
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void ValidateRequest(RestoreRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ServerName))
            throw new ArgumentException("No SQL Server instance is selected.");
        if (string.IsNullOrWhiteSpace(request.DestinationDatabase))
            throw new ArgumentException("No destination database is selected.");
        if (request.DestinationDatabase.Length > 128)
            throw new ArgumentException("The database name cannot exceed 128 characters.");
        if (string.IsNullOrWhiteSpace(request.BackupPath) || !File.Exists(request.BackupPath))
            throw new FileNotFoundException("The selected .BAK file cannot be found.", request.BackupPath);
        if (request.BackupInfo.Files.Count == 0)
            throw new InvalidDataException("Backup metadata contains no database files.");
        if (string.IsNullOrWhiteSpace(request.InstancePaths.DataPath) || string.IsNullOrWhiteSpace(request.InstancePaths.LogPath))
            throw new InvalidOperationException("SQL Server DATA/LOG locations were not detected.");
    }

    private static void AssignDestinationPaths(
        IReadOnlyList<RestoreFileInfo> files,
        string databaseName,
        SqlInstancePaths paths,
        IReadOnlyDictionary<string, string> owners)
    {
        var safeName = SanitizeFileName(databaseName);
        var dataIndex = 0;
        var logIndex = 0;

        foreach (var file in files)
        {
            string candidate;
            if (file.IsLog)
            {
                logIndex++;
                var fileName = logIndex == 1 ? $"{safeName}_log.ldf" : $"{safeName}_log{logIndex}.ldf";
                candidate = Path.Combine(paths.LogPath, fileName);
            }
            else
            {
                dataIndex++;
                var fileName = dataIndex == 1 ? $"{safeName}.mdf" : $"{safeName}_{dataIndex}.ndf";
                candidate = Path.Combine(paths.DataPath, fileName);
            }

            file.DestinationPath = MakeSafeDestination(candidate, databaseName, owners);
        }
    }

    private static string MakeSafeDestination(
        string candidate,
        string destinationDatabase,
        IReadOnlyDictionary<string, string> owners)
    {
        if (owners.TryGetValue(candidate, out var owner))
        {
            if (string.Equals(owner, destinationDatabase, StringComparison.OrdinalIgnoreCase))
                return candidate;
        }
        else if (!File.Exists(candidate))
        {
            return candidate;
        }

        var folder = Path.GetDirectoryName(candidate)!;
        var name = Path.GetFileNameWithoutExtension(candidate);
        var ext = Path.GetExtension(candidate);

        for (var i = 2; i < 1000; i++)
        {
            var alternate = Path.Combine(folder, $"{name}_{i}{ext}");
            if (!owners.ContainsKey(alternate) && !File.Exists(alternate))
                return alternate;
        }

        throw new IOException($"Could not choose a safe destination file name for {candidate}.");
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        var result = new string(chars).Trim('.', ' ');
        return string.IsNullOrWhiteSpace(result) ? "RestoredDatabase" : result;
    }

    private static void CheckLocalFreeSpace(IReadOnlyList<RestoreFileInfo> files)
    {
        var grouped = files
            .Where(f => !string.IsNullOrWhiteSpace(f.DestinationPath))
            .GroupBy(f => Path.GetPathRoot(f.DestinationPath) ?? string.Empty, StringComparer.OrdinalIgnoreCase);

        foreach (var group in grouped)
        {
            if (string.IsNullOrWhiteSpace(group.Key) || group.Key.StartsWith(@"\\"))
                continue;

            var drive = new DriveInfo(group.Key);
            var required = group.Sum(f => Math.Max(0, f.SizeBytes));
            var margin = Math.Max(256L * 1024 * 1024, required / 20);
            if (drive.AvailableFreeSpace < required + margin)
            {
                throw new IOException(
                    $"Insufficient disk space on {group.Key}. Required approximately {FormatBytes(required)}; " +
                    $"available {FormatBytes(drive.AvailableFreeSpace)}.");
            }
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)Math.Max(0, bytes);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value:0.##} {units[unit]}";
    }
}
