using Microsoft.Data.SqlClient;

namespace SqlTransactionMonitor.Services;

public sealed class SqlMonitoringService
{
    private SqlConnection? _connection;

    public bool IsConnected =>
        _connection?.State == System.Data.ConnectionState.Open;

    public async Task ConnectAsync(
        string server,
        string database,
        string userName,
        string password,
        bool integratedSecurity,
        CancellationToken token)
    {
        AppLogger.Information(
            nameof(SqlMonitoringService),
            $"SQL connection requested. " +
            $"Server={server}, Database={database}, " +
            $"Authentication={(integratedSecurity ? "Windows" : "SQL")}");

        try
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

            AppLogger.Information(
                nameof(SqlMonitoringService),
                $"SQL connection opened. " +
                $"ServerVersion={_connection.ServerVersion}, " +
                $"DataSource={_connection.DataSource}, " +
                $"Database={_connection.Database}");

            _ = await GetCounterValueAsync(database, token);

            AppLogger.Information(
                nameof(SqlMonitoringService),
                $"Transactions/sec counter verified for Database={database}");
        }
        catch (Exception ex)
        {
            AppLogger.Error(
                nameof(SqlMonitoringService),
                $"SQL connection failed. Server={server}, Database={database}",
                ex);

            await DisconnectAsync();
            throw;
        }
    }

    public async Task<long> GetCounterValueAsync(
        string database,
        CancellationToken token)
    {
        if (_connection is null ||
            _connection.State != System.Data.ConnectionState.Open)
        {
            var exception = new InvalidOperationException(
                "Not connected to SQL Server.");

            AppLogger.Error(
                nameof(SqlMonitoringService),
                "Counter query attempted without an open SQL connection.",
                exception);

            throw exception;
        }

        const string sql = """
            SELECT TOP (1) cntr_value
            FROM sys.dm_os_performance_counters
            WHERE counter_name = N'Transactions/sec'
              AND instance_name = @database
              AND RTRIM(object_name) LIKE N'%:Databases';
            """;

        try
        {
            await using var command = new SqlCommand(sql, _connection);
            command.Parameters.AddWithValue("@database", database);

            object? result = await command.ExecuteScalarAsync(token);

            if (result is null || result is DBNull)
            {
                throw new InvalidOperationException(
                    $"The Transactions/sec counter was not found for " +
                    $"database '{database}'. Verify the database name and " +
                    $"the SQL login's server-state permissions.");
            }

            long counterValue = Convert.ToInt64(result);

            AppLogger.Debug(
                nameof(SqlMonitoringService),
                $"Counter read. Database={database}, Value={counterValue}");

            return counterValue;
        }
        catch (Exception ex)
        {
            AppLogger.Error(
                nameof(SqlMonitoringService),
                $"Unable to read transaction counter. Database={database}",
                ex);

            throw;
        }
    }

    public async Task DisconnectAsync()
    {
        if (_connection is null)
            return;

        try
        {
            AppLogger.Information(
                nameof(SqlMonitoringService),
                $"Closing SQL connection. DataSource={_connection.DataSource}");

            await _connection.DisposeAsync();
        }
        catch (Exception ex)
        {
            AppLogger.Error(
                nameof(SqlMonitoringService),
                "Error while disposing SQL connection.",
                ex);
        }
        finally
        {
            _connection = null;
        }
    }
}