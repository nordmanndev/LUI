namespace Nordmann.LUI.Hosting;

/// <summary>
/// A registered index with an outbox — the rows its triggers noted (ADR-0015). The outbox service asks for this ability,
/// not for a type of registration: a registration that has an outbox implements it, and the service cannot forget it
/// (review of pull request 45, M2).
/// </summary>
internal interface IOutboxRegistration
{
    /// <summary>The name of the index — the key of its services.</summary>
    string Name { get; }

    /// <summary>Applies the outbox of the index; says how many rows.</summary>
    Func<IServiceProvider, CancellationToken, int> ApplyPending { get; }

    /// <summary>How often a hosted service runs <see cref="ApplyPending"/>; <see langword="null"/> for never — the application calls it.</summary>
    TimeSpan? ApplyPendingEvery { get; }
}
