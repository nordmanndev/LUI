namespace Nordmann.LUI;

/// <summary>
/// What the keys of an index are (ADR-0035, ADR-0035): numbers or texts. The file stores both as text; the
/// kind is part of the definition, and a key of the other kind is refused on the write.
/// </summary>
public enum LuiKeyKind
{
    /// <summary>A whole number (<see cref="long"/>) — the default, and the only kind of a <c>Linked</c> index.</summary>
    Number = 0,

    /// <summary>A text: a slug, a ULID, a code of the application.</summary>
    Text = 1,
}
