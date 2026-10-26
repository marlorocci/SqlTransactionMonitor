using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using Microsoft.Win32;
using SqlTransactionMonitor.Models;
using SqlTransactionMonitor.Services;
using System.Diagnostics;

namespace SqlTransactionMonitor.ViewModels;

public partial class MainViewModel : ObservableObject
{
    //public string DatabasePath => _history.DatabasePath;

    public string LogPath => AppLogger.CurrentLogPath;
    private readonly SqlMonitoringService _sql = new();
    private readonly HistoryRepository _history = new();
    private CancellationTokenSource? _monitorCts;
    private Task? _monitorTask;
    private long? _previousCounter;
    private DateTime? _previousTimeUtc;
    private readonly ObservableCollection<double> _chartValues = new();
    private readonly ObservableCollection<string> _labels = new();

    public ObservableCollection<TransactionSample> Samples { get; } = new();
    public ISeries[] Series { get; }

    public Axis[] XAxes { get; }
    public Axis[] YAxes { get; }

    [ObservableProperty] private string serverName = "localhost";
    [ObservableProperty] private string databaseName = "master";
    [ObservableProperty] private string userName = string.Empty;
    [ObservableProperty] private string password = string.Empty;
    [ObservableProperty] private bool useWindowsAuthentication = true;
    [ObservableProperty] private bool isConnected;
    [ObservableProperty] private bool isMonitoring;
    [ObservableProperty] private string statusText = "Not connected";
    [ObservableProperty] private double currentTransactionsPerMinute;
    public string DatabasePath => _history.DatabasePath;
    public string MonitorButtonText => IsMonitoring ? "Stop Monitoring" : "Start Monitoring";

