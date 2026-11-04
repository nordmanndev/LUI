namespace Nordmann.LUI.Sample.Api;

/// <summary>
/// What the suggest route has seen: queries answered, and queries the page abandoned before the answer left. The page shows
/// it next to its own count of aborts — the server stopped the work, not only the browser the wait.
/// </summary>
internal sealed class RequestStats
{
    private long _served;
    private long _aborted;

    internal long Served => Interlocked.Read(ref _served);

    internal long Aborted => Interlocked.Read(ref _aborted);

    internal void CountServed() => Interlocked.Increment(ref _served);

    internal void CountAborted() => Interlocked.Increment(ref _aborted);
}
