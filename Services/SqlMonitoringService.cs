using Microsoft.Data.SqlClient;

namespace SqlTransactionMonitor.Services;

public sealed class SqlMonitoringService
{
    private SqlConnection? _connection;

    public bool IsConnected => _connection?.State == System.Data.ConnectionState.Open;

    public async Task ConnectAsync(string server, string database, string userName, string password, bool integratedSecurity, CancellationToken token)
    {
        await DisconnectAsync();
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = server,
            InitialCatalog = database,
            IntegratedSecurity = integratedSecurity,
            Encrypt = true,
            TrustServerCertificate = true,
            ConnectTimeout = 15,
            ApplicationName = "SQL Transaction Monitor"
        };
        if (!integratedSecurity)
        {
            builder.UserID = userName;
            builder.Password = password;
        }

        _connection = new SqlConnection(builder.ConnectionString);
        await _connection.OpenAsync(token);
        _ = await GetCounterValueAsync(database, token); // Validate counter visibility now.
    }

    public async Task<long> GetCounterValueAsync(string database, CancellationToken token)
    {
        if (_connection is null || _connection.State != System.Data.ConnectionState.Open)
            throw new InvalidOperationException("Not connected to SQL Server.");

        const string sql = """
            SELECT TOP (1) cntr_value
            FROM sys.dm_os_performance_counters
            WHERE counter_name = N'Transactions/sec'
              AND instance_name = @database
              AND RTRIM(object_name) LIKE N'%:Databases';
            """;
        await using var command = new SqlCommand(sql, _connection);
        command.Parameters.AddWithValue("@database", database);
        var result = await command.ExecuteScalarAsync(token);
        if (result is null || result is DBNull)
            throw new InvalidOperationException($"The Transactions/sec counter was not found for database '{database}'. Verify the database name and VIEW SERVER PERFORMANCE STATE/VIEW SERVER STATE permissions.");
        return Convert.ToInt64(result);
    }

    public async Task DisconnectAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
            _connection = null;
        }
    }
}
