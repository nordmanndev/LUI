using Nordmann.LUI.Indexing;
using Shouldly;

namespace Nordmann.LUI.Tests;

/// <summary>
/// The rowid carries priority and weight (LUI-Spezifikation §7.2): the round
/// trip, and the order that <c>ORDER BY rowid</c> relies on.
/// </summary>
public sealed class RowIdTests
{
    [Theory]
    [InlineData(Priority.Normal, 0, 0L)]
    [InlineData(Priority.Normal, 536_870_911, 4_294_967_295L)]
    [InlineData(Priority.System, 5, 42L)]
    [InlineData(Priority.High, 1_000, 1L)]
    [InlineData(Priority.Elevated, 536_870_911, 0L)]
    public void Encoding_and_decoding_is_a_round_trip(Priority priority, int weight, long docId)
    {
        var rowId = RowId.Encode(priority, weight, docId);

        RowId.PriorityOf(rowId).ShouldBe(priority);
        RowId.WeightOf(rowId).ShouldBe(weight);
        RowId.DocIdOf(rowId).ShouldBe(docId);
    }

    [Fact]
    public void The_rowid_is_never_negative()
    {
        RowId.Encode(Priority.Normal, 0, RowId.MaxDocId).ShouldBeGreaterThan(0);
    }

    [Fact]
    public void A_higher_priority_comes_first_whatever_the_weight()
    {
        var system = RowId.Encode(Priority.System, weight: 0, docId: 1);
        var normal = RowId.Encode(Priority.Normal, weight: RowId.MaxWeight, docId: 1);

        system.ShouldBeLessThan(normal);
    }

    [Fact]
    public void Within_a_priority_a_higher_weight_comes_first()
    {
        var heavy = RowId.Encode(Priority.Normal, weight: 900, docId: 7);
        var light = RowId.Encode(Priority.Normal, weight: 10, docId: 1);

        heavy.ShouldBeLessThan(light);
    }

    [Fact]
    public void With_priority_and_weight_equal_the_smaller_doc_id_comes_first()
    {
        RowId.Encode(Priority.Normal, 5, 1).ShouldBeLessThan(RowId.Encode(Priority.Normal, 5, 2));
    }

    [Fact]
    public void The_same_arithmetic_runs_in_sql()
    {
        // the generated column and the C# encoder must agree, or the decoded weight is wrong
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var index = catalog.Index<long, Chip>("tags").SingleWord().Owned().Ensure();

        index.Upsert(1, "vegan", weight: 5, priority: Priority.System);
        index.Upsert(2, "organ", weight: 123_456);

        var system = database.Scalar<long>("SELECT s_rowid FROM lui_tags WHERE key = '1'");
        var normal = database.Scalar<long>("SELECT s_rowid FROM lui_tags WHERE key = '2'");
        var docOfNormal = database.Scalar<long>("SELECT doc_id FROM lui_tags WHERE key = '2'");

        system.ShouldBe(RowId.Encode(Priority.System, 5, database.Scalar<long>("SELECT doc_id FROM lui_tags WHERE key = '1'")));
        normal.ShouldBe(RowId.Encode(Priority.Normal, 123_456, docOfNormal));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(536_870_912)]
    public void A_weight_out_of_range_is_refused(int weight)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => RowId.Encode(Priority.Normal, weight, 1));
    }

    [Fact]
    public void A_priority_out_of_range_is_refused()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => RowId.Encode((Priority)4, 1, 1));
    }
}
