using Microsoft.Data.Sqlite;
using SqlTransactionMonitor.Models;
using System.IO;


namespace SqlTransactionMonitor.Services;

public sealed class HistoryRepository
{
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private bool _initialized;

    public string DatabasePath { get; } = CreateDatabasePath();

    private string ConnectionString =>
        new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();

    private static string CreateDatabasePath()
    {
        string folderPath = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "SqlTransactionMonitor");

        Directory.CreateDirectory(folderPath);

        return Path.Combine(folderPath, "transactions.db");
    }

    public async Task InitializeAsync()
    {
        if (_initialized)
            return;

        await _initializationLock.WaitAsync();

        try
        {
            if (_initialized)
                return;

            AppLogger.Information(
                nameof(HistoryRepository),
                $"Initializing SQLite database. Path={DatabasePath}");

            Directory.CreateDirectory(
                Path.GetDirectoryName(DatabasePath)!);

            await using var connection =
                new SqliteConnection(ConnectionString);

            await connection.OpenAsync();

            await using var command = connection.CreateCommand();

            command.CommandText = """
                CREATE TABLE IF NOT EXISTS Samples
                (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ServerName TEXT NOT NULL,
                    DatabaseName TEXT NOT NULL,
                    TimestampUtc TEXT NOT NULL,
                    TransactionsPerMinute REAL NOT NULL,
                    CounterValue INTEGER NOT NULL
                );

                CREATE INDEX IF NOT EXISTS IX_Samples_Target_Time
                ON Samples
                (
                    ServerName,
                    DatabaseName,
                    TimestampUtc
                );
                """;

            await command.ExecuteNonQueryAsync();

            _initialized = true;

            AppLogger.Information(
                nameof(HistoryRepository),
                "SQLite database and Samples schema initialized.");

            await PurgeOldInternalAsync();
        }
        catch (Exception ex)
        {
            _initialized = false;

            AppLogger.Error(
                nameof(HistoryRepository),
                $"SQLite initialization failed. Path={DatabasePath}",
                ex);

            throw;
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    public async Task AddAsync(
        string server,
        string database,
        TransactionSample sample)
    {
        await InitializeAsync();

        try
        {
            await using var connection =
                new SqliteConnection(ConnectionString);

            await connection.OpenAsync();

            await using var command = connection.CreateCommand();

            command.CommandText = """
                INSERT INTO Samples
                (
                    ServerName,
                    DatabaseName,
                    TimestampUtc,
                    TransactionsPerMinute,
                    CounterValue
                )
                VALUES
                (
                    $server,
                    $database,
                    $time,
                    $tpm,
                    $counter
                );
                """;

            command.Parameters.AddWithValue("$server", server);
            command.Parameters.AddWithValue("$database", database);
            command.Parameters.AddWithValue(
                "$time",
                sample.TimestampUtc.ToString("O"));
            command.Parameters.AddWithValue(
                "$tpm",
                sample.TransactionsPerMinute);
            command.Parameters.AddWithValue(
                "$counter",
                sample.CounterValue);

            await command.ExecuteNonQueryAsync();

            AppLogger.Debug(
                nameof(HistoryRepository),
                $"Sample stored. Server={server}, Database={database}, " +
                $"TPM={sample.TransactionsPerMinute:F3}, " +
                $"Counter={sample.CounterValue}");
        }
        catch (Exception ex)
        {
            AppLogger.Error(
                nameof(HistoryRepository),
                $"Unable to store sample. Server={server}, Database={database}",
                ex);

            throw;
        }
    }

    public async Task<List<TransactionSample>> LoadAsync(
        string server,
        string database)
    {
        await InitializeAsync();

        var result = new List<TransactionSample>();

        try
        {
            await using var connection =
                new SqliteConnection(ConnectionString);

            await connection.OpenAsync();

            await using var command = connection.CreateCommand();

            command.CommandText = """
                SELECT
                    TimestampUtc,
                    TransactionsPerMinute,
                    CounterValue
                FROM Samples
                WHERE ServerName = $server
                  AND DatabaseName = $database
                  AND TimestampUtc >= $cutoff
                ORDER BY TimestampUtc;
                """;

            command.Parameters.AddWithValue("$server", server);
            command.Parameters.AddWithValue("$database", database);
            command.Parameters.AddWithValue(
                "$cutoff",
                DateTime.UtcNow.AddDays(-7).ToString("O"));

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                result.Add(new TransactionSample
                {
                    TimestampUtc = DateTime.Parse(
                        reader.GetString(0),
                        null,
                        System.Globalization.DateTimeStyles.RoundtripKind),

                    TransactionsPerMinute = reader.GetDouble(1),
                    CounterValue = reader.GetInt64(2)
                });
            }

            AppLogger.Information(
                nameof(HistoryRepository),
                $"History loaded. Server={server}, Database={database}, " +
                $"Rows={result.Count}");

            return result;
        }
        catch (Exception ex)
        {
            AppLogger.Error(
                nameof(HistoryRepository),
                $"Unable to load history. Server={server}, Database={database}",
                ex);

            throw;
        }
    }

    public async Task PurgeOldAsync()
    {
        await InitializeAsync();
        await PurgeOldInternalAsync();
    }

    private async Task PurgeOldInternalAsync()
    {
        try
        {
            await using var connection =
                new SqliteConnection(ConnectionString);

            await connection.OpenAsync();

            await using var command = connection.CreateCommand();

            command.CommandText = """
                DELETE FROM Samples
                WHERE TimestampUtc < $cutoff;
                """;

            command.Parameters.AddWithValue(
                "$cutoff",
                DateTime.UtcNow.AddDays(-7).ToString("O"));

            int deletedRows = await command.ExecuteNonQueryAsync();

            AppLogger.Information(
                nameof(HistoryRepository),
                $"SQLite retention cleanup completed. DeletedRows={deletedRows}");
        }
        catch (Exception ex)
        {
            AppLogger.Error(
                nameof(HistoryRepository),
                "SQLite retention cleanup failed.",
                ex);

            throw;
        }
    }

    public async Task ClearAllDataAsync()
    {
        await InitializeAsync();

        try
        {
            await using var connection =
                new SqliteConnection(ConnectionString);

            await connection.OpenAsync();

            await using var transaction =
                await connection.BeginTransactionAsync();

            try
            {
                await using var deleteCommand =
                    connection.CreateCommand();

                deleteCommand.Transaction =
                    (SqliteTransaction)transaction;

                deleteCommand.CommandText =
                    "DELETE FROM Samples;";

                int deletedRows =
                    await deleteCommand.ExecuteNonQueryAsync();

                await using var sequenceCommand =
                    connection.CreateCommand();

                sequenceCommand.Transaction =
                    (SqliteTransaction)transaction;

                sequenceCommand.CommandText = """
                    DELETE FROM sqlite_sequence
                    WHERE name = 'Samples';
                    """;

                await sequenceCommand.ExecuteNonQueryAsync();
                await transaction.CommitAsync();

                AppLogger.Warning(
                    nameof(HistoryRepository),
                    $"All transaction history cleared. " +
                    $"DeletedRows={deletedRows}");
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error(
                nameof(HistoryRepository),
                "Unable to clear SQLite history.",
                ex);

            throw;
        }
    }
}