using System.Runtime.InteropServices;

namespace Nordmann.LUI;

/// <summary>
/// The payload of an index that stores none. An index starts without a payload
/// (<c>catalog.Create("tags", index => index.SingleWord())</c>); <c>WithPayload(…)</c>
/// gives it one.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct LuiNoPayload;
