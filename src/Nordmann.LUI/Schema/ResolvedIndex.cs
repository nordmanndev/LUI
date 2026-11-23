namespace Nordmann.LUI.Schema;

/// <summary>A definition after its first use (ADR-0073): the validated name, the description and the stored name of the key type.</summary>
/// <param name="Name">The validated name of the index.</param>
/// <param name="Description">What the builder chain described.</param>
/// <param name="KeyType">The stored name of the key type: <c>long</c>.</param>
internal sealed record ResolvedIndex(string Name, IndexDescription Description, string KeyType);
