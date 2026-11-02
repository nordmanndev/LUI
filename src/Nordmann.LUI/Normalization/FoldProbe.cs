using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;

namespace Nordmann.LUI.Normalization;

/// <summary>
/// Asks the SQLite that is loaded how it folds text, and compares the answer
/// with the measured table (docs/normalization.md §7).
/// <para>
/// The table was measured from the SQLite that <c>Microsoft.Data.Sqlite</c>
/// bundles. An application can load another SQLite — and an index stored
/// with the old folding would then be invisible to its queries. The probe
/// turns that silent failure into an error at <c>LuiCatalog.Open</c>.
/// </para>
/// </summary>
internal static class FoldProbe
{
    /// <summary>How many code points of the rest of the plane are checked to stay unchanged.</summary>
    private const int UnchangedSamples = 96;

    /// <summary>
    /// The ranges in which a tokenizer folds — checked in full, so that a
    /// mapping the table forgot (a newer SQLite folds more) is found, not
    /// only a mapping the table got wrong.
    /// </summary>
    private static readonly (int From, int To)[] Ranges =
    [
        (0x0020, 0x024F), // ASCII, Latin-1, Latin Extended-A and B
        (0x0300, 0x036F), // combining marks
        (0x0370, 0x058F), // Greek, Cyrillic, Armenian
        (0x1E00, 0x1FFF), // Latin Extended Additional, Greek Extended
        (0x2100, 0x218F), // letterlike symbols, number forms
        (0xFF00, 0xFFEF), // full-width forms
    ];

    private static readonly Lock Gate = new();
    private static string? _failure;
    private static bool _verified;

    /// <summary>
    /// Verifies the SQLite of this process once and remembers the verdict —
    /// the native library is the same for every connection.
    /// </summary>
    internal static void EnsureVerified()
    {
        lock (Gate)
        {
            if (!_verified)
            {
                var mismatches = Verify(TokenizerFold.Listed);
                _failure = mismatches.Count == 0 ? null : Describe(mismatches);
                _verified = true;
            }

            if (_failure is not null)
            {
                throw new LuiEnvironmentException(_failure);
            }
        }
    }

    /// <summary>
    /// Compares the folding of the loaded SQLite with a table. Every listed
    /// code point is checked, plus a spread of code points the table does not
    /// list (they must come out unchanged).
    /// </summary>
    /// <returns>The code points on which the two disagree; empty when they agree.</returns>
    internal static IReadOnlyList<Mismatch> Verify(IReadOnlyList<(int From, char To)> table)
    {
        var expected = new Dictionary<int, char>();
        foreach (var (from, to) in table)
        {
            expected[from] = to;
        }

        var probes = new SortedSet<int>(expected.Keys);

        foreach (var (from, to) in Ranges)
        {
            for (var codePoint = from; codePoint <= to; codePoint++)
            {
                probes.Add(codePoint);
            }
        }

        var step = 0x10000 / UnchangedSamples;
        for (var codePoint = 0x20; codePoint < 0xD800; codePoint += step)
        {
            probes.Add(codePoint);
        }

        var observed = Fold(probes);
        var mismatches = new List<Mismatch>();

        foreach (var codePoint in probes)
        {
            var wanted = expected.TryGetValue(codePoint, out var to) ? ToText(to) : ((char)codePoint).ToString();
            if (!observed.TryGetValue(codePoint, out var actual) || !string.Equals(actual, wanted, StringComparison.Ordinal))
            {
                mismatches.Add(new Mismatch(codePoint, wanted, observed.GetValueOrDefault(codePoint)));
            }
        }

        return mismatches;
    }

    private static string ToText(char folded) => folded == TokenizerFold.Removed ? string.Empty : folded.ToString();

    /// <summary>
    /// What the tokenizer makes of each code point. The code point sits in
    /// the middle of two plain letters, so that the text is long enough for a
    /// trigram even when the tokenizer drops the character.
    /// </summary>
    private static Dictionary<int, string> Fold(SortedSet<int> codePoints)
    {
        using var connection = new SqliteConnection("Data Source=:memory:");

        try
        {
            connection.Open();

            Execute(connection, "CREATE VIRTUAL TABLE probe USING fts5(body, tokenize = 'trigram remove_diacritics 1');");
            Execute(connection, "CREATE VIRTUAL TABLE probe_vocab USING fts5vocab(probe, 'instance');");

            Insert(connection, codePoints);

            var result = new Dictionary<int, string>(codePoints.Count);
            foreach (var (doc, text) in ReadTrigrams(connection))
            {
                // the framing letters are not part of the answer
                var folded = text.ToString();
                result[doc - 1] = folded.Length >= 4
                    && folded.StartsWith("xx", StringComparison.Ordinal)
                    && folded.EndsWith("xx", StringComparison.Ordinal)
                    ? folded[2..^2]
                    : "?" + folded;
            }

            return result;
        }
        catch (SqliteException exception)
        {
            throw new LuiEnvironmentException(
                "LUI needs SQLite with the FTS5 extension and its 'trigram' tokenizer "
                + $"(SQLite 3.45 or newer), and the probe failed: {exception.Message}",
                exception);
        }
    }

    private static void Insert(SqliteConnection connection, IEnumerable<int> codePoints)
    {
        using var transaction = connection.BeginTransaction();
        using var insert = connection.CreateCommand();

        insert.Transaction = transaction;
        insert.CommandText = "INSERT INTO probe(rowid, body) VALUES ($rowid, $body);";
        var rowId = insert.Parameters.Add("$rowid", SqliteType.Integer);
        var body = insert.Parameters.Add("$body", SqliteType.Text);

        foreach (var codePoint in codePoints)
        {
            rowId.Value = codePoint + 1;
            body.Value = $"xx{(char)codePoint}xx";
            insert.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    /// <summary>The trigrams of each document stitched back into the folded text.</summary>
    private static Dictionary<int, StringBuilder> ReadTrigrams(SqliteConnection connection)
    {
        var trigrams = new Dictionary<int, StringBuilder>();

        using var select = connection.CreateCommand();
        select.CommandText = "SELECT doc, term FROM probe_vocab ORDER BY doc, offset;";
        using var reader = select.ExecuteReader();

        while (reader.Read())
        {
            var doc = reader.GetInt32(0);
            var term = reader.GetString(1);

            if (!trigrams.TryGetValue(doc, out var text))
            {
                trigrams[doc] = new StringBuilder(term);
            }
            else
            {
                text.Append(term[^1]);
            }
        }

        return trigrams;
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static string Describe(IReadOnlyList<Mismatch> mismatches)
    {
        var shown = string.Join(
            "; ",
            mismatches.Take(5).Select(m => string.Create(
                CultureInfo.InvariantCulture,
                $"U+{m.CodePoint:X4} folds to '{m.Actual ?? "<nothing>"}', the table says '{m.Expected}'")));

        return $"The SQLite that is loaded folds text differently from the table LUI was measured with "
            + $"({mismatches.Count} code points differ: {shown}). An index stored with one folding is invisible "
            + "to queries that use the other. Use the SQLite that Microsoft.Data.Sqlite bundles, or regenerate "
            + "the table (docs/normalization.md) and build the indexes again.";
    }

    /// <summary>A code point on which the loaded SQLite and the table disagree.</summary>
    internal readonly record struct Mismatch(int CodePoint, string Expected, string? Actual);
}
