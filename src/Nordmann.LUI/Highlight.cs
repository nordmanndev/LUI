using System.Runtime.InteropServices;

namespace Nordmann.LUI;

/// <summary>
/// Where a match lies in the original text of a hit. Reserved: it is filled
/// once the highlighting is built (LUI-Spezifikation §9.3); until then
/// <see cref="LuiHit{TKey, TPayload}.Highlight"/> is <see langword="null"/>.
/// </summary>
/// <param name="Start">The position of the first character of the match.</param>
/// <param name="Length">The number of characters of the match.</param>
[StructLayout(LayoutKind.Auto)]
public readonly record struct Highlight(int Start, int Length);
