namespace Nordmann.LUI;

/// <summary>
/// The edge between the typed handles and the index below them (ADR-0073): the handle speaks <c>TKey</c>, the index speaks
/// <see cref="LuiKey"/>. <c>long</c> is the key of every definition in this version; the check of the type is
/// folded away by the compiler for each closed type.
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

        return typeof(TKey) == typeof(string)
            ? LuiKey.FromString((string)(object)key)
            : throw new NotSupportedException($"The key type {typeof(TKey).Name} is not supported.");
    }

    internal static TKey FromKey<TKey>(LuiKey key)
        where TKey : notnull
    {
        if (typeof(TKey) == typeof(long))
        {
            return (TKey)(object)key.ToInt64();
        }

        return typeof(TKey) == typeof(string)
            ? (TKey)(object)key.ToText()
            : throw new NotSupportedException($"The key type {typeof(TKey).Name} is not supported.");
    }
}
