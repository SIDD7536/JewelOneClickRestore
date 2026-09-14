using System.IO.Compression;
using JewelOneClickRestore.Models;
using Microsoft.Data.SqlClient;

namespace JewelOneClickRestore.Services;

public sealed class PreparedBackup : IDisposable
{
    public string SourcePath { get; init; } = string.Empty;
    public string BakPath { get; set; } = string.Empty;
    public string? TemporaryDirectory { get; init; }
    public string? StagedCopyPath { get; set; }
    public IReadOnlyList<string> ExtractedBakFiles { get; init; } = Array.Empty<string>();

    public void Dispose()
    {
        if (!string.IsNullOrWhiteSpace(StagedCopyPath))
        {
            try
            {
                if (File.Exists(StagedCopyPath)) File.Delete(StagedCopyPath);
                LoggingService.Info($"Deleted staged SQL-readable backup: {StagedCopyPath}");
            }
            catch (Exception ex)
            {
                LoggingService.Warn($"Could not delete staged backup '{StagedCopyPath}': {ex.Message}");
            }
        }

        if (!string.IsNullOrWhiteSpace(TemporaryDirectory))
        {
            try
            {
                if (Directory.Exists(TemporaryDirectory)) Directory.Delete(TemporaryDirectory, true);
                LoggingService.Info($"Temporary ZIP extraction cleaned: {TemporaryDirectory}");
            }
            catch (Exception ex)
            {
                LoggingService.Warn($"Could not clean temporary extraction folder '{TemporaryDirectory}': {ex.Message}");
            }
        }
    }
}

public sealed class BackupService
{
    private readonly SqlConnectionService _connectionService;

    public BackupService(SqlConnectionService connectionService)
    {
        _connectionService = connectionService;
    }

