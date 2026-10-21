# SQL Transaction Monitor

WPF/MVVM .NET 8 application that samples SQL Server's per-database `Transactions/sec` cumulative counter once per minute, converts the delta into transactions per minute, displays a graph and table, keeps seven days in SQLite, and exports the visible history to CSV.

## Build
1. Open `SqlTransactionMonitor.csproj` in Visual Studio 2022 or later with the .NET desktop workload.
2. Restore NuGet packages and build.
3. Run the application, enter SQL Server and database, choose Windows or SQL authentication, and click **Connect**.
4. Click **Start Monitoring**.

## SQL permission
The login must be able to connect to the selected database and read `sys.dm_os_performance_counters`. Depending on SQL Server version/configuration, grant the least-privileged server permission required for that DMV, commonly `VIEW SERVER STATE` or `VIEW SERVER PERFORMANCE STATE`.

## Measurement behavior
- SQL Server exposes this per-second counter as a cumulative value. The application reads it once per minute and calculates `(new counter - old counter) / elapsed minutes`.
- The first plotted value requires two reads.
- If SQL Server restarts or the counter decreases, that interval is discarded.
- Credentials are used only for the active connection and are not stored in SQLite.
- SQLite path: `%LOCALAPPDATA%\SqlTransactionMonitor\transactions.db`.
