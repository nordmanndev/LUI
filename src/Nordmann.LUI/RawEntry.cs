namespace Nordmann.LUI;

/// <summary>
/// An entry as the indexes of the library take it (ADR-0073): the key as the file stores it — the invariant text of a number, the text of a
/// string, a GUID in the form <c>D</c> —, and the payload as the object the serializer of the index writes. The typed entries
/// (<see cref="LuiEntry{TKey}"/>, <see cref="LuiEntry{TKey, TPayload}"/>) check their values and make this; nothing below the handles knows the
/// type of a key or of a payload.
/// </summary>
/// <param name="Key">The key as the file stores it.</param>
/// <param name="Text">The text that is searched; checked by the typed entry.</param>
/// <param name="Weight">The weight; checked by the typed entry.</param>
/// <param name="Priority">The priority; checked by the typed entry.</param>
/// <param name="Payload">The payload, or <see langword="null"/>.</param>
internal sealed record RawEntry(string Key, string Text, int Weight, LuiPriority Priority, object? Payload);
