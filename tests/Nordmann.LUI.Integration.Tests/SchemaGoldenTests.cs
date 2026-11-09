using Shouldly;

namespace Nordmann.LUI.Integration.Tests;

/// <summary>
/// The schema of an <c>Embedded</c> index is a promise to every database that already holds one: the SQL that
/// creates it must not change by accident (ADR-0014, ADR-0015). The golden file is the <c>sqlite_master</c> of a
/// single-word and a multi-word index as <c>main</c> created them before the second content source; a change
/// that is meant is a new golden file and a new schema version, in the same pull request.
/// </summary>
public sealed class SchemaGoldenTests
{
    [Fact]
    public void The_schema_of_an_embedded_index_is_the_golden_one()
    {
        using var database = new TestDatabase();
        using (var catalog = database.Open())
        {
            catalog.Create("one", index => index.SingleWord());
            catalog.Create("many", index => index.MultiWord().WithKey(LuiKeyKind.Text).GermanTransliteration(false));
        }

        var golden = File.ReadAllText(TestFiles.GoldenSchemaPath);

        Dump(database).ShouldBe(golden.ReplaceLineEndings("\n"));
    }

    private static string Dump(TestDatabase database)
    {
        using var connection = database.Connect();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT type, name, sql FROM sqlite_master WHERE name LIKE 'lui_%' ORDER BY name;";
        using var reader = command.ExecuteReader();
        var entries = new List<string>();
        while (reader.Read())
        {
            entries.Add($"{reader.GetString(0)} {reader.GetString(1)}\n{(reader.IsDBNull(2) ? string.Empty : reader.GetString(2))}");
        }

        return string.Join("\n---\n", entries).ReplaceLineEndings("\n");
    }
}
