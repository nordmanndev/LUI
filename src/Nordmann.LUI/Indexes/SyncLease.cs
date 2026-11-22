namespace Nordmann.LUI.Indexes;

/// <summary>
/// The statements of the lease of a linked sync (ADR-0077): three columns of the row of the index in <c>lui_meta</c> — the generation the
/// last sync took, the owner of the lease and the time it expires, in milliseconds since 1970 (UTC). Every statement is one statement, so
/// that the check and the change are one step inside the write transaction that runs them.
/// </summary>
internal static class SyncLease
{
    /// <summary>
    /// Takes the lease and the next generation, when the index has no lease, or its lease has expired: <c>changes()</c> is 1 for a lease that
    /// was taken and 0 for one that is held — or for an index that is not there.
    /// </summary>
    internal const string TakeSql =
        "UPDATE lui_meta SET sync_generation = sync_generation + 1, sync_lease_owner = $owner, sync_lease_expires = $expires "
        + "WHERE index_name = $name AND (sync_lease_owner IS NULL OR sync_lease_expires IS NULL OR sync_lease_expires <= $now);";

    /// <summary>The generation of the sync that took the lease.</summary>
    internal const string GenerationSql = "SELECT sync_generation FROM lui_meta WHERE index_name = $name;";

    /// <summary>Extends the lease of its owner: <c>changes()</c> is 0 when <c>$owner</c> does not hold it — that is how a sync finds out that it lost it.</summary>
    internal const string RenewSql =
        "UPDATE lui_meta SET sync_lease_expires = $expires WHERE index_name = $name AND sync_lease_owner = $owner;";

    /// <summary>Gives the lease back — only the lease of <c>$owner</c>: the lease of a sync that took it over is not touched.</summary>
    internal const string ReleaseSql =
        "UPDATE lui_meta SET sync_lease_owner = NULL, sync_lease_expires = NULL WHERE index_name = $name AND sync_lease_owner = $owner;";

    /// <summary>Who holds the lease and until when: <c>(owner, expires)</c>; no row when the index is not there.</summary>
    internal const string ReadSql = "SELECT sync_lease_owner, sync_lease_expires FROM lui_meta WHERE index_name = $name;";
}
