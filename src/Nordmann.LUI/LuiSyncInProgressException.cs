using System.Globalization;

namespace Nordmann.LUI;

/// <summary>
/// Another sync of this linked index is running, or this one lost its right to run (ADR-0077). One sync of an index runs at a time:
/// it holds a <b>lease</b> that it renews with every batch, and a second <c>Sync()</c> of the same index — in this process or in another
/// one — does not wait for it, it throws this exception, which names the index, the owner of the lease and the time it expires (UTC).
/// A lease whose owner stopped renewing — a process that crashed — expires, and the next sync takes it over.
/// <para>
/// The same exception stops a sync that <b>lost</b> its lease while it ran: it did not renew in time and another sync took the lease over,
/// or the index was dropped and created again. The sync does not write another batch — it would write into an index it no longer owns.
/// The batches it committed stay, <c>NeedsInitialSync</c> is as it was, and <see cref="Owner"/> and <see cref="ExpiresAt"/> are
/// <see langword="null"/> when nobody holds the lease now.
/// </para>
/// </summary>
public sealed class LuiSyncInProgressException : LuiException
{
    internal LuiSyncInProgressException(string indexName, string? owner, DateTimeOffset? expiresAt, bool lost)
        : base(Describe(indexName, owner, expiresAt, lost))
    {
        IndexName = indexName;
        Owner = owner;
        ExpiresAt = expiresAt;
    }

    /// <summary>The name of the index that is being synced.</summary>
    public string IndexName { get; }

    /// <summary>The owner of the lease — the sync that runs — or <see langword="null"/> when nobody holds it.</summary>
    public string? Owner { get; }

    /// <summary>When the lease expires (UTC) if its owner does not renew it, or <see langword="null"/> when nobody holds it.</summary>
    public DateTimeOffset? ExpiresAt { get; }

    private static string Describe(string indexName, string? owner, DateTimeOffset? expiresAt, bool lost)
    {
        var holder = owner is null
            ? "Nobody holds it now"
            : $"The sync '{owner}' holds it until {expiresAt?.ToString("O", CultureInfo.InvariantCulture)} (UTC)";

        return lost
            ? $"The sync of the index '{indexName}' stopped: it no longer holds the lease of the index — it did not renew it in time and another sync took it "
              + "over, or the index was dropped and created again. "
              + $"{holder}. The batches it committed stay and NeedsInitialSync is as it was: run Sync() again."
            : $"The index '{indexName}' is being synced already: only one sync of an index runs at a time, and Sync() does not wait for it. "
              + $"{holder}. Call Sync() again when that sync is done — a lease whose owner stopped (a process that crashed) expires, and the next sync takes it over.";
    }
}
