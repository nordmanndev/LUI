namespace Nordmann.LUI;

/// <summary>
/// Gives a payload type the name that an embedded index stores for it (ADR-0072): <c>[LuiPayloadName("recipe")]</c>. Without the
/// attribute LUI computes a stable name from the namespace and the class (<c>MyApp.Recipes.RecipePayload</c>), and renaming the class
/// or moving it to another namespace is then a changed definition — for an embedded index that means drop and fill again. With
/// the attribute the class can be renamed and moved: the stored name stays. Put it on every payload type.
/// <para>
/// <b>Two different types with the same name look the same to LUI</b> — the name is the identity of a payload, and LUI does
/// not detect a duplicate across types. A generic payload type keeps the stable names of its type arguments after the name
/// (<c>recipe&lt;MyApp.Tag&gt;</c>), and an array keeps its brackets.
/// </para>
/// </summary>
/// <param name="name">The name; not empty.</param>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface | AttributeTargets.Enum, AllowMultiple = false, Inherited = false)]
public sealed class LuiPayloadNameAttribute(string name) : Attribute
{
    /// <summary>The name that is stored for the payload type.</summary>
    public string Name { get; } = name;
}
