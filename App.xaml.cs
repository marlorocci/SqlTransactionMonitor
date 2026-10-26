using System.Windows;
//namespace SqlTransactionMonitor;

using global::SqlTransactionMonitor.Services;
//using SqlTransactionMonitor.Services;
using System.Windows;
using System.Windows.Threading;

namespace SqlTransactionMonitor;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        AppLogger.DeleteLogsOlderThan(14);

        AppLogger.Information(
            nameof(App),
            $"Application starting. Version={GetApplicationVersion()}, " +
            $"OS={Environment.OSVersion}, " +
            $"64BitProcess={Environment.Is64BitProcess}, " +
            $"Machine={Environment.MachineName}");

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AppLogger.Information(
            nameof(App),
            $"Application exiting. ExitCode={e.ApplicationExitCode}");

        DispatcherUnhandledException -= OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;

        base.OnExit(e);
    }

    private static void OnDispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs e)
    {
        AppLogger.Critical(
            nameof(App),
            "Unhandled exception on the WPF dispatcher thread.",
            e.Exception);

        MessageBox.Show(
            $"An unexpected error occurred.\n\n" +
            $"{e.Exception.Message}\n\n" +
            $"Log file:\n{AppLogger.CurrentLogPath}",
            "SQL Transaction Monitor",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        // Leave false for truly unexpected UI failures.
        // Setting true could leave application state inconsistent.
        e.Handled = false;
    }

    private static void OnUnhandledException(
        object sender,
        UnhandledExceptionEventArgs e)
    {
        var exception = e.ExceptionObject as Exception;

        AppLogger.Critical(
            nameof(App),
            $"Unhandled application-domain exception. " +
            $"IsTerminating={e.IsTerminating}",
            exception);
    }

    private static void OnUnobservedTaskException(
        object? sender,
        UnobservedTaskExceptionEventArgs e)
    {
        AppLogger.Error(
            nameof(App),
            "An unobserved task exception occurred.",
            e.Exception);

        e.SetObserved();
    }

    private static string GetApplicationVersion()
    {
        return typeof(App).Assembly
                   .GetName()
                   .Version?
                   .ToString()
               ?? "Unknown";
    }
}