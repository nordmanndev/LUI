namespace Nordmann.LUI;

#pragma warning disable CA1720 // the values are the C# types they name: Long, Int, String and Guid are the key types of ADR-0073
/// <summary>
/// The type of the keys of an index, exactly as the stored definition names it (ADR-0073): the type argument of the
/// definition — <c>LuiIndexDefinition&lt;int&gt;</c> is an <see cref="Int"/> index. It replaces <see cref="LuiKeyKind"/>
/// (numbers or texts), which stays until the old calls are removed; the report on an index (#273) carries it.
/// </summary>
public enum LuiKeyType
{
    /// <summary>A <see cref="long"/> — an embedded or a linked index. Stored as <c>long</c>.</summary>
    Long = 0,

    /// <summary>An <see cref="int"/> — an embedded or a linked index. Stored as <c>int</c>.</summary>
    Int = 1,

    /// <summary>A <see cref="string"/> — an embedded index (a linked one follows with #267). Stored as <c>string</c>.</summary>
    String = 2,

    /// <summary>A <see cref="System.Guid"/> in the canonical form <c>"D"</c>, lower case — an embedded index. Stored as <c>guid</c>.</summary>
    Guid = 3,
}
#pragma warning restore CA1720
