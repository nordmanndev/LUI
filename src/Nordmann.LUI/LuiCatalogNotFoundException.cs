namespace Nordmann.LUI;

/// <summary>
/// There is no file at the path of the catalog (ADR-0066): <see cref="LuiCatalog.Open(string, LuiCatalogOptions?)"/> and
/// <see cref="LuiCatalog.OpenReadOnly(string, LuiCatalogOptions?)"/> open a file that is there, they never create one.
/// <see cref="LuiCatalog.OpenOrCreate(string, LuiCatalogOptions?)"/> creates it.
/// </summary>
public sealed class LuiCatalogNotFoundException : LuiException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="path">The full path of the file that is not there.</param>
    public LuiCatalogNotFoundException(string path)
        : base(
            $"There is no file at '{path}'. LuiCatalog.Open and LuiCatalog.OpenReadOnly open a catalog that is there: "
            + "create the file with LuiCatalog.OpenOrCreate, or correct the path.")
    {
        Path = path;
    }

    /// <summary>The full path of the file that is not there.</summary>
    public string Path { get; }
}