    [RelayCommand]
    private async Task ClearDatabaseDataAsync()
    {
        try
        {
            var result = MessageBox.Show(
                "This will permanently delete all transaction history from the SQLite database.\n\n" +
                "The SQLite database itself will remain in place.\n\n" +
                "Do you want to continue?",
                "Clear Database Data",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);

            if (result != MessageBoxResult.Yes)
                return;

            await _history.ClearAllDataAsync();

            Samples.Clear();
            _chartValues.Clear();
            _labels.Clear();

            CurrentTransactionsPerMinute = 0;

            StatusText = "All transaction history has been cleared.";
        }
        catch (Exception ex)
        {
            StatusText = $"Unable to clear database data: {ex.Message}";

            MessageBox.Show(
                ex.Message,
                "Clear Database Data Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    public MainViewModel()
    {
        Series =
        [
            new LineSeries<double>
        {
            Values = _chartValues,
            Name = "Transactions/Min",
            GeometrySize = 4,
            Fill = null
        }
        ];

        XAxes =
        [
            new Axis
        {
            Labels = _labels
        }
        ];

        YAxes =
        [
            new Axis
        {
            Name = "Transactions / Minute"
        }
        ];
    }

    private async Task InitializeAsync()
    {
        try
        {
            AppLogger.Information(
                nameof(MainViewModel),
                "MainViewModel initialization started.");

            await _history.InitializeAsync();

            OnPropertyChanged(nameof(DatabasePath));
            OnPropertyChanged(nameof(LogPath));

            StatusText = "Ready.";

            AppLogger.Information(
                nameof(MainViewModel),
                "MainViewModel initialization completed.");
        }
        catch (Exception ex)
        {
            StatusText =
                $"SQLite initialization failed: {ex.Message}";

            AppLogger.Error(
                nameof(MainViewModel),
                "MainViewModel initialization failed.",
                ex);
        }
    }
    [RelayCommand]
    private async Task ConnectAsync()
    {
        AppLogger.Information(
            nameof(MainViewModel),
            $"Connect selected. Server={ServerName}, " +
            $"Database={DatabaseName}, " +
            $"WindowsAuthentication={UseWindowsAuthentication}, " +
            $"UserName={(UseWindowsAuthentication ? "<Windows>" : UserName)}");

        try
        {
            StatusText = "Initializing local database...";

            await _history.InitializeAsync();
            await StopMonitoringInternalAsync();

            StatusText = "Connecting...";

            await _sql.ConnectAsync(
                ServerName.Trim(),
                DatabaseName.Trim(),
                UserName,
                Password,
                UseWindowsAuthentication,
                CancellationToken.None);

            IsConnected = true;
            Password = string.Empty;

            await LoadHistoryAsync();

            StatusText =
                $"Connected to {ServerName} / {DatabaseName}";

            AppLogger.Information(
                nameof(MainViewModel),
                $"Connection workflow completed successfully. " +
                $"Server={ServerName}, Database={DatabaseName}");
        }
        catch (Exception ex)
        {
            IsConnected = false;

            StatusText =
                $"Connection failed: {ex.Message}";

            AppLogger.Error(
                nameof(MainViewModel),
                $"Connection workflow failed. " +
                $"Server={ServerName}, Database={DatabaseName}",
                ex);
        }
    }

    [RelayCommand]
    private async Task ToggleMonitoringAsync()
    {
        if (IsMonitoring) await StopMonitoringInternalAsync();
        else StartMonitoring();
    }

    private void StartMonitoring()
    {
        if (!IsConnected) { StatusText = "Connect to SQL Server first."; return; }
        _previousCounter = null;
        _previousTimeUtc = null;
        _monitorCts = new CancellationTokenSource();
        IsMonitoring = true;
        OnPropertyChanged(nameof(MonitorButtonText));
        StatusText = "Monitoring. The first data point appears after two counter reads.";
        _monitorTask = MonitorLoopAsync(_monitorCts.Token);
    }

    private async Task MonitorLoopAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var now = DateTime.UtcNow;
                var counter = await _sql.GetCounterValueAsync(DatabaseName.Trim(), token);
                if (_previousCounter.HasValue && _previousTimeUtc.HasValue)
                {
                    var elapsedMinutes = (now - _previousTimeUtc.Value).TotalMinutes;
                    if (counter >= _previousCounter.Value && elapsedMinutes > 0)
                    {
                        var tpm = (counter - _previousCounter.Value) / elapsedMinutes;
                        var sample = new TransactionSample { TimestampUtc = now, TransactionsPerMinute = tpm, CounterValue = counter };
                        await _history.AddAsync(ServerName.Trim(), DatabaseName.Trim(), sample);
                        Application.Current.Dispatcher.Invoke(() => AddSample(sample));
                        CurrentTransactionsPerMinute = tpm;
                        StatusText = $"Monitoring {DatabaseName}. Last sample: {tpm:N1} transactions/min";
                    }
                    else StatusText = "SQL counter reset detected. The interval was discarded.";
                }
                _previousCounter = counter;
                _previousTimeUtc = now;
                await Task.Delay(TimeSpan.FromMinutes(1), token);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            StatusText = $"Monitoring stopped: {ex.Message}";
            IsConnected = _sql.IsConnected;
        }
        finally
        {
            IsMonitoring = false;
            OnPropertyChanged(nameof(MonitorButtonText));
        }
    }

    private async Task StopMonitoringInternalAsync()
    {
        if (_monitorCts is not null)
        {
            _monitorCts.Cancel();
            if (_monitorTask is not null)
            {
                try { await _monitorTask; } catch (OperationCanceledException) { }
            }
            _monitorCts.Dispose();
            _monitorCts = null;
            _monitorTask = null;
        }
        IsMonitoring = false;
        OnPropertyChanged(nameof(MonitorButtonText));
        await Task.CompletedTask;
    }

    private async Task LoadHistoryAsync()
    {
        var data = await _history.LoadAsync(ServerName.Trim(), DatabaseName.Trim());
        Samples.Clear();
        _chartValues.Clear();
        foreach (var item in data) AddSample(item);
        if (Samples.Count > 0) CurrentTransactionsPerMinute = Samples[^1].TransactionsPerMinute;
    }

    private void AddSample(TransactionSample sample)
    {
        Samples.Add(sample);                // DataGrid

        _chartValues.Add(sample.TransactionsPerMinute); // Graph

        _labels.Add(
            sample.TimestampLocal.ToString("MM-dd HH:mm"));

        while (Samples.Count > 0 &&
               Samples[0].TimestampUtc < DateTime.UtcNow.AddDays(-7))
        {
            Samples.RemoveAt(0);

            if (_chartValues.Count > 0)
                _chartValues.RemoveAt(0);

            if (_labels.Count > 0)
                _labels.RemoveAt(0);
        }
    }

    [RelayCommand]
    private void OpenLogFolder()
    {
        try
        {
            Directory.CreateDirectory(AppLogger.LogDirectory);

            Process.Start(new ProcessStartInfo
            {
                FileName = AppLogger.LogDirectory,
                UseShellExecute = true
            });

            AppLogger.Information(
                nameof(MainViewModel),
                "Log folder opened by the user.");
        }
        catch (Exception ex)
        {
            StatusText = $"Unable to open log folder: {ex.Message}";

            AppLogger.Error(
                nameof(MainViewModel),
                "Unable to open log folder.",
                ex);
        }
    }

    [RelayCommand]
    private async Task ExportCsvAsync()
    {
        if (Samples.Count == 0) { StatusText = "There is no data to export."; return; }
        var dialog = new SaveFileDialog
        {
            Filter = "CSV files (*.csv)|*.csv",
            FileName = $"SqlTransactions_{Sanitize(ServerName)}_{Sanitize(DatabaseName)}_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
        };
        if (dialog.ShowDialog() != true) return;
        await using var writer = new StreamWriter(dialog.FileName, false, new System.Text.UTF8Encoding(true));
        await writer.WriteLineAsync("TimestampLocal,TimestampUtc,Server,Database,TransactionsPerMinute,CounterValue");
        foreach (var sample in Samples)
        {
            await writer.WriteLineAsync(string.Join(',',
                Csv(sample.TimestampLocal.ToString("O")), Csv(sample.TimestampUtc.ToString("O")),
                Csv(ServerName), Csv(DatabaseName), sample.TransactionsPerMinute.ToString("F3", CultureInfo.InvariantCulture), sample.CounterValue));
        }
        StatusText = $"Exported {Samples.Count} samples to {dialog.FileName}";
    }

    private static string Csv(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
    private static string Sanitize(string value) => string.Concat(value.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
}
