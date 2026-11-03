namespace Nordmann.LUI.Hosting;

/// <summary>
/// One index registered with the container (LUI-Spezifikation §12.7): its name, and what the hosted services do with it —
/// ensure it, warm its typo dictionary, apply its outbox. The delegates close over the key and payload types, so the
/// services do not need to know them.
/// </summary>
/// <param name="Name">The name of the index — the key of its services.</param>
/// <param name="Ensure">Resolves the index from the container: the first resolution ensures it.</param>
/// <param name="WarmUp">Builds the typo dictionary of the index.</param>
/// <param name="ApplyPending">Applies the outbox of a linked index; <see langword="null"/> for an owned one.</param>
/// <param name="ApplyPendingEvery">How often <paramref name="ApplyPending"/> runs; <see langword="null"/> for never.</param>
internal sealed record IndexRegistration(
    string Name,
    Action<IServiceProvider> Ensure,
    Action<IServiceProvider, CancellationToken> WarmUp,
    Func<IServiceProvider, CancellationToken, int>? ApplyPending,
    TimeSpan? ApplyPendingEvery);
