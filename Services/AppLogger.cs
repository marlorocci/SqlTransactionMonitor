using System.IO;
using System.Text;

namespace SqlTransactionMonitor.Services;

public enum LogLevel
{
    Debug,
    Information,
    Warning,
    Error,
    Critical
}

public static class AppLogger
{
    private static readonly SemaphoreSlim WriteLock = new(1, 1);

    public static string LogDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData),
        "SqlTransactionMonitor",
        "Logs");

    public static string CurrentLogPath => Path.Combine(
        LogDirectory,
        $"SqlTransactionMonitor-{DateTime.Now:yyyy-MM-dd}.log");

    public static void Debug(string source, string message)
    {
        _ = WriteAsync(LogLevel.Debug, source, message);
    }

    public static void Information(string source, string message)
    {
        _ = WriteAsync(LogLevel.Information, source, message);
    }

    public static void Warning(string source, string message)
    {
        _ = WriteAsync(LogLevel.Warning, source, message);
    }

    public static void Error(
        string source,
        string message,
        Exception? exception = null)
    {
        _ = WriteAsync(LogLevel.Error, source, message, exception);
    }

    public static void Critical(
        string source,
        string message,
        Exception? exception = null)
    {
        _ = WriteAsync(LogLevel.Critical, source, message, exception);
    }

    public static async Task WriteAsync(
        LogLevel level,
        string source,
        string message,
        Exception? exception = null)
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);

            var entry = new StringBuilder();

            entry.Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz"));
            entry.Append(" [");
            entry.Append(level.ToString().ToUpperInvariant());
            entry.Append("] [");
            entry.Append(source);
            entry.Append("] ");
            entry.AppendLine(message);

            if (exception is not null)
            {
                entry.AppendLine($"Exception type: {exception.GetType().FullName}");
                entry.AppendLine($"Exception message: {exception.Message}");
                entry.AppendLine(exception.ToString());

                if (exception.InnerException is not null)
                {
                    entry.AppendLine("Inner exception:");
                    entry.AppendLine(exception.InnerException.ToString());
                }
            }

            await WriteLock.WaitAsync();

            try
            {
                await File.AppendAllTextAsync(
                    CurrentLogPath,
                    entry.ToString(),
                    Encoding.UTF8);
            }
            finally
            {
                WriteLock.Release();
            }
        }
        catch
        {
            // Logging must never crash the application.
        }
    }

    public static void DeleteLogsOlderThan(int retentionDays)
    {
        try
        {
            if (!Directory.Exists(LogDirectory))
                return;

            DateTime cutoff = DateTime.Now.AddDays(-retentionDays);

            foreach (string file in Directory.EnumerateFiles(
                         LogDirectory,
                         "SqlTransactionMonitor-*.log"))
            {
                try
                {
                    if (File.GetLastWriteTime(file) < cutoff)
                        File.Delete(file);
                }
                catch
                {
                    // One inaccessible log should not stop cleanup.
                }
            }
        }
        catch
        {
            // Logging maintenance must not crash the application.
        }
    }
}