namespace Nordmann.LUI.Storage;

/// <summary>
/// A count of the commands that LUI creates on its own connections — a seam for the tests of the thin handle (ADR-0075):
/// "after the first call <c>catalog.Index(definition)</c> creates no <c>SqliteCommand</c>" is a number that is counted, not
/// an impression. A <see cref="Database"/> that has a counter opens its connections as <see cref="CountingConnection"/>;
/// one without opens plain ones and pays nothing.
/// </summary>
internal sealed class CommandCounter
{
    private long _count;

    /// <summary>How many commands were created since the counter was set.</summary>
    internal long Count => Interlocked.Read(ref _count);

    internal void Increment() => Interlocked.Increment(ref _count);
}
