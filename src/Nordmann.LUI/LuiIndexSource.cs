namespace Nordmann.LUI;

/// <summary>
/// Where the content of an index lives (ADR-0014, ADR-0015, ADR-0073): in a table of the library or in a table of
/// the application. A <see cref="LuiIndexDefinitionBase"/> says which; the definition type decides it.
/// </summary>
public enum LuiIndexSource
{
    /// <summary>The content lives in a table of the library, <c>lui_&lt;name&gt;</c>: the application writes through the index.</summary>
    Embedded,

    /// <summary>The content lives in a table of the application: the index reads it and keeps only its search form.</summary>
    Linked,
}
