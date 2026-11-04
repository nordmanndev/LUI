namespace Nordmann.LUI.Hosting;

/// <summary>
/// One index registered with the container (LUI-Spezifikation §12.7): its name, and what the hosted services do with it —
/// ensure it, warm its typo dictionary. The delegates close over the key and payload types,
/// so the services do not need to know them. A linked index has an outbox besides: <see cref="LinkedIndexRegistration"/>
/// (review of 2026-11-04, C.2, issue #40 — a type, not two fields that are <see langword="null"/> for the other kind).
/// </summary>
/// <param name="Name">The name of the index — the key of its services.</param>
/// <param name="Ensure">Resolves the index from the container: the first resolution ensures it.</param>
/// <param name="WarmUp">Builds the typo dictionary of an index with typo tolerance; says whether it did.</param>
internal record IndexRegistration(
    string Name,
    Action<IServiceProvider> Ensure,
    Func<IServiceProvider, CancellationToken, bool> WarmUp);
