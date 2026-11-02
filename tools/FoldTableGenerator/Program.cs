// FoldTableGenerator — measures what FTS5's trigram tokenizer does to every
// code point of the Basic Multilingual Plane and writes the result as the
// table that LUI's Normalizer uses.
//
// The table is the *measured* behaviour of the tokenizer, not a guess: the
// Normalizer has to fold text exactly the way SQLite folds it, otherwise the
// SQL side and the C# side of the normalisation rule drift apart and the
// library silently stops finding things.
//
//   dotnet run --project tools/FoldTableGenerator
//
// The generated file is checked in. The parity test re-measures the tokenizer
// and compares it with the checked-in table on every build — so a table that
// no longer matches the tokenizer fails the build instead of lying.

using System.Text;
using Microsoft.Data.Sqlite;

namespace Lui.Tools.FoldTableGenerator;

internal static class Program
{
    private const string Target = "src/Nordmann.LUI/Normalization/tokenizer-fold.txt";

    private static int Main()
    {
        var repoRoot = FindRepositoryRoot();
        var target = Path.Combine(repoRoot, Target);

        var result = Measure();

        File.WriteAllText(target, Render(result.Mappings, result.Dropped, result.SqliteVersion));
        Console.WriteLine($"wrote {result.Mappings.Count} mappings and {result.Dropped.Count} removals to {target}");

        return 0;
    }

    private static string Render(SortedDictionary<int, char> mappings, List<int> dropped, string sqliteVersion)
    {
        var lines = new StringBuilder();
        lines.Append("# LUI tokenizer fold table — generated, do not edit by hand.\n");
        lines.Append("#\n");
        lines.Append("# Measured from FTS5's trigram tokenizer with `remove_diacritics 1`\n");
        lines.Append($"# (SQLite {sqliteVersion}). Every entry is one code point and the\n");
        lines.Append("# single character the tokenizer makes of it. A code point that is not\n");
        lines.Append("# listed is passed through unchanged. A '-' means the tokenizer drops\n");
        lines.Append("# the character entirely (the combining marks).\n");
        lines.Append("#\n");
        lines.Append("# Regenerate:  dotnet run --project tools/FoldTableGenerator\n");
        lines.Append("# Verify:      the parity test re-measures the tokenizer and compares.\n");
        lines.Append("#\n");
        lines.Append("# from    to\n");

        foreach (var (from, to) in mappings)
        {
            lines.Append($"{from:X4} {(int)to:X4}\n");
        }

        foreach (var codePoint in dropped)
        {
            lines.Append($"{codePoint:X4} -\n");
        }

        return lines.ToString();
    }

    private static Measurement Measure()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        Exec(connection, """
            CREATE VIRTUAL TABLE probe USING fts5(
                body,
                tokenize = 'trigram remove_diacritics 1');
            """);
        Exec(connection, "CREATE VIRTUAL TABLE probe_vocab USING fts5vocab(probe, 'instance');");

        var codePoints = CodePoints();
        InsertAll(connection, codePoints);
        var rows = ReadRows(connection);

        var mappings = new SortedDictionary<int, char>();
        var dropped = new List<int>();

        for (var i = 0; i < codePoints.Count; i++)
        {
            var mapped = MappedCharacter(rows, i + 1);

            if (mapped is null)
            {
                dropped.Add(codePoints[i]);
                continue;
            }

            var original = char.ConvertFromUtf32(codePoints[i])[0];
            if (mapped.Value != original)
            {
                mappings[codePoints[i]] = mapped.Value;
            }
        }

        return new Measurement(mappings, dropped, SqliteVersion());
    }

    private static List<int> CodePoints()
    {
        var codePoints = new List<int>();

        for (var cp = 1; cp <= 0xFFFF; cp++)
        {
            if (cp is < 0xD800 or > 0xDFFF)
            {
                codePoints.Add(cp);
            }
        }

        return codePoints;
    }

    private static void InsertAll(SqliteConnection connection, List<int> codePoints)
    {
        using var transaction = connection.BeginTransaction();

        using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = "INSERT INTO probe(rowid, body) VALUES ($rowid, $body);";
        var rowid = insert.Parameters.Add("$rowid", SqliteType.Integer);
        var body = insert.Parameters.Add("$body", SqliteType.Text);

        for (var i = 0; i < codePoints.Count; i++)
        {
            rowid.Value = i + 1;
            body.Value = "aaa" + char.ConvertFromUtf32(codePoints[i]) + "aaa";
            insert.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    private static Dictionary<int, List<string>> ReadRows(SqliteConnection connection)
    {
        var rows = new Dictionary<int, List<string>>();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT doc, term FROM probe_vocab ORDER BY doc, offset;";

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var doc = reader.GetInt32(0);
            if (!rows.TryGetValue(doc, out var list))
            {
                list = [];
                rows[doc] = list;
            }

            list.Add(reader.GetString(1));
        }

        return rows;
    }

    /// <summary>
    /// The character the tokenizer made of the code point in the given row, or
    /// null when the row has no trigram or the code point was dropped.
    /// </summary>
    private static char? MappedCharacter(Dictionary<int, List<string>> rows, int row)
    {
        if (!rows.TryGetValue(row, out var trigrams) || trigrams.Count == 0)
        {
            return null;
        }

        var builder = new StringBuilder();
        builder.Append(trigrams[0]);
        foreach (var term in trigrams.Skip(1))
        {
            builder.Append(term[^1]);
        }

        var text = builder.ToString();
        if (text.Length < 6)
        {
            return null;
        }

        var mapped = text[3..^3];
        if (mapped.Length == 0)
        {
            return null; // the tokenizer dropped the code point
        }

        if (mapped.Length != 1)
        {
            throw new InvalidOperationException(
                $"The tokenizer mapped a code point to {mapped.Length} characters — it is not a 1:1 map.");
        }

        return mapped[0];
    }

    private static string SqliteVersion()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT sqlite_version();";
        return command.ExecuteScalar()?.ToString() ?? "unknown";
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "src", "Nordmann.LUI")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root.");
    }

    private static void Exec(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private sealed record Measurement(
        SortedDictionary<int, char> Mappings,
        List<int> Dropped,
        string SqliteVersion);
}
