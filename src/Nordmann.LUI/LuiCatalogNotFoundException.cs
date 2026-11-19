namespace Nordmann.LUI;

/// <summary>
/// There is no catalogue at the path (ADR-0068): the file is missing, or it is no catalogue — it is no SQLite database, or it has no
/// <c>lui_meta</c>. <see cref="LuiCatalog.Open(string, LuiCatalogOptions?)"/> and
/// <see cref="LuiCatalog.OpenReadOnly(string, LuiCatalogOptions?)"/> open a catalogue that is there: they never create a file and never
/// turn a file into a catalogue. <see cref="LuiCatalog.OpenOrCreate(string, LuiCatalogOptions?)"/> does. The message says which of
/// the cases it is.
/// </summary>
public sealed class LuiCatalogNotFoundException : LuiException
{
    /// <summary>Creates the exception for a file that is not there.</summary>
    /// <param name="path">The full path of the file that is not there.</param>
    public LuiCatalogNotFoundException(string path)
        : this(
            path,
            $"There is no file at '{path}'. LuiCatalog.Open and LuiCatalog.OpenReadOnly open a catalog that is there: "
            + "create the file with LuiCatalog.OpenOrCreate, or correct the path.",
            inner: null)
    {
    }

    /// <summary>Creates the exception with the reason.</summary>
    /// <param name="path">The full path of the file.</param>
    /// <param name="message">What is wrong with the file, and what to do about it.</param>
    /// <param name="inner">The error of SQLite, if there is one.</param>
    internal LuiCatalogNotFoundException(string path, string message, Exception? inner)
        : base(message, inner!)
    {
        Path = path;
    }

    /// <summary>The full path of the file that is not a catalogue.</summary>
    public string Path { get; }
}