    public async Task<PreparedBackup> PrepareBackupAsync(
        string sourcePath,
        Func<IReadOnlyList<string>, Task<string?>> selectBakAsync,
        IProgress<string>? operationProgress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            throw new ArgumentException("Select a .BAK or .ZIP backup file.");

        var fullPath = Path.GetFullPath(sourcePath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("The selected backup file does not exist.", fullPath);

        var extension = Path.GetExtension(fullPath);
        if (extension.Equals(".bak", StringComparison.OrdinalIgnoreCase))
        {
            return new PreparedBackup
            {
                SourcePath = fullPath,
                BakPath = fullPath
            };
        }

        if (!extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Only .BAK and .ZIP files are supported.");

        operationProgress?.Report("Extracting ZIP locally...");
        var tempRoot = Path.Combine(
            Path.GetTempPath(),
            "JewelOneClickRestore",
            $"{DateTime.Now:yyyyMMddHHmmss}-{Guid.NewGuid():N}");

        Directory.CreateDirectory(tempRoot);
        LoggingService.Info($"ZIP extraction start: {fullPath} -> {tempRoot}");

        try
        {
            await Task.Run(() => ZipFile.ExtractToDirectory(fullPath, tempRoot), cancellationToken);

            var bakFiles = Directory
                .EnumerateFiles(tempRoot, "*.bak", SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .ThenBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (bakFiles.Count == 0)
                throw new InvalidDataException("No .BAK file found inside ZIP.");

            string? chosen;
            if (bakFiles.Count == 1)
            {
                chosen = bakFiles[0];
            }
            else
            {
                chosen = await selectBakAsync(bakFiles);
                if (string.IsNullOrWhiteSpace(chosen))
                    throw new OperationCanceledException("Backup selection was cancelled.");
            }

            LoggingService.Info($"ZIP extraction complete. Selected BAK: {chosen}");
            return new PreparedBackup
            {
                SourcePath = fullPath,
                BakPath = chosen,
                TemporaryDirectory = tempRoot,
                ExtractedBakFiles = bakFiles
            };
        }
        catch
        {
            try
            {
                if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true);
            }
            catch { }
            throw;
        }
    }

    public async Task<BackupFileInfo> ReadBackupInfoReliableAsync(
        string serverName,
        string sqlServiceName,
        SqlInstancePaths sqlPaths,
        PreparedBackup prepared,
        IProgress<string>? operationProgress = null,
        CancellationToken cancellationToken = default)
    {
        operationProgress?.Report("Reading backup metadata...");

        FolderPermissionService.TryGrantReadAccess(prepared.BakPath, sqlServiceName);

        try
        {
            return await ReadBackupInfoAsync(serverName, prepared.BakPath, cancellationToken);
        }
        catch (SqlException ex) when (LooksLikeBackupFileAccessProblem(ex))
        {
            LoggingService.Warn($"SQL Server could not read original backup path. Staging locally. {ex.Message}");
            operationProgress?.Report("Preparing backup for local SQL Server...");

            var staged = await StageBackupForSqlServerAsync(
                prepared.BakPath,
                sqlServiceName,
                sqlPaths,
                operationProgress,
                cancellationToken);

            prepared.StagedCopyPath = staged;
            prepared.BakPath = staged;
            return await ReadBackupInfoAsync(serverName, prepared.BakPath, cancellationToken);
        }
    }

    private static bool LooksLikeBackupFileAccessProblem(SqlException ex)
    {
        var text = ex.Message.ToLowerInvariant();
        return ex.Number == 3201 ||
               text.Contains("cannot open backup device") ||
               text.Contains("operating system error 5") ||
               text.Contains("access is denied") ||
               text.Contains("system cannot find the file specified");
    }

    private static async Task<string> StageBackupForSqlServerAsync(
        string sourceBak,
        string serviceName,
        SqlInstancePaths paths,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var preferredRoot = !string.IsNullOrWhiteSpace(paths.BackupPath)
            ? paths.BackupPath
            : paths.DataPath;

        var stagingFolder = Path.Combine(preferredRoot, "JewelOneClickRestore");
        Directory.CreateDirectory(stagingFolder);
        FolderPermissionService.TryGrantModifyAccess(stagingFolder, serviceName);

        var stagedPath = Path.Combine(
            stagingFolder,
            $"JewelRestore-{DateTime.Now:yyyyMMddHHmmss}-{Guid.NewGuid():N}.bak");

        progress?.Report("Copying backup to SQL Server local staging folder...");

        const int bufferSize = 1024 * 1024;
        await using var input = new FileStream(
            sourceBak, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var output = new FileStream(
            stagedPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            bufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);

        await input.CopyToAsync(output, bufferSize, cancellationToken);
        await output.FlushAsync(cancellationToken);

        FolderPermissionService.TryGrantReadAccess(stagedPath, serviceName);
        LoggingService.Info($"Backup staged for SQL Server: {sourceBak} -> {stagedPath}");
        return stagedPath;
    }

    public async Task<BackupFileInfo> ReadBackupInfoAsync(
        string serverName,
        string bakPath,
        CancellationToken cancellationToken = default)
    {
        LoggingService.Info($"RESTORE HEADERONLY / FILELISTONLY start: {bakPath}");

        await using var connection = _connectionService.CreateMasterConnection(serverName, 15);
        await connection.OpenAsync(cancellationToken);

        string originalDatabaseName = string.Empty;

        var headerSql = $"RESTORE HEADERONLY FROM DISK = {SqlText.StringLiteral(bakPath)};";
        await using (var command = new SqlCommand(headerSql, connection) { CommandTimeout = 0 })
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            var databaseNameOrdinal = TryGetOrdinal(reader, "DatabaseName");
            if (databaseNameOrdinal >= 0 && await reader.ReadAsync(cancellationToken))
            {
                originalDatabaseName = reader.IsDBNull(databaseNameOrdinal)
                    ? string.Empty
                    : Convert.ToString(reader.GetValue(databaseNameOrdinal)) ?? string.Empty;
            }
        }

        var files = new List<RestoreFileInfo>();
        var fileListSql = $"RESTORE FILELISTONLY FROM DISK = {SqlText.StringLiteral(bakPath)};";
        await using (var command = new SqlCommand(fileListSql, connection) { CommandTimeout = 0 })
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            var logicalOrdinal = reader.GetOrdinal("LogicalName");
            var physicalOrdinal = reader.GetOrdinal("PhysicalName");
            var typeOrdinal = reader.GetOrdinal("Type");
            var sizeOrdinal = TryGetOrdinal(reader, "Size");

            while (await reader.ReadAsync(cancellationToken))
            {
                files.Add(new RestoreFileInfo
                {
                    LogicalName = Convert.ToString(reader.GetValue(logicalOrdinal)) ?? string.Empty,
                    PhysicalName = Convert.ToString(reader.GetValue(physicalOrdinal)) ?? string.Empty,
                    Type = Convert.ToString(reader.GetValue(typeOrdinal)) ?? string.Empty,
                    SizeBytes = sizeOrdinal >= 0 && !reader.IsDBNull(sizeOrdinal)
                        ? Convert.ToInt64(reader.GetValue(sizeOrdinal))
                        : 0L
                });
            }
        }

        if (files.Count == 0)
            throw new InvalidDataException("The backup contains no database files.");

        LoggingService.Info(
            $"Backup metadata: Database='{originalDatabaseName}', Files={string.Join(" | ", files.Select(f => $"{f.LogicalName}:{f.Type}:{f.PhysicalName}"))}");

        return new BackupFileInfo
        {
            BackupPath = bakPath,
            OriginalDatabaseName = originalDatabaseName,
            Files = files
        };
    }

    private static int TryGetOrdinal(SqlDataReader reader, string column)
    {
        for (var i = 0; i < reader.FieldCount; i++)
            if (string.Equals(reader.GetName(i), column, StringComparison.OrdinalIgnoreCase))
                return i;
        return -1;
    }
}

internal static class SqlText
{
    public static string Identifier(string value)
        => "[" + value.Replace("]", "]]", StringComparison.Ordinal) + "]";

    public static string StringLiteral(string value)
        => "N'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
}
