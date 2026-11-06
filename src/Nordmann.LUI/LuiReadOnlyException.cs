namespace Nordmann.LUI;

/// <summary>
/// A write was refused because the file may not be written (ADR-0033): the catalog was opened with
/// <c>Mode=ReadOnly</c>. <b>The file decides what may happen</b>, not a switch in the options and not the index:
/// a read-only catalog reads and searches, and it writes nothing — no write-ahead logging, no table, no
/// definition, no write lock.
/// <para>
/// The same application code runs in the service that writes and in the service that only reads: the second one
/// opens the same file with a read-only connection string.
/// </para>
/// </summary>
public sealed class LuiReadOnlyException : LuiException
{
    /// <summary>Creates the exception for a refused write.</summary>
    /// <param name="operation">What was refused, in words: "creating an index".</param>
    public LuiReadOnlyException(string operation)
        : base(
            $"The catalog is read-only: the database was opened with Mode=ReadOnly, so {operation} is refused. "
            + "Open the catalog with a connection string that may write (Data Source=app.db) to write; a read-only "
            + "catalog reads and searches.")
    {
        Operation = operation;
    }

    /// <summary>Creates the exception with the way out of it.</summary>
    /// <param name="operation">What was refused, in words.</param>
    /// <param name="advice">What to do about it.</param>
    internal LuiReadOnlyException(string operation, string advice)
        : base(
            $"The catalog is read-only: the database was opened with Mode=ReadOnly, so {operation} is refused. {advice}")
    {
        Operation = operation;
    }

    /// <summary>What was refused — the write that the file does not allow.</summary>
    public string Operation { get; }
}
