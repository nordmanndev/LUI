using Nordmann.LUI.Sample.Api;

namespace Nordmann.LUI.Sample.Indexes;

/// <summary>
/// What the page can do with an index, whatever its kind and payload. The generic type of the payload ends here: the
/// endpoints do not know it. One implementation per storage — <see cref="EmbeddedSampleIndex"/> and
/// <see cref="LinkedSampleIndex"/> — so the two ways of keeping an index current stay visibly different.
/// </summary>
internal interface ISampleIndex
{
    /// <summary>The name of the index, and of its routes.</summary>
    string Name { get; }

    /// <summary>The index as the index panel shows it.</summary>
    IndexInfo Describe();

    /// <summary>A query; the cancellation token is the request's, so an abort of the page stops the query.</summary>
    SuggestResponse Suggest(string text, LuiQueryOptions options, CancellationToken cancellationToken);

    /// <summary>Checks the index against itself (<c>Check()</c>).</summary>
    LuiIntegrityReport Check();

    /// <summary>Merges the segments of the search index (<c>Optimize()</c>).</summary>
    void Optimize();

    /// <summary>Builds the typo dictionary now (<c>WarmUpTypoDictionary()</c>).</summary>
    void WarmUpTypoDictionary(CancellationToken cancellationToken);

    /// <summary>Builds the typo dictionary anew (<c>RebuildTypoDictionary()</c>).</summary>
    void RebuildTypoDictionary(CancellationToken cancellationToken);

    /// <summary>
    /// Brings the index in step with its source: the seed for an embedded index (<c>Sync(entries)</c>), the application's
    /// table for a linked one (<c>Sync()</c>). Returns the report of the library.
    /// </summary>
    object Sync(CancellationToken cancellationToken);
}
