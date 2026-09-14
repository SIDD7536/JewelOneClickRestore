using Microsoft.Data.SqlClient;

namespace JewelOneClickRestore.Services;

public sealed class SqlConnectionService
{
    public string BuildConnectionString(string serverName, string databaseName = "master", int timeoutSeconds = 10)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = serverName,
            InitialCatalog = databaseName,
            IntegratedSecurity = true,
            TrustServerCertificate = true,
            ConnectTimeout = timeoutSeconds,
            ApplicationName = "JEWEL One Click Restore",
            Pooling = true
        };

        return builder.ConnectionString;
    }

    public string BuildMasterConnectionString(string serverName, int timeoutSeconds = 10)
        => BuildConnectionString(serverName, "master", timeoutSeconds);

    public async Task TestConnectionAsync(string serverName, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(BuildMasterConnectionString(serverName));
        await connection.OpenAsync(cancellationToken);
    }

    public SqlConnection CreateMasterConnection(string serverName, int timeoutSeconds = 10)
        => new(BuildMasterConnectionString(serverName, timeoutSeconds));

    public SqlConnection CreateDatabaseConnection(string serverName, string databaseName, int timeoutSeconds = 10)
        => new(BuildConnectionString(serverName, databaseName, timeoutSeconds));
}
