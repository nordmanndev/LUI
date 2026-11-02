using Nordmann.LUI.Querying;
using Shouldly;

namespace Nordmann.LUI.Tests;

/// <summary>
/// The plan of the candidate query is a promise (LUI-Spezifikation §8.2, §19):
/// the search index hands out its rows in the order of the rowid — no sorter.
/// </summary>
public sealed class QueryPlanTests
{
    [Fact]
    public void The_candidate_query_reads_the_search_index_in_rowid_order_without_a_sorter()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        catalog.Index<long, Chip>("tags").SingleWord().Owned().Ensure();

        var plan = Plan(database, QueryRunner<long, Chip>.CandidateSql("lui_tags_fts", "lui_tags"), "$expr", "s_norm : \"gan\"");

        plan.ShouldContain(line => line.Contains("VIRTUAL TABLE INDEX 64", StringComparison.Ordinal), customMessage: Describe(plan));
        plan.ShouldContain(line => line.Contains("USING INTEGER PRIMARY KEY", StringComparison.Ordinal), customMessage: Describe(plan));
        plan.ShouldNotContain(line => line.Contains("TEMP B-TREE", StringComparison.Ordinal), Describe(plan));
    }

    [Theory]
    [InlineData("(s_norm : (\"apfel\" AND \"kuch\")) OR (s_compact : \"apfelkuch\")")]
    [InlineData("s_compact : \"apfelkuch\"")]
    [InlineData("s_compact : \"abcd\"")]
    public void The_candidate_query_of_a_multi_word_index_has_the_same_plan(string expression)
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        catalog.Index<long, Chip>("tags").MultiWord().Owned().Ensure();

        var plan = Plan(database, QueryRunner<long, Chip>.CandidateSql("lui_tags_fts", "lui_tags"), "$expr", expression);

        plan.ShouldContain(line => line.Contains("VIRTUAL TABLE INDEX 64", StringComparison.Ordinal), customMessage: Describe(plan));
        plan.ShouldContain(line => line.Contains("USING INTEGER PRIMARY KEY", StringComparison.Ordinal), customMessage: Describe(plan));
        plan.ShouldNotContain(line => line.Contains("TEMP B-TREE", StringComparison.Ordinal), Describe(plan));
    }

    [Theory]
    [InlineData(1, "lui_tags_p1")]
    [InlineData(2, "lui_tags_p2")]
    public void The_prefix_path_reads_its_index_in_rowid_order_without_a_sorter(int length, string expectedIndex)
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        catalog.Index<long, Chip>("tags").SingleWord().Owned().Ensure();

        var plan = Plan(database, QueryRunner<long, Chip>.PrefixSql("lui_tags", length), "$q", "a");

        plan.ShouldContain(line => line.Contains($"USING INDEX {expectedIndex}", StringComparison.Ordinal), customMessage: Describe(plan));
        plan.ShouldNotContain(line => line.Contains("TEMP B-TREE", StringComparison.Ordinal), Describe(plan));
    }

    [Fact]
    public void The_hits_are_read_by_primary_key()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        catalog.Index<long, Chip>("tags").SingleWord().Owned().Ensure();

        var plan = Plan(database, "SELECT doc_id, key, payload FROM lui_tags WHERE doc_id IN (1, 2, 3);", "$none", "0");

        plan.ShouldNotContain(line => line.StartsWith("SCAN", StringComparison.Ordinal), Describe(plan));
    }

    private static string[] Plan(TestDatabase database, string sql, string parameter, string value)
    {
        using var connection = database.Connect();
        using var command = connection.CreateCommand();
        command.CommandText = "EXPLAIN QUERY PLAN " + sql;
        command.Parameters.AddWithValue(parameter, value);
        command.Parameters.AddWithValue("$n", 501);

        var lines = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            lines.Add(reader.GetString(3));
        }

        return [.. lines];
    }

    private static string Describe(string[] plan) => "plan: " + string.Join(" | ", plan);
}
