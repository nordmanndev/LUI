namespace Nordmann.LUI.Schema;

/// <summary>
/// The key types an index definition may have (ADR-0073) — one place, so that the key types of 0.4.0 (<c>int</c>,
/// <c>string</c>, <see cref="Guid"/>) open it and nothing else. Today a definition has <c>long</c> keys; the old path
/// keeps its texts for an embedded index.
/// </summary>
internal static class KeyTypes
{
    /// <summary>The stored name of a key type, or an error when a definition may not have it.</summary>
    /// <param name="keyType">The type argument of the definition.</param>
    /// <param name="source">The source of the definition.</param>
    /// <param name="oldPath"><see langword="true"/> for the definition the old calls build: texts are allowed for an embedded index.</param>
    /// <returns>The name the definition stores.</returns>
    /// <exception cref="LuiConfigurationException">The key type is not allowed.</exception>
    internal static string Validate(Type keyType, LuiIndexSource source, bool oldPath)
    {
        if (keyType == typeof(long))
        {
            return "long";
        }

        if (oldPath && source == LuiIndexSource.Embedded && keyType == typeof(string))
        {
            return "string";
        }

        throw new LuiConfigurationException(
            $"The key type {Display(keyType)} of an index definition is not supported: the keys are long. "
            + "Declare the definition as LuiIndexDefinition<long> or LuiLinkedIndexDefinition<long>.");
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
