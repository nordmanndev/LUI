using System.Globalization;

namespace Nordmann.LUI.Indexing;

/// <summary>
/// The key of the application as the <c>key</c> column holds it. A key is a
/// <see cref="long"/> or a <see cref="string"/> (LUI-Spezifikation §12.3);
/// both are stored as text.
/// </summary>
internal static class KeyCodec<TKey>
{
    internal static string KeyType => Describe();

    internal static string ToText(TKey key)
    {
        if (typeof(TKey) == typeof(long))
        {
            return ((long)(object)key!).ToString(CultureInfo.InvariantCulture);
        }

        var text = (string?)(object?)key;
        ArgumentNullException.ThrowIfNull(text, nameof(key));
        return text;
    }

    internal static TKey FromText(string text) =>
        typeof(TKey) == typeof(long)
            ? (TKey)(object)long.Parse(text, CultureInfo.InvariantCulture)
            : (TKey)(object)text;

    private static string Describe()
    {
        if (typeof(TKey) == typeof(long))
        {
            return "long";
        }

        if (typeof(TKey) == typeof(string))
        {
            return "string";
        }

        throw new NotSupportedException(
            $"A key is a long or a string; '{typeof(TKey)}' is not supported (LUI-Spezifikation §12.3).");
    }
}
