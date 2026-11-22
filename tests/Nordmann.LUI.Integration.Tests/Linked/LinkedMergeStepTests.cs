using Microsoft.Data.Sqlite;
using Nordmann.LUI.Schema;
using Shouldly;

namespace Nordmann.LUI.Integration.Tests.Linked;

/// <summary>
/// What FTS5's <c>'merge'</c> command says about the work it did (#271, ADR-0077). The linked sync merges its search index in
/// steps — <c>INSERT INTO fts(fts, rank) VALUES('merge', N)</c> in a transaction of its own, repeated until nothing is left —, so the
/// loop depends on one fact of SQLite that no document of LUI owns: <b>how a step reports that it had nothing to do</b>. The
/// SQLite function <c>changes()</c> says 1 after every step (the <c>INSERT</c> itself); <c>total_changes()</c> counts what FTS5 wrote
/// into its shadow tables, so a step that merged something moves it by more than the one change of the <c>INSERT</c>, and a step that
/// found nothing left moves it by exactly one. The tests pin that behaviour down with a table of the shape LUI creates
/// (contentless, with <c>contentless_delete</c>, the trigram tokenizer) before the sync relies on it.
/// </summary>
public sealed class LinkedMergeStepTests
{
    private const string Fts =
        "CREATE VIRTUAL TABLE f USING fts5(t, content = '', contentless_delete = 1, tokenize = 'trigram remove_diacritics 1');";

    [Fact]
    public void A_step_that_merged_something_moves_total_changes_by_more_than_one_and_changes_says_one_either_way()
    {
        using var database = new TestDatabase();
        using var connection = database.Connect();
        Exec(connection, Fts);
        Exec(connection, "INSERT INTO f(f, rank) VALUES ('automerge', 0);");
        Load(connection, firstRowid: 1, batches: 30, rowsPerBatch: 400);

        var steps = new List<(long Delta, long Changes)>();
        while (true)
        {
            steps.Add(Step(connection, pages: 1));
            if (steps[^1].Delta <= 1)
            {
                break;
            }

            steps.Count.ShouldBeLessThan(100_000, "a merge that does not end would hang the sync");
        }

        steps.Count.ShouldBeGreaterThan(2, "one page per step cannot merge thirty segments in one step: the work comes in steps");
        steps.Take(steps.Count - 1).ShouldAllBe(step => step.Delta > 1, "a step that did work wrote more than the INSERT itself");
        steps[^1].Delta.ShouldBe(1, "a step with nothing left changes only the INSERT");
        steps.ShouldAllBe(step => step.Changes == 1, "changes() is the INSERT of the command and says nothing about the work — it is not the signal");
    }

    [Fact]
    public void When_the_steps_say_nothing_is_left_the_index_is_smaller_and_the_next_step_says_it_again()
    {
        // "nothing left" is FTS5's own measure — segments of a level are merged while the level holds the number of segments of
        // 'usermerge' (4 by default) —, not "one segment": optimize merges on from there, and the sync does not ask for it
        using var database = new TestDatabase();
        using var connection = database.Connect();
        Exec(connection, Fts);
        Exec(connection, "INSERT INTO f(f, rank) VALUES ('automerge', 0);");
        Load(connection, firstRowid: 1, batches: 30, rowsPerBatch: 400);
        var blocks = Scalar(connection, "SELECT count(*) FROM f_data;");

        MergeUntilNothingIsLeft(connection, pages: 64);

        Scalar(connection, "SELECT count(*) FROM f_data;").ShouldBeLessThan(blocks, "the merge wrote the segments into fewer pages");
        Step(connection, pages: 64).Delta.ShouldBe(1, "a step on an index with nothing left to merge changes only the INSERT");
        Step(connection, pages: 1).Delta.ShouldBe(1, "whatever the number of pages of the step");
    }

