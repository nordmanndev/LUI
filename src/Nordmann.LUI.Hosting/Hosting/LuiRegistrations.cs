namespace Nordmann.LUI.Hosting;

/// <summary>What <c>AddLui</c> registered: the indexes, and what the hosted services do on start.</summary>
internal sealed class LuiRegistrations
{
    private readonly List<IndexRegistration> _indexes = [];
    private readonly List<OpenIndexRegistration> _opened = [];

    /// <summary>The indexes that are created, in the order they were added.</summary>
    internal IReadOnlyList<IndexRegistration> Indexes => _indexes;

    /// <summary>The indexes that are opened (ADR-0033), in the order they were added.</summary>
    internal IReadOnlyList<OpenIndexRegistration> OpenedIndexes => _opened;

    /// <summary>Whether every index is created when the host starts. On by default (§12.7).</summary>
    internal bool EnsureOnStart { get; set; } = true;

    /// <summary>Whether the typo dictionaries are built in the background after the start. Off by default.</summary>
    internal bool WarmUpTypoDictionariesOnStart { get; set; }

    /// <summary>Adds an index; a name can be registered once.</summary>
    /// <exception cref="ArgumentException">An index of that name is registered already.</exception>
    internal void Add(IndexRegistration registration)
    {
        if (IsRegistered(registration.Name))
        {
            throw new ArgumentException($"An index named '{registration.Name}' is registered already.", nameof(registration));
        }

        _indexes.Add(registration);
    }

    /// <summary>Adds an index that is opened instead of created; a name can be registered once.</summary>
    /// <exception cref="ArgumentException">An index of that name is registered already.</exception>
    internal void Add(OpenIndexRegistration registration)
    {
        if (IsRegistered(registration.Name))
        {
            throw new ArgumentException($"An index named '{registration.Name}' is registered already.", nameof(registration));
        }

        _opened.Add(registration);
    }

    /// <summary>Whether a name is taken — by an index that is created or by one that is opened.</summary>
    private bool IsRegistered(string name) =>
        _indexes.Exists(index => string.Equals(index.Name, name, StringComparison.Ordinal))
        || _opened.Exists(index => string.Equals(index.Name, name, StringComparison.Ordinal));
}
