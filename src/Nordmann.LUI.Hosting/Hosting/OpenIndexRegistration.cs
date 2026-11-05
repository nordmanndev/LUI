namespace Nordmann.LUI.Hosting;

/// <summary>
/// An index the container opens instead of creating it (ADR-0033): the index has to be there, and the file is never
/// written — no table, no definition, no write lock. It is the way into a database that is written elsewhere: a
/// replica, a file that is read-only, a service that may not write.
/// <para>
/// It is a type of its own, not a flag of <see cref="IndexRegistration"/>: an opened index has no typo dictionary
/// to warm up and no outbox to apply — it only reads.
/// </para>
/// </summary>
/// <param name="Name">The name of the index — the key of its services.</param>
/// <param name="Open">Resolves the index from the container: the first resolution opens it.</param>
internal sealed record OpenIndexRegistration(
    string Name,
    Action<IServiceProvider> Open);