    [Fact]
    public void Deleted_rows_and_new_segments_give_the_next_run_work_again_and_it_ends_too()
    {
        using var database = new TestDatabase();
        using var connection = database.Connect();
        Exec(connection, Fts);
        Exec(connection, "INSERT INTO f(f, rank) VALUES ('automerge', 0);");
        Load(connection, firstRowid: 1, batches: 20, rowsPerBatch: 400);
        MergeUntilNothingIsLeft(connection, pages: 64);

        // the shape of a second linked sync: every row is deleted by its rowid and written again in batches
        Exec(connection, "DELETE FROM f WHERE rowid % 2 = 0;");
        Load(connection, firstRowid: 100_000, batches: 20, rowsPerBatch: 400);

        MergeUntilNothingIsLeft(connection, pages: 64).ShouldBeGreaterThan(1, "the new segments are merged");
        MergeUntilNothingIsLeft(connection, pages: 64).ShouldBe(1, "and a run on a merged index is one step that finds nothing");
    }

    [Fact]
    public void The_statement_of_LUI_binds_the_pages_and_is_the_one_that_reports()
    {
        using var database = new TestDatabase();
        using var connection = database.Connect();
        Exec(connection, Fts);
        Exec(connection, "INSERT INTO f(f, rank) VALUES ('automerge', 0);");
        Load(connection, firstRowid: 1, batches: 10, rowsPerBatch: 400);

        var before = TotalChanges(connection);
        using (var merge = connection.CreateCommand())
        {
            merge.CommandText = LinkedSource.MergeSql("f");
            merge.Parameters.AddWithValue("$pages", 4_096);
            merge.ExecuteNonQuery();
        }

        (TotalChanges(connection) - before).ShouldBeGreaterThan(1, "a first step on ten segments has work");
        LinkedSource.TotalChangesSql.ShouldBe("SELECT total_changes();");
    }

    /// <summary>Merges in steps of <paramref name="pages"/> until a step reports nothing left; the number of steps, the last one included.</summary>
    private static int MergeUntilNothingIsLeft(SqliteConnection connection, int pages)
    {
        var steps = 0;
        while (true)
        {
            steps++;
            if (Step(connection, pages).Delta <= 1)
            {
                return steps;
            }

            steps.ShouldBeLessThan(100_000, "a merge that does not end would hang the sync");
        }
    }

    /// <summary>One step in a transaction of its own: how far <c>total_changes()</c> moved and what <c>changes()</c> says.</summary>
    private static (long Delta, long Changes) Step(SqliteConnection connection, int pages)
    {
        Exec(connection, "BEGIN IMMEDIATE;");
        var before = TotalChanges(connection);
        using (var merge = connection.CreateCommand())
        {
            merge.CommandText = LinkedSource.MergeSql("f");
            merge.Parameters.AddWithValue("$pages", pages);
            merge.ExecuteNonQuery();
        }

        var delta = TotalChanges(connection) - before;
        using var changes = connection.CreateCommand();
        changes.CommandText = "SELECT changes();";
        var reported = (long)changes.ExecuteScalar()!;
        Exec(connection, "COMMIT;");
        return (delta, reported);
    }

    private static long Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (long)command.ExecuteScalar()!;
    }

    private static long TotalChanges(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = LinkedSource.TotalChangesSql;
        return (long)command.ExecuteScalar()!;
    }

    /// <summary>Loads rows in transactions of their own, each an FTS5 segment while the automatic merge is off.</summary>
    private static void Load(SqliteConnection connection, long firstRowid, int batches, int rowsPerBatch)
    {
        var random = new Random(271);
        var rowid = firstRowid;
        for (var batch = 0; batch < batches; batch++)
        {
            using var transaction = connection.BeginTransaction();
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO f(rowid, t) VALUES ($rowid, $text);";
            var id = insert.Parameters.Add("$rowid", SqliteType.Integer);
            var text = insert.Parameters.Add("$text", SqliteType.Text);
            for (var row = 0; row < rowsPerBatch; row++)
            {
                id.Value = rowid++;
                text.Value = Word(random) + " " + Word(random);
                insert.ExecuteNonQuery();
            }

            transaction.Commit();
        }
    }

    private static string Word(Random random)
    {
        var letters = new char[random.Next(4, 11)];
        for (var i = 0; i < letters.Length; i++)
        {
            letters[i] = (char)('a' + random.Next(26));
        }

        return new string(letters);
    }

    private static void Exec(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
