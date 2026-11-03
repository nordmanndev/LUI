using System.Text;
using Microsoft.Data.Sqlite;

namespace Nordmann.LUI.Integration.Tests;

/// <summary>
/// Reads back what FTS5's trigram tokenizer makes of a text.
/// <para>
/// The tokenizer is the ground truth: it is what the index holds and what a
/// query is matched against. The vocabulary table lists the trigrams it
/// produced; stitching them back together gives the folded text.
/// </para>
/// </summary>
internal sealed class TokenizerProbe : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly SqliteCommand _insert;
    private int _nextRowId = 1;

    public TokenizerProbe()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        Execute("CREATE VIRTUAL TABLE probe USING fts5(body, tokenize = 'trigram remove_diacritics 1');");
        Execute("CREATE VIRTUAL TABLE probe_vocab USING fts5vocab(probe, 'instance');");

        _insert = _connection.CreateCommand();
        _insert.CommandText = "INSERT INTO probe(rowid, body) VALUES ($rowid, $body);";
        _insert.Parameters.Add("$rowid", SqliteType.Integer);
        _insert.Parameters.Add("$body", SqliteType.Text);
    }

    /// <summary>Adds a text and returns the row it landed in.</summary>
    public int Add(string text)
    {
        var rowId = _nextRowId++;
        _insert.Parameters["$rowid"].Value = rowId;
        _insert.Parameters["$body"].Value = text;
        _insert.ExecuteNonQuery();

        return rowId;
    }

    /// <summary>
    /// The folded text of every row that was added — empty for a text too
    /// short for a single trigram, because the tokenizer then produces
    /// nothing at all.
    /// </summary>
    public Dictionary<int, string> FoldAll()
    {
        var trigrams = new Dictionary<int, List<string>>();

        using (var command = _connection.CreateCommand())
        {
            command.CommandText = "SELECT doc, term FROM probe_vocab ORDER BY doc, offset;";

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var doc = reader.GetInt32(0);
                if (!trigrams.TryGetValue(doc, out var list))
                {
                    list = [];
                    trigrams[doc] = list;
                }

                list.Add(reader.GetString(1));
            }
        }

        var folded = new Dictionary<int, string>();

        foreach (var (doc, terms) in trigrams)
        {
            var builder = new StringBuilder(terms[0]);
            for (var i = 1; i < terms.Count; i++)
            {
                builder.Append(terms[i][^1]);
            }

            folded[doc] = builder.ToString();
        }

        return folded;
    }

    /// <summary>Runs a scalar expression against a value and returns the result.</summary>
    public string Scalar(string expression, string value)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = $"SELECT {expression};";
        command.Parameters.AddWithValue("$value", value);

        return command.ExecuteScalar()?.ToString() ?? string.Empty;
    }

    public void Dispose()
    {
        _insert.Dispose();
        _connection.Dispose();
    }

    private void Execute(string sql)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
