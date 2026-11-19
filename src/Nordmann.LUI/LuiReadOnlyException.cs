namespace Nordmann.LUI;

/// <summary>
/// SQLite refused a write because the file may not be written (ADR-0068). <b>SQLite decides, LUI translates</b>: the
/// exception is derived from SQLite's refusal — it is the <see cref="Exception.InnerException"/> — and never thrown
/// ahead of it. The message names the reason: the catalogue was opened with <see cref="LuiCatalog.OpenReadOnly(string, LuiCatalogOptions?)"/>
/// (with the operation and the index, and the way to write), or the file or its directory may not be written (rights, a
/// read-only medium, a <c>-wal</c> or <c>-shm</c> that cannot be created).
/// <para>
/// The same application code runs in the service that writes and in the service that only reads: the second one
/// opens the same file with <c>OpenReadOnly</c>.
/// </para>
/// </summary>
public sealed class LuiReadOnlyException : LuiException
{
    /// <summary>Creates the exception for a refused write.</summary>
    /// <param name="operation">What was refused, in words: "creating an index".</param>
    public LuiReadOnlyException(string operation)
        : base($"The catalog may not be written, so {operation} is refused. A catalog is written when it was opened with LuiCatalog.Open or LuiCatalog.OpenOrCreate, on a file and a directory that may be written.")
    {
        Operation = operation;
    }

    /// <summary>Creates the exception with the reason, and the refusal of SQLite.</summary>
    /// <param name="operation">What was refused, in words.</param>
    /// <param name="message">The reason, and what to do about it.</param>
    /// <param name="inner">The refusal of SQLite, if there is one.</param>
    internal LuiReadOnlyException(string operation, string message, Exception? inner)
        : base(message, inner!)
    {
        Operation = operation;
    }

    /// <summary>What was refused — the write that the file does not allow.</summary>
    public string Operation { get; }
}
