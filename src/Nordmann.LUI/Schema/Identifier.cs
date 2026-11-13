namespace Nordmann.LUI.Schema;

/// <summary>
/// The names that reach SQL. They are checked against
/// <c>^[a-z][a-z0-9_]{0,40}$</c> and never put into a statement unchecked
/// (ADR-0006).
/// </summary>
internal static class Identifier
{
    internal const string Pattern = "^[a-z][a-z0-9_]{0,40}$";

    internal static string Validate(string? name, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(name, parameterName);

        if (!IsValid(name))
        {
            throw new ArgumentException(
                $"'{name}' is not a valid name. A name matches {Pattern}: "
                + "a lower-case letter, then up to 40 lower-case letters, digits or underscores.",
                parameterName);
        }

        return name;
    }

    /// <summary>
    /// The name of an index: a valid name that cannot collide with the objects
    /// the library creates for it or with the catalog table (ADR-0006).
    /// </summary>
    internal static string ValidateIndexName(string? name, string parameterName)
    {
        Validate(name, parameterName);

        if (string.Equals(name, "meta", StringComparison.Ordinal)
            || name!.Contains("_fts", StringComparison.Ordinal)
            || name.EndsWith("_rowid", StringComparison.Ordinal)
            || name.EndsWith("_p1", StringComparison.Ordinal)
            || name.EndsWith("_p2", StringComparison.Ordinal)
            || name.EndsWith("_pending", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"'{name}' is reserved: 'meta' is the catalog, and a name that contains '_fts' or ends with "
                + "'_rowid', '_p1', '_p2' or '_pending' could collide with the tables and indexes LUI creates for another index.",
                parameterName);
        }

        return name;
    }

    /// <summary>The pattern of a name of the application — a table or a column a <c>Linked</c> index reads.</summary>
    internal const string ApplicationPattern = "^[A-Za-z_][A-Za-z0-9_]{0,63}$";

    /// <summary>
    /// Checks a name of the application's schema: letters, digits and underscores, as the application chose them —
    /// <c>Tags</c> and <c>usage_count</c> alike. The name is always quoted where it reaches SQL (<see cref="Quote"/>).
    /// </summary>
    internal static string ValidateApplicationName(string? name, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(name, parameterName);

        var valid = name.Length is > 0 and <= 64
            && (char.IsAsciiLetter(name[0]) || name[0] == '_')
            && name.AsSpan(1).IndexOfAnyExcept(ApplicationNameCharacters) < 0;

        return valid
            ? name
            : throw new ArgumentException(
                $"'{name}' is not a name LUI reads. A name of the application matches {ApplicationPattern}.",
                parameterName);
    }

    /// <summary>A checked name as an SQL identifier, in double quotes.</summary>
    internal static string Quote(string name) => $"\"{name}\"";

    private static readonly System.Buffers.SearchValues<char> ApplicationNameCharacters =
        System.Buffers.SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789_");

    internal static bool IsValid(string name)
    {
        if (name.Length is 0 or > 41 || name[0] is < 'a' or > 'z')
        {
            return false;
        }

        foreach (var c in name.AsSpan(1))
        {
            if (c is not ((>= 'a' and <= 'z') or (>= '0' and <= '9') or '_'))
            {
                return false;
            }
        }

        return true;
    }
}
