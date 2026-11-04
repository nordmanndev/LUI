namespace Nordmann.LUI.Hosting;

/// <summary>A linked index registered with the container: an <see cref="IndexRegistration"/> with an outbox (ADR-0015).</summary>
/// <param name="Name">The name of the index — the key of its services.</param>
/// <param name="Ensure">Resolves the index from the container: the first resolution ensures it.</param>
/// <param name="WarmUp">Builds the typo dictionary of an index with typo tolerance; says whether it did.</param>
/// <param name="RebuildIfStale">Rebuilds the typo dictionary of the index if a sync left it stale; says whether it did.</param>
/// <param name="ApplyPending">Applies the outbox of the index: the rows its triggers noted; says how many.</param>
/// <param name="ApplyPendingEvery">How often a hosted service runs <paramref name="ApplyPending"/>; <see langword="null"/> for never — the application calls it.</param>
internal sealed record LinkedIndexRegistration(
    string Name,
    Action<IServiceProvider> Ensure,
    Func<IServiceProvider, CancellationToken, bool> WarmUp,
    Func<IServiceProvider, CancellationToken, bool> RebuildIfStale,
    Func<IServiceProvider, CancellationToken, int> ApplyPending,
    TimeSpan? ApplyPendingEvery)
    : IndexRegistration(Name, Ensure, WarmUp, RebuildIfStale);
