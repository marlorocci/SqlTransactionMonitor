using Microsoft.Data.Sqlite;
using SqlTransactionMonitor.Models;
using System.IO;

namespace SqlTransactionMonitor.Services;

public sealed class HistoryRepository
{
    public string DatabasePath { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SqlTransactionMonitor", "transactions.db");
    private string ConnectionString => $"Data Source={DatabasePath}";

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS Samples (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ServerName TEXT NOT NULL,
                DatabaseName TEXT NOT NULL,
                TimestampUtc TEXT NOT NULL,
                TransactionsPerMinute REAL NOT NULL,
                CounterValue INTEGER NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_Samples_Target_Time
            ON Samples(ServerName, DatabaseName, TimestampUtc);
            """;
        await command.ExecuteNonQueryAsync();
        await PurgeOldAsync();
    }

    public async Task AddAsync(string server, string database, TransactionSample sample)
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Samples(ServerName, DatabaseName, TimestampUtc, TransactionsPerMinute, CounterValue)
            VALUES($server, $database, $time, $tpm, $counter);
            """;
        command.Parameters.AddWithValue("$server", server);
        command.Parameters.AddWithValue("$database", database);
        command.Parameters.AddWithValue("$time", sample.TimestampUtc.ToString("O"));
        command.Parameters.AddWithValue("$tpm", sample.TransactionsPerMinute);
        command.Parameters.AddWithValue("$counter", sample.CounterValue);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<List<TransactionSample>> LoadAsync(string server, string database)
    {
        var result = new List<TransactionSample>();
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT TimestampUtc, TransactionsPerMinute, CounterValue
            FROM Samples
            WHERE ServerName = $server AND DatabaseName = $database
              AND TimestampUtc >= $cutoff
            ORDER BY TimestampUtc;
            """;
        command.Parameters.AddWithValue("$server", server);
        command.Parameters.AddWithValue("$database", database);
        command.Parameters.AddWithValue("$cutoff", DateTime.UtcNow.AddDays(-7).ToString("O"));
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new TransactionSample
            {
                TimestampUtc = DateTime.Parse(reader.GetString(0), null, System.Globalization.DateTimeStyles.RoundtripKind),
                TransactionsPerMinute = reader.GetDouble(1),
                CounterValue = reader.GetInt64(2)
            });
        }
        return result;
    }

    public async Task PurgeOldAsync()
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Samples WHERE TimestampUtc < $cutoff;";
        command.Parameters.AddWithValue("$cutoff", DateTime.UtcNow.AddDays(-7).ToString("O"));
        await command.ExecuteNonQueryAsync();
    }
}
