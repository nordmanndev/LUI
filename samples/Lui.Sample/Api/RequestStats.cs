namespace Nordmann.LUI.Sample.Api;

/// <summary>
/// What the suggest route has seen: queries answered, and queries the page abandoned before the answer left. The page shows
/// it next to its own count of aborts — the server stopped the work, not only the browser the wait. The count of the
/// queries the route began is for the tests: an abort can only be seen once the server has the request (issue #94).
/// </summary>
internal sealed class RequestStats
{
    private long _started;
    private long _served;
    private long _aborted;

    internal long Started => Interlocked.Read(ref _started);

    internal long Served => Interlocked.Read(ref _served);

    internal long Aborted => Interlocked.Read(ref _aborted);

    internal void CountStarted() => Interlocked.Increment(ref _started);

    internal void CountServed() => Interlocked.Increment(ref _served);

    internal void CountAborted() => Interlocked.Increment(ref _aborted);
}
