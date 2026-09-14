using JewelOneClickRestore.Models;
using Microsoft.Data.SqlClient;

namespace JewelOneClickRestore.Services;

public sealed class DatabaseService
{
    private readonly SqlConnectionService _connectionService;

    public DatabaseService(SqlConnectionService connectionService)
    {
        _connectionService = connectionService;
    }

    public async Task<IReadOnlyList<DatabaseInfo>> GetDatabasesAsync(
        string serverName,
        bool includeSystemDatabases,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT name, database_id, state_desc
            FROM sys.databases
            WHERE source_database_id IS NULL
              AND (@IncludeSystem = 1 OR database_id > 4)
            ORDER BY name;
            """;

        var result = new List<DatabaseInfo>();

        await using var connection = _connectionService.CreateMasterConnection(serverName);
        await connection.OpenAsync(cancellationToken);

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@IncludeSystem", includeSystemDatabases ? 1 : 0);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new DatabaseInfo
            {
                Name = reader.GetString(0),
                IsSystemDatabase = reader.GetInt32(1) <= 4,
                StateDescription = reader.IsDBNull(2) ? string.Empty : reader.GetString(2)
            });
        }

        LoggingService.Info($"Database list loaded from {serverName}: {string.Join(", ", result.Select(d => d.Name))}");
        return result;
    }

    public async Task<bool> DatabaseExistsAsync(
        string serverName,
        string databaseName,
        CancellationToken cancellationToken = default)
    {
        const string sql = "SELECT CASE WHEN DB_ID(@DatabaseName) IS NULL THEN 0 ELSE 1 END;";

        await using var connection = _connectionService.CreateMasterConnection(serverName);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@DatabaseName", databaseName);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }

    public async Task<SqlInstancePaths> GetInstancePathsAsync(
        string serverName,
        CancellationToken cancellationToken = default)
    {
        const string propertySql = """
            SELECT
                CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS nvarchar(4000)),
                CAST(SERVERPROPERTY('InstanceDefaultLogPath') AS nvarchar(4000)),
                CAST(SERVERPROPERTY('InstanceDefaultBackupPath') AS nvarchar(4000));
            """;

        string? dataPath = null;
        string? logPath = null;
        string? backupPath = null;

        await using var connection = _connectionService.CreateMasterConnection(serverName);
        await connection.OpenAsync(cancellationToken);

        await using (var command = new SqlCommand(propertySql, connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (await reader.ReadAsync(cancellationToken))
            {
                dataPath = reader.IsDBNull(0) ? null : reader.GetString(0);
                logPath = reader.IsDBNull(1) ? null : reader.GetString(1);
                backupPath = reader.IsDBNull(2) ? null : reader.GetString(2);
            }
        }

        if (string.IsNullOrWhiteSpace(dataPath) || string.IsNullOrWhiteSpace(logPath))
        {
            const string fallbackSql = """
                SELECT type_desc, physical_name
                FROM master.sys.database_files
                WHERE file_id IN (1, 2)
                ORDER BY file_id;
                """;

            await using var command = new SqlCommand(fallbackSql, connection);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var type = reader.GetString(0);
                var physical = reader.GetString(1);
                var folder = Path.GetDirectoryName(physical);
                if (string.IsNullOrWhiteSpace(folder))
                    continue;

                if (type.Contains("ROWS", StringComparison.OrdinalIgnoreCase))
                    dataPath ??= folder;
                else if (type.Contains("LOG", StringComparison.OrdinalIgnoreCase))
                    logPath ??= folder;
            }
        }

        if (string.IsNullOrWhiteSpace(dataPath) || string.IsNullOrWhiteSpace(logPath))
            throw new InvalidOperationException("SQL Server DATA/LOG folders could not be detected automatically.");

        backupPath = string.IsNullOrWhiteSpace(backupPath) ? dataPath : backupPath;

        var result = new SqlInstancePaths
        {
            DataPath = dataPath.TrimEnd(Path.DirectorySeparatorChar),
            LogPath = logPath.TrimEnd(Path.DirectorySeparatorChar),
            BackupPath = backupPath.TrimEnd(Path.DirectorySeparatorChar)
        };

        LoggingService.Info($"SQL paths for {serverName}: Data={result.DataPath}; Log={result.LogPath}; Backup={result.BackupPath}");
        return result;
    }

    public async Task<IReadOnlyDictionary<string, string>> GetPhysicalFileOwnersAsync(
        string serverName,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT mf.physical_name, DB_NAME(mf.database_id)
            FROM sys.master_files AS mf;
            """;

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await using var connection = _connectionService.CreateMasterConnection(serverName);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (reader.IsDBNull(0)) continue;
            var path = reader.GetString(0);
            var owner = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
            result[path] = owner;
        }
        return result;
    }

    public async Task<RestoreVerificationResult> VerifyRestoredDatabaseAsync(
        string serverName,
        string databaseName,
        DateTime restoreStartedUtc,
        CancellationToken cancellationToken = default)
    {
        var exists = false;
        var state = string.Empty;
        var files = new List<string>();
        DateTime? restoreDate = null;

        await using (var connection = _connectionService.CreateMasterConnection(serverName))
        {
            await connection.OpenAsync(cancellationToken);

            const string dbSql = """
                SELECT state_desc
                FROM sys.databases
                WHERE name = @DatabaseName;
                """;
            await using (var command = new SqlCommand(dbSql, connection))
            {
                command.Parameters.AddWithValue("@DatabaseName", databaseName);
                var scalar = await command.ExecuteScalarAsync(cancellationToken);
                if (scalar is not null && scalar != DBNull.Value)
                {
                    exists = true;
                    state = Convert.ToString(scalar) ?? string.Empty;
                }
            }

            if (exists)
            {
                const string fileSql = """
                    SELECT physical_name
                    FROM sys.master_files
                    WHERE database_id = DB_ID(@DatabaseName)
                    ORDER BY file_id;
                    """;
                await using var command = new SqlCommand(fileSql, connection);
                command.Parameters.AddWithValue("@DatabaseName", databaseName);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    if (!reader.IsDBNull(0)) files.Add(reader.GetString(0));
                }
            }

            const string historySql = """
                SELECT TOP (1) restore_date
                FROM msdb.dbo.restorehistory
                WHERE destination_database_name = @DatabaseName
                ORDER BY restore_date DESC;
                """;
            try
            {
                await using var command = new SqlCommand(historySql, connection);
                command.Parameters.AddWithValue("@DatabaseName", databaseName);
                var scalar = await command.ExecuteScalarAsync(cancellationToken);
                if (scalar is DateTime dt) restoreDate = dt;
            }
            catch (Exception ex)
            {
                LoggingService.Warn($"Could not read msdb restore history: {ex.Message}");
            }
        }

        var tableCount = -1;
        if (exists && string.Equals(state, "ONLINE", StringComparison.OrdinalIgnoreCase))
        {
            await using var dbConnection = _connectionService.CreateDatabaseConnection(serverName, databaseName, 10);
            await dbConnection.OpenAsync(cancellationToken);
            await using var command = new SqlCommand("SELECT COUNT(*) FROM sys.tables WHERE is_ms_shipped = 0;", dbConnection);
            tableCount = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        }

        var result = new RestoreVerificationResult
        {
            DatabaseExists = exists,
            StateDescription = state,
            PhysicalFiles = files,
            LatestRestoreDate = restoreDate,
            UserTableCount = tableCount
        };

        LoggingService.Info(
            $"Restore verification: DB={databaseName}; Exists={result.DatabaseExists}; State={result.StateDescription}; " +
            $"Tables={result.UserTableCount}; RestoreDate={result.LatestRestoreDate}; Files={string.Join(" | ", result.PhysicalFiles)}; " +
            $"StartedUtc={restoreStartedUtc:O}");

        return result;
    }
}
