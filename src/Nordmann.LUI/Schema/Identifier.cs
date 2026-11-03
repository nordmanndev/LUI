namespace Nordmann.LUI.Schema;

/// <summary>
/// The names that reach SQL. They are checked against
/// <c>^[a-z][a-z0-9_]{0,40}$</c> and never put into a statement unchecked
/// (LUI-Spezifikation §7).
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
            || name.EndsWith("_p2", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"'{name}' is reserved: 'meta' is the catalog, and a name that contains '_fts' or ends with "
                + "'_rowid', '_p1' or '_p2' could collide with the tables and indexes LUI creates for another index.",
                parameterName);
        }

        return name;
    }

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
