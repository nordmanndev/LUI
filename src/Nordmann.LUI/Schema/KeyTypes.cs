namespace Nordmann.LUI.Schema;

/// <summary>
/// The key types an index definition may have (ADR-0073) — one place. An embedded definition has <c>long</c>, <c>int</c>,
/// <c>string</c> or <see cref="Guid"/> keys; a linked one <c>long</c> or <c>int</c>, because its key is the <c>INTEGER PRIMARY
/// KEY</c> of the application's table (text keys of a linked index are #267).
/// </summary>
internal static class KeyTypes
{
    /// <summary>The stored name of a key type, or an error when a definition may not have it.</summary>
    /// <param name="keyType">The type argument of the definition.</param>
    /// <param name="source">The source of the definition.</param>
    /// <returns>The name the definition stores: <c>long</c>, <c>int</c>, <c>string</c> or <c>guid</c>.</returns>
    /// <exception cref="LuiConfigurationException">The key type is not allowed.</exception>
    internal static string Validate(Type keyType, LuiIndexSource source)
    {
        if (keyType == typeof(long))
        {
            return KeyCodec.Name(LuiKeyType.Long);
        }

        if (keyType == typeof(int))
        {
            return KeyCodec.Name(LuiKeyType.Int);
        }

        if (keyType == typeof(string) || keyType == typeof(Guid))
        {
            if (source == LuiIndexSource.Embedded)
            {
                return KeyCodec.Name(keyType == typeof(string) ? LuiKeyType.String : LuiKeyType.Guid);
            }

            throw new LuiConfigurationException(
                $"The key type {Display(keyType)} of a linked index definition is not supported yet: a linked index has the keys of the "
                + "application's table — its INTEGER PRIMARY KEY — and they are long or int. Text keys of a linked index (string, Guid) come with "
                + "issue #267; until then declare the definition as LuiLinkedIndexDefinition<long> or LuiLinkedIndexDefinition<int>, "
                + $"or index the table as an embedded index with a {Display(keyType)} key.");
        }

        throw new LuiConfigurationException(
            $"The key type {Display(keyType)} of an index definition is not supported: the keys are long, int, string or Guid"
            + (source == LuiIndexSource.Linked ? " (a linked index: long or int)." : ".")
            + " Declare the definition as LuiIndexDefinition<long>, LuiIndexDefinition<int>, LuiIndexDefinition<string> or "
            + "LuiIndexDefinition<Guid> (a linked one: LuiLinkedIndexDefinition<long> or LuiLinkedIndexDefinition<int>).");
    }

    /// <summary>The name of a key type as C# writes it: <c>long</c>, <c>int</c>, <c>string</c>, <c>Guid</c>.</summary>
    /// <param name="keyType">The type.</param>
    /// <returns>The name.</returns>
    internal static string Display(Type keyType)
    {
        if (keyType == typeof(long))
        {
            return "long";
        }

        if (keyType == typeof(int))
        {
            return "int";
        }

        return keyType == typeof(string) ? "string" : keyType.Name;
    }
}
