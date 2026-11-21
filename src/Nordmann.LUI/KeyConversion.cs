namespace Nordmann.LUI;

/// <summary>
/// The edge between the typed handles and the index below them (ADR-0073): the handle speaks <c>TKey</c>, the index speaks
/// <see cref="LuiKey"/> — a number for <c>long</c> and <c>int</c>, a text for <c>string</c> and <see cref="Guid"/> (in the canonical
/// form <c>"D"</c>, lower case). The key type of a definition is checked before a handle exists (<c>KeyTypes.Validate</c>), so
/// the checks of the type are folded away by the compiler for each closed type.
/// </summary>
internal static class KeyConversion
{
    internal static LuiKey ToKey<TKey>(TKey key)
        where TKey : notnull
    {
        if (typeof(TKey) == typeof(long))
        {
            return LuiKey.FromInt64((long)(object)key);
        }

        if (typeof(TKey) == typeof(int))
        {
            return LuiKey.FromInt64((int)(object)key);
        }

        if (typeof(TKey) == typeof(string))
        {
            return LuiKey.FromString((string)(object)key);
        }

        return typeof(TKey) == typeof(Guid)
            ? LuiKey.FromString(((Guid)(object)key).ToString("D", System.Globalization.CultureInfo.InvariantCulture))
            : throw new NotSupportedException($"The key type {typeof(TKey).Name} is not supported.");
    }

    internal static TKey FromKey<TKey>(LuiKey key)
        where TKey : notnull
    {
        if (typeof(TKey) == typeof(long))
        {
            return (TKey)(object)key.ToInt64();
        }

        if (typeof(TKey) == typeof(int))
        {
            // the stored key was checked against the int range when it was read (KeyCodec.FromText), and a linked index leaves
            // the rows outside it out: a key that gets here fits
            return (TKey)(object)checked((int)key.ToInt64());
        }

        if (typeof(TKey) == typeof(string))
        {
            return (TKey)(object)key.ToText();
        }

        return typeof(TKey) == typeof(Guid)
            ? (TKey)(object)Guid.ParseExact(key.ToText(), "D")
            : throw new NotSupportedException($"The key type {typeof(TKey).Name} is not supported.");
    }
}
