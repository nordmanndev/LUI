namespace Nordmann.LUI.Architecture.Tests;

/// <summary>
/// The name of a type as the documents write it: without the arity that the runtime appends to a generic type
/// (<c>LuiTypedHit`1</c>). The documents name a type, not a closed generic — and the guards that compare the page
/// with the assemblies must ask the same question on both sides.
/// </summary>
internal static class Type_Names
{
    /// <summary>The name of a type, without the arity of a generic type.</summary>
    /// <param name="type">The type.</param>
    /// <returns>The name, as the documents write it.</returns>
    public static string Of(Type type)
    {
        var name = type.Name;
        var arity = name.IndexOf('`', StringComparison.Ordinal);
        return arity < 0 ? name : name[..arity];
    }
}
