using Shouldly;

namespace Nordmann.LUI.Integration.Tests.Catalog;

/// <summary>
/// The runtimes of the catalog have a lifetime (ADR-0075, issue #265, review of pull request 286): what the catalog keeps for a
/// definition dies with the definition object, and what another process made stale does not stay. A host that builds a definition
/// per request — with a similarity or a map that captures something — must not grow the memory of the catalog by one runtime (about
/// 20 KB) per request.
/// </summary>
[Collection("Heap measurement")]
public sealed class RuntimeLifetimeTests
{
    private const int Requests = 2_000;

    /// <summary>What the heap may grow by: a runtime per request would be 40 MB and more.</summary>
    private const long MemoryBound = 8_000_000;

    private static readonly LuiIndexDefinition<long> Tags = new("tags", index => index.SingleWord());

    private static readonly LuiIndexDefinition<long> TagsOfWords = new("tags", index => index.MultiWord());

    [Fact]
    public void Short_lived_definition_objects_with_their_own_similarity_do_not_grow_the_catalog()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        catalog.Create(Tags).Upsert(1, "vegan");

        var before = GC.GetTotalMemory(forceFullCollection: true);
        OpenWithADefinitionPerRequest(catalog);
        CollectEverything();
        var grown = GC.GetTotalMemory(forceFullCollection: true) - before;

        catalog.CachedHandles.ShouldBeLessThan(Requests / 10, "the handles die with their definition objects");
        grown.ShouldBeLessThan(MemoryBound, $"{Requests:N0} requests grew the heap by {grown:N0} B — a runtime is about 20 KB, and it dies with its definition");
    }

    [Fact]
    public void Short_lived_linked_definitions_with_a_capturing_map_do_not_grow_the_catalog()
    {
        using var database = new TestDatabase();
        Linked.LinkedTable.Run(database, Linked.LinkedTable.Table);
        using var catalog = database.Open();
        catalog.Create(new LuiLinkedIndexDefinition<long, string>(
            "slugs", index => index.SingleWord().LinkTo("tag", "id", "name").WithPayload(["slug"], row => row.GetString("slug"))));

        var before = GC.GetTotalMemory(forceFullCollection: true);
        OpenALinkedDefinitionPerRequest(catalog);
        CollectEverything();
        var grown = GC.GetTotalMemory(forceFullCollection: true) - before;

        catalog.CachedHandles.ShouldBeLessThan(Requests / 10);
        grown.ShouldBeLessThan(MemoryBound, $"{Requests:N0} requests grew the heap by {grown:N0} B");
    }

    [Fact]
    public void A_runtime_that_another_catalog_made_stale_is_not_kept()
    {
        using var database = new TestDatabase();
        using var first = database.Open();
        using var second = database.Open();
        first.Create(Tags).Upsert(1, "vegan");
        var old = WeakHandleOf(second);
        _ = second.Index(Tags).Count();

        first.DropIndex("tags");
        first.Create(TagsOfWords);
        Should.Throw<LuiDefinitionMismatchException>(() => second.Index(Tags).Count());

        // the next open call finds the failed handle and does not keep it
        Should.Throw<LuiDefinitionMismatchException>(() => second.Index(Tags));
        _ = second.Index(TagsOfWords).Count();
        CollectEverything();

        old.IsAlive.ShouldBeFalse("nothing in the catalog holds the handle and the runtime of the index that was created again");
        second.CachedHandles.ShouldBeLessThanOrEqualTo(2);
    }

    [Fact]
    public void Two_definition_objects_of_one_index_use_the_similarity_and_the_map_each_of_them_declares()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var plain = catalog.Create(Tags);
        plain.UpsertMany([new LuiEntry<long>(1, "vegan"), new LuiEntry<long>(2, "vegetarisch")]);
        var constant = new LuiIndexDefinition<long>("tags", index => index.SingleWord().Similarity(new Constant(0.25)));

        var scored = catalog.Index(constant).Search("veg", new LuiQueryOptions { Rank = LuiRankingOrder.SimilarityOnly });

        scored.Hits.ShouldAllBe(hit => hit.Similarity == 0.25, "the similarity of the definition takes effect");
        plain.Search("veg", new LuiQueryOptions { Rank = LuiRankingOrder.SimilarityOnly }).Hits.ShouldAllBe(hit => hit.Similarity != 0.25);
    }

    /// <summary>The handle, held weakly — in a method of its own, so that no local of the test keeps it alive.</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference WeakHandleOf(LuiCatalog catalog) => new(catalog.Index(Tags));

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void OpenWithADefinitionPerRequest(LuiCatalog catalog)
    {
        for (var request = 0; request < Requests; request++)
        {
            var similarity = new Constant(request);
            var definition = new LuiIndexDefinition<long>("tags", index => index.SingleWord().Similarity(similarity));
            _ = catalog.Index(definition);
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void OpenALinkedDefinitionPerRequest(LuiCatalog catalog)
    {
        for (var request = 0; request < Requests; request++)
        {
            var suffix = request.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var definition = new LuiLinkedIndexDefinition<long, string>(
                "slugs", index => index.SingleWord().LinkTo("tag", "id", "name").WithPayload(["slug"], row => row.GetString("slug") + suffix));
            _ = catalog.Index(definition);
        }
    }

    private static void CollectEverything()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private sealed class Constant(double score) : ILuiSimilarity
    {
        public double Similarity(string query, string text) => score;
    }
}
