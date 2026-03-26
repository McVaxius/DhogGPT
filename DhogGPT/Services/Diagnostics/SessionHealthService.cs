using System.Threading;

namespace DhogGPT.Services.Diagnostics;

public sealed class SessionHealthService
{
    private readonly object syncRoot = new();

    private int queueDepth;
    private long successCount;
    private long failureCount;
    private string lastError = string.Empty;
    private string lastEndpoint = string.Empty;
    private string lastProvider = string.Empty;
    private DateTimeOffset? lastSuccessUtc;
    private TimeSpan lastLatency = TimeSpan.Zero;

    public void UpdateQueueDepth(int depth)
    {
        Interlocked.Exchange(ref queueDepth, Math.Max(0, depth));
    }

    public void RecordSuccess(string providerName, string endpoint, TimeSpan latency)
    {
        Interlocked.Increment(ref successCount);

        lock (syncRoot)
        {
            lastProvider = providerName;
            lastEndpoint = endpoint;
            lastLatency = latency;
            lastSuccessUtc = DateTimeOffset.UtcNow;
            lastError = string.Empty;
        }
    }

    public void RecordFailure(string providerName, string endpoint, string error, TimeSpan latency)
    {
        Interlocked.Increment(ref failureCount);

        lock (syncRoot)
        {
            lastProvider = providerName;
            lastEndpoint = endpoint;
            lastLatency = latency;
            lastError = error;
        }
    }

    public SessionHealthSnapshot GetSnapshot()
    {
        lock (syncRoot)
        {
            return new SessionHealthSnapshot(
                Volatile.Read(ref queueDepth),
                Interlocked.Read(ref successCount),
                Interlocked.Read(ref failureCount),
                lastProvider,
                lastEndpoint,
                lastError,
                lastSuccessUtc,
                lastLatency);
        }
    }

    public readonly record struct SessionHealthSnapshot(
        int QueueDepth,
        long SuccessCount,
        long FailureCount,
        string LastProvider,
        string LastEndpoint,
        string LastError,
        DateTimeOffset? LastSuccessUtc,
        TimeSpan LastLatency);
}
