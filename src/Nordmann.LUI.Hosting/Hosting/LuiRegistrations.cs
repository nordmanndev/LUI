namespace Nordmann.LUI.Hosting;

/// <summary>What <c>AddLui</c> registered: the indexes, and what the hosted services do on start.</summary>
internal sealed class LuiRegistrations
{
    private readonly List<IndexRegistration> _indexes = [];

    /// <summary>The indexes, in the order they were added.</summary>
    internal IReadOnlyList<IndexRegistration> Indexes => _indexes;

    /// <summary>Whether every index is ensured when the host starts. On by default (§12.7).</summary>
    internal bool EnsureOnStart { get; set; } = true;

    /// <summary>Whether the typo dictionaries are built in the background after the start. Off by default.</summary>
    internal bool WarmUpTypoDictionariesOnStart { get; set; }

    /// <summary>Adds an index; a name can be registered once.</summary>
    /// <exception cref="ArgumentException">An index of that name is registered already.</exception>
    internal void Add(IndexRegistration registration)
    {
        if (_indexes.Exists(index => string.Equals(index.Name, registration.Name, StringComparison.Ordinal)))
        {
            throw new ArgumentException($"An index named '{registration.Name}' is registered already.", nameof(registration));
        }

        _indexes.Add(registration);
    }
}
