using Nordmann.LUI.Sample.Api;

namespace Nordmann.LUI.Sample.Indexes;

/// <summary>An index whose entries live in LUI's own table: the page writes through the library (<c>Upsert</c> …).</summary>
internal interface IEmbeddedSampleIndex : ISampleIndex
{
    /// <summary>Adds or replaces an entry; returns its key.</summary>
    long Upsert(UpsertRequest request);

    /// <summary>Changes the weight (<c>UpdateWeight</c>) and/or the priority (<c>UpdatePriority</c>); <see langword="false"/> when the key is not there.</summary>
    bool Patch(long key, PatchRequest request);

    /// <summary>Removes an entry.</summary>
    bool Remove(long key);

    /// <summary>Builds the derived structures anew (<c>Rebuild()</c>).</summary>
    void Rebuild();

    /// <summary>Compares the index with the seed and changes nothing (<c>Check(entries)</c>).</summary>
    LuiIntegrityReport CheckAgainstSeed(CancellationToken cancellationToken);
}
