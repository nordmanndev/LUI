namespace Nordmann.LUI;

/// <summary>
/// There is no index of that name in the database (ADR-0033). The read path opens an index, it does not create one:
/// the index has to be there, and the message names it.
/// </summary>
public sealed class LuiIndexNotFoundException : LuiException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="indexName">The name of the index that is not there.</param>
    public LuiIndexNotFoundException(string indexName)
        : base(
            $"There is no index named '{indexName}' in this database. An index that is opened has to exist: create it "
            + "with LuiCatalog.Create(name, …) — that writes to the file — or open another name.")
    {
        IndexName = indexName;
    }

    /// <summary>The name of the index that is not there.</summary>
    public string IndexName { get; }
}
