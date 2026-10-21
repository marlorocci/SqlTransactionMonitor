namespace SqlTransactionMonitor.Models;
public sealed class TransactionSample
{
    public DateTime TimestampUtc { get; init; }
    public double TransactionsPerMinute { get; init; }
    public long CounterValue { get; init; }
    public DateTime TimestampLocal => TimestampUtc.ToLocalTime();
}
