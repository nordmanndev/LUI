namespace Nordmann.LUI;

/// <summary>
/// The file was written in another LUI format than this version reads (ADR-0073). The <b>LUI format version</b> — the
/// <c>schema_version</c> of <c>lui_meta</c>, not SQLite's own schema counter — is 2 in 0.4.0; a file of 0.3.0 has format 1,
/// and a file of a newer version a newer one. It is thrown when the catalog is <b>opened</b>
/// (<see cref="LuiCatalog.Open(string, LuiCatalogOptions?)"/>, <see cref="LuiCatalog.OpenReadOnly(string, LuiCatalogOptions?)"/> and
/// <see cref="LuiCatalog.OpenOrCreate(string, LuiCatalogOptions?)"/> of a file that has indexes), not when the first index is
/// used. LUI does not migrate a file of another format: recreate the indexes.
/// </summary>
public sealed class LuiFormatException : LuiException
{
    internal LuiFormatException(string path, int foundFormat, int supportedFormat)
        : base(Describe(path, foundFormat, supportedFormat))
    {
        Path = path;
        FoundFormat = foundFormat;
        SupportedFormat = supportedFormat;
    }

    /// <summary>The full path of the file.</summary>
    public string Path { get; }

    /// <summary>The LUI format version the file was written with.</summary>
    public int FoundFormat { get; }

    /// <summary>The LUI format version this version reads.</summary>
    public int SupportedFormat { get; }

    private static string Describe(string path, int foundFormat, int supportedFormat)
    {
        var file = System.IO.Path.GetFileName(path);
        var found = foundFormat.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var supported = supportedFormat.ToString(System.Globalization.CultureInfo.InvariantCulture);

        return foundFormat == 1
            ? $"The file '{file}' was written by LUI 0.3.0 (format 1); this version reads format {supported}. Recreate the indexes."
            : foundFormat > supportedFormat
                ? $"The file '{file}' was written by a newer version of LUI (format {found}); this version reads format {supported}. "
                  + "Open it with the version that wrote it, or recreate the indexes."
                : $"The file '{file}' was written by an unknown version of LUI (format {found}); this version reads format {supported}. Recreate the indexes.";
    }
}
