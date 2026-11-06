using System.Globalization;
using System.Runtime.InteropServices;

namespace Nordmann.LUI;

/// <summary>
/// The key of an entry (ADR-0035): a number or a text. It is made from a <see cref="long"/> or a <see cref="string"/>
/// without a cast — <c>index.Remove(42)</c>, <c>index.Remove("01J…")</c> — and read back with an explicit one,
/// <c>(long)hit.Key</c>. A number and a text are different keys even when they read the same: <c>1</c> is not <c>"1"</c>.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly struct LuiKey : IEquatable<LuiKey>
{
    private readonly long _number;
    private readonly string? _text;

    private LuiKey(long number)
    {
        _number = number;
        _text = null;
    }

    private LuiKey(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        _number = 0;
        _text = text;
    }

    /// <summary>Whether the key is a number or a text.</summary>
    public LuiKeyKind Kind => _text is null ? LuiKeyKind.Number : LuiKeyKind.Text;

    /// <summary>A key that is a number.</summary>
    /// <param name="number">The number.</param>
    public static implicit operator LuiKey(long number) => new(number);

    /// <summary>A key that is a text.</summary>
    /// <param name="text">The text; not <see langword="null"/>.</param>
    public static implicit operator LuiKey(string text) => new(text);

    /// <summary>The number of a key that is a number.</summary>
    /// <param name="key">The key.</param>
    /// <exception cref="InvalidCastException">The key is a text.</exception>
    public static explicit operator long(LuiKey key) => key.ToInt64();

    /// <summary>The text of a key that is a text.</summary>
    /// <param name="key">The key.</param>
    /// <exception cref="InvalidCastException">The key is a number.</exception>
    public static explicit operator string(LuiKey key) => key.ToText();

    /// <summary>Whether two keys are the same: the same kind and the same value.</summary>
    /// <param name="left">One key.</param>
    /// <param name="right">The other key.</param>
    /// <returns><see langword="true"/> when they are the same.</returns>
    public static bool operator ==(LuiKey left, LuiKey right) => left.Equals(right);

    /// <summary>Whether two keys differ in kind or value.</summary>
    /// <param name="left">One key.</param>
    /// <param name="right">The other key.</param>
    /// <returns><see langword="true"/> when they differ.</returns>
    public static bool operator !=(LuiKey left, LuiKey right) => !left.Equals(right);

    /// <summary>A key that is a number — the named form of the conversion.</summary>
    /// <param name="number">The number.</param>
    /// <returns>The key.</returns>
    public static LuiKey FromInt64(long number) => new(number);

    /// <summary>A key that is a text — the named form of the conversion.</summary>
    /// <param name="text">The text; not <see langword="null"/>.</param>
    /// <returns>The key.</returns>
    public static LuiKey FromString(string text) => new(text);

    /// <summary>The number of a key that is a number.</summary>
    /// <returns>The number.</returns>
    /// <exception cref="InvalidCastException">The key is a text.</exception>
    public long ToInt64() =>
        _text is null
            ? _number
            : throw new InvalidCastException($"The key \"{_text}\" is a text, not a number.");

    /// <summary>The text of a key that is a text.</summary>
    /// <returns>The text.</returns>
    /// <exception cref="InvalidCastException">The key is a number.</exception>
    public string ToText() =>
        _text ?? throw new InvalidCastException($"The key {_number.ToString(CultureInfo.InvariantCulture)} is a number, not a text.");

    /// <inheritdoc/>
    public bool Equals(LuiKey other) =>
        _text is null
            ? other._text is null && _number == other._number
            : string.Equals(_text, other._text, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is LuiKey other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() =>
        _text is null ? _number.GetHashCode() : StringComparer.Ordinal.GetHashCode(_text);

    /// <summary>The key as the file stores it: the number in invariant digits, or the text.</summary>
    /// <returns>The stored form.</returns>
    public override string ToString() => _text ?? _number.ToString(CultureInfo.InvariantCulture);
}
