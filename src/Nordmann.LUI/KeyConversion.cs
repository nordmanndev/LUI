using System.Globalization;
using Nordmann.LUI.Schema;

namespace Nordmann.LUI;

/// <summary>
/// The key between the application and the file (ADR-0073): the file stores every key as text — the invariant text of a number, the text
/// of a string, a GUID in the form <c>D</c> in lower case — and the handles of the definitions convert at the border. Everything below the
/// handles speaks the text.
/// </summary>
internal static class KeyConversion
{
    /// <summary>The key type a type argument stands for.</summary>
    /// <typeparam name="TKey">The type argument of a definition.</typeparam>
    /// <returns>The key type.</returns>
    internal static LuiKeyType TypeOf<TKey>()
        where TKey : notnull
    {
        if (typeof(TKey) == typeof(long))
        {
            return LuiKeyType.Long;
        }

        if (typeof(TKey) == typeof(int))
        {
            return LuiKeyType.Int;
        }

        if (typeof(TKey) == typeof(string))
        {
            return LuiKeyType.String;
        }

        return typeof(TKey) == typeof(Guid)
            ? LuiKeyType.Guid
            : throw new NotSupportedException($"The key type {typeof(TKey).Name} is not supported.");
    }

    /// <summary>The text of a key, as the file stores it.</summary>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <param name="key">The key.</param>
    /// <returns>The text.</returns>
    internal static string ToText<TKey>(TKey key)
        where TKey : notnull
    {
        if (typeof(TKey) == typeof(long))
        {
            return ((long)(object)key).ToString(CultureInfo.InvariantCulture);
        }

        if (typeof(TKey) == typeof(int))
        {
            return ((int)(object)key).ToString(CultureInfo.InvariantCulture);
        }

        if (typeof(TKey) == typeof(string))
        {
            return (string)(object)key;
        }

        return typeof(TKey) == typeof(Guid)
            ? ((Guid)(object)key).ToString("D", CultureInfo.InvariantCulture)
            : throw new NotSupportedException($"The key type {typeof(TKey).Name} is not supported.");
    }

    /// <summary>The number a key of a linked index is: its <c>long</c> or <c>int</c> (the keys of a linked index are the rowids of its table).</summary>
    /// <typeparam name="TKey">The key type, <c>long</c> or <c>int</c>.</typeparam>
    /// <param name="key">The key.</param>
    /// <returns>The number.</returns>
    internal static long ToInt64<TKey>(TKey key)
        where TKey : notnull =>
        typeof(TKey) == typeof(long) ? (long)(object)key
        : typeof(TKey) == typeof(int) ? (int)(object)key
        : throw new NotSupportedException($"The key type {typeof(TKey).Name} is not a number.");

    /// <summary>The key of a stored text; a text that is not a key of the type is a <see cref="LuiKeyException"/>.</summary>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <param name="text">The stored key.</param>
    /// <param name="indexName">The index, for the message.</param>
    /// <returns>The key.</returns>
    internal static TKey FromText<TKey>(string text, string indexName)
        where TKey : notnull
    {
        if (typeof(TKey) == typeof(long))
        {
            return long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number)
                ? (TKey)(object)number
                : throw new LuiKeyException(indexName, text, LuiKeyType.Long, "it is not a whole number in the range of a long.");
        }

        if (typeof(TKey) == typeof(int))
        {
            return int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var small)
                ? (TKey)(object)small
                : throw new LuiKeyException(indexName, text, LuiKeyType.Int, "it is not a whole number in the range of an int.");
        }

        if (typeof(TKey) == typeof(string))
        {
            return (TKey)(object)text;
        }

        return typeof(TKey) == typeof(Guid)
            ? Guid.TryParseExact(text, "D", out var guid)
                ? (TKey)(object)guid
                : throw new LuiKeyException(indexName, text, LuiKeyType.Guid, "it is not a GUID in the form 'D'.")
            : throw new NotSupportedException($"The key type {typeof(TKey).Name} is not supported.");
    }
}
