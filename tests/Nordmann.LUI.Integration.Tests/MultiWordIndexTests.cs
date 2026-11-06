using Nordmann.LUI.Schema;
using Microsoft.Data.Sqlite;
using Nordmann.LUI.Normalization;
using Shouldly;

namespace Nordmann.LUI.Integration.Tests;

/// <summary>
/// The <c>MultiWord</c> index: the schema, the candidates and the ranking of names and titles
/// (LUI-Spezifikation §4, §7, §8.2, §9, Anhang B).
/// </summary>
public sealed class MultiWordIndexTests
{
    [Fact]
    public void Ensure_creates_the_compact_column_and_a_search_index_over_it_alone()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();

        catalog.Create("recipes", index => index.MultiWord());

        Columns(database, "lui_recipes").Contains("s_compact", StringComparer.Ordinal).ShouldBeTrue();
        Columns(database, "lui_recipes").Contains("s_norm", StringComparer.Ordinal).ShouldBeTrue("s_norm stays a column of the table");
        Columns(database, "lui_recipes_fts").ShouldBe(["s_compact"], "ADR-0011: the search index holds the compact column only");
        database.Scalar<string>("SELECT kind FROM lui_meta WHERE index_name = 'recipes'").ShouldBe("MultiWord");
    }

    [Fact]
    public void A_single_word_index_has_no_compact_column()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();

        catalog.Create("tags", index => index.SingleWord());

        Columns(database, "lui_tags").Contains("s_compact", StringComparer.Ordinal).ShouldBeFalse();
        Columns(database, "lui_tags_fts").ShouldBe(["s_norm"]);
    }

    [Theory]
    [InlineData("Gedeckter Apfelkuchen mit Äpfeln")]
    [InlineData("Straße")]
    [InlineData("Café Crème")]
    [InlineData("Apfel-Zimt-Kuchen")]
    [InlineData("Der Herr der Ringe")]
    public void The_compact_column_is_the_compact_form_the_normalizer_computes(string text)
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var recipes = catalog.Create("recipes", index => index.MultiWord());

        recipes.Upsert(1, text, 1);

        // the column is not folded (the tokenizer folds it); once folded it is the compact form
        var column = database.Scalar<string>("SELECT s_compact FROM lui_recipes");
        Normalizer.ToSearchForm(column).ShouldBe(Normalizer.ToCompactForm(text));
    }

    [Fact]
    public void A_different_kind_is_a_different_definition()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        catalog.Create("recipes", index => index.MultiWord());

        Should.Throw<LuiDefinitionMismatchException>(
            () => catalog.Create("recipes", index => index.SingleWord()));
    }

    [Fact]
    public void The_default_similarity_of_a_multi_word_index_is_n_gram_dice()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var recipes = catalog.Create("recipes", index => index.MultiWord());
        recipes.Upsert(1, "Apfelkuchen", 1);

        var expected = LuiSimilarity.NgramDice.Similarity("apfelkuch", "apfelkuchen");
        var jaro = LuiSimilarity.JaroWinkler.Similarity("apfelkuch", "apfelkuchen");

        recipes.Query("apfelkuch").Execute().Hits.ShouldHaveSingleItem().Similarity.ShouldBe(expected, 1e-9);
        expected.ShouldNotBe(jaro, "the two measures must differ here, or the test proves nothing");
    }

    [Fact]
    public void The_search_index_stays_consistent_through_every_kind_of_write()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var recipes = catalog.Create("recipes", index => index.MultiWord());

        for (var i = 1; i <= 40; i++)
        {
            recipes.Upsert(i, $"Gedeckter Apfelkuchen Nummer {i}", i);
        }

        for (var i = 1; i <= 40; i += 2)
        {
            recipes.UpdateWeight(i, 1_000 - i);
            recipes.UpdatePriority(i + 1, LuiPriority.High);
        }

        recipes.Upsert(3, "Birnen Torte", 3);
        recipes.Remove(5);

        using var connection = database.Connect();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO lui_recipes_fts(lui_recipes_fts, rank) VALUES ('integrity-check', 1);";
        Should.NotThrow(() => command.ExecuteNonQuery());
        recipes.Query("birnen torte").Execute().Hits.ShouldHaveSingleItem().Key.ShouldBe(3);
        recipes.Query("apfelkuchen nummer").Take(100).Execute().Hits.Select(h => h.Key).ShouldNotContain(5L);
    }

    // ---- Anhang B -----------------------------------------------------------------------------

    [Fact]
    public void apfelk_finds_the_apfelkuchen_family()
    {
        using var database = new TestDatabase();
        using var recipes = Recipes(database);

        var texts = recipes.Index.Query("apfelk").Execute().Hits.Select(h => h.Text).ToArray();

        texts.Take(3).ShouldBe(["Apfelkuchen", "Gedeckter Apfelkuchen", "Apfel Kuchen"], ignoreOrder: true);
        texts.Contains("Apfelstrudel", StringComparer.Ordinal).ShouldBeFalse();
    }

    [Theory]
    [InlineData("apfelkuch")]
    [InlineData("apfel kuch")]
    [InlineData("Apfel Kuch")]
    public void apfelkuch_puts_the_word_starts_first_and_bratapfelkuchen_behind_them(string typed)
    {
        using var database = new TestDatabase();
        using var recipes = Recipes(database);

        var hits = recipes.Index.Query(typed).Execute().Hits;

        hits.Take(5).Select(h => h.Text).ShouldBe(
            ["Apfelkuchen", "Gedeckter Apfelkuchen", "Apfel Kuchen", "Apfelkuchen mit Sahne", "Apfelkuchen mit Äpfeln"],
            ignoreOrder: true);
        hits.Select(h => h.Text).Last().ShouldBe("Bratapfelkuchen", "more popular, but not at a word start");
        hits.Select(h => h.AtWordStart).ShouldBe([true, true, true, true, true, false]);
    }

    [Fact]
    public void The_order_of_the_words_does_not_matter()
    {
        using var database = new TestDatabase();
        using var recipes = Recipes(database);

        var hits = recipes.Index.Query("kuchen apfel").Execute().Hits.Select(h => h.Text).ToArray();

        hits.Take(2).ShouldBe(["Apfel Kuchen", "Apfelkuchen"], ignoreOrder: true);
        hits.Length.ShouldBe(6, "every title that holds both words");

        recipes.Index.Query("sahne apfelkuchen").Execute().Hits.ShouldHaveSingleItem().Text.ShouldBe("Apfelkuchen mit Sahne");
    }

    [Fact]
    public void A_second_word_narrows_the_search()
    {
        using var database = new TestDatabase();
        using var recipes = Recipes(database);

        recipes.Index.Query("apfelkuchen sahne").Execute().Hits.ShouldHaveSingleItem().Text.ShouldBe("Apfelkuchen mit Sahne");
        recipes.Index.Query("apfelkuchen strudel").Execute().Hits.ShouldBeEmpty();
    }

    [Fact]
    public void A_word_of_fewer_than_three_characters_decides_the_ranking_not_the_candidates()
    {
        using var database = new TestDatabase();
        using var recipes = Recipes(database);

        var hits = recipes.Index.Query("apfelkuchen mi").Execute().Hits;

        hits.Select(h => h.Text).Contains("Apfelkuchen", StringComparer.Ordinal).ShouldBeTrue("the short word did not narrow the candidates");
        hits.Take(2).Select(h => h.Text).ShouldBe(
            ["Apfelkuchen mit Sahne", "Apfelkuchen mit Äpfeln"],
            ignoreOrder: true);
        hits.Take(2).ShouldAllBe(h => h.AtWordStart, "the compact form apfelkuchenmi stands at a word start there");
        hits.Skip(2).ShouldAllBe(h => !h.AtWordStart);
    }

    [Fact]
    public void The_words_of_the_input_may_start_in_the_middle_of_the_title()
    {
        using var database = new TestDatabase();
        using var recipes = Recipes(database);

        recipes.Index.Query("gedeckter apfel").Execute().Hits[0].Text.ShouldBe("Gedeckter Apfelkuchen");
        recipes.Index.Query("apfel mit").Execute().Hits.Select(h => h.Text)
            .ShouldBe(["Apfelkuchen mit Sahne", "Apfelkuchen mit Äpfeln"], ignoreOrder: true);
    }

    [Theory]
    [InlineData("äpfeln")]
    [InlineData("aepfeln")]
    [InlineData("ÄPFELN")]
    public void The_umlaut_and_its_spelling_find_the_same_title(string typed)
    {
        using var database = new TestDatabase();
        using var recipes = Recipes(database);

        recipes.Index.Query(typed).Execute().Hits.ShouldHaveSingleItem().Text.ShouldBe("Apfelkuchen mit Äpfeln");
    }

    [Theory]
    [InlineData("strasse")]
    [InlineData("Straße")]
    public void Sharp_s_and_double_s_are_the_same(string typed)
    {
        using var database = new TestDatabase();
        using var recipes = Recipes(database);

        recipes.Index.Query(typed).Execute().Hits.ShouldHaveSingleItem().Text.ShouldBe("Hauptstraße");
    }

    [Fact]
    public void One_and_two_letters_give_the_most_popular_titles_that_begin_so()
    {
        using var database = new TestDatabase();
        using var recipes = Recipes(database);

        recipes.Index.Query("a").Execute().Hits.Select(h => h.Text)
            .Take(3).ShouldBe(["Apfelstrudel", "Apfelkuchen", "Apfel Kuchen"]);
        recipes.Index.Query("ap").Execute().Hits.Select(h => h.Text)
            .Take(3).ShouldBe(["Apfelstrudel", "Apfelkuchen", "Apfel Kuchen"]);
    }

    [Fact]
    public void One_and_two_letters_do_not_depend_on_spaces_or_punctuation()
    {
        // the prefix path of a multi-word index works on the compact text: the answer to „ab", „a b", „a-b"
        // and „-a b" is the same (review of pull request 8, M1)
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var titles = catalog.Create("titles", index => index.MultiWord());
        titles.Upsert(1, "Abendbrot", 100);
        titles.Upsert(2, "A Bit Of Fun", 90);
        titles.Upsert(3, "Anna Berg", 80);
        titles.Upsert(4, "Ab Cd Torte", 70);
        titles.Upsert(5, "Abc", 60);
        titles.Upsert(6, "x-y Test", 50);
        titles.Upsert(7, "Xylophon", 40);

        foreach (var typed in new[] { "ab", "a b", "a-b", "a.b", "AB", "-a b", "  a  b  " })
        {
            Keys(titles, typed).ShouldBe([1L, 2L, 4L, 5L], $"for '{typed}'");
        }

        foreach (var typed in new[] { "a", "-a", "#a", "A " })
        {
            Keys(titles, typed).ShouldBe([1L, 2L, 3L, 4L, 5L], $"for '{typed}'");
        }

        foreach (var typed in new[] { "xy", "x y", "x-y", "X.Y" })
        {
            Keys(titles, typed).ShouldBe([6L, 7L], $"for '{typed}'");
        }

        foreach (var typed in new[] { "-", "- -", ".", "#" })
        {
            Keys(titles, typed).ShouldBeEmpty($"for '{typed}'");
        }
    }

    [Fact]
    public void A_hundred_words_are_a_valid_query()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var titles = catalog.Create("titles", index => index.MultiWord());
        titles.Upsert(1, "wort001 wort002 wort003", 10);

        var typed = string.Join(' ', Enumerable.Range(0, 100).Select(i => $"wort{i:000}"));

        Should.NotThrow(() => titles.Query(typed).Execute());
        titles.Query("wort001 wort002 wort003 " + string.Join(' ', Enumerable.Range(4, 20).Select(i => $"wort{i:000}")))
            .Execute().Hits.ShouldBeEmpty("a title that lacks a word of the longest eight is not a candidate");
    }

    [Fact]
    public void A_hyphen_separates_words_in_the_input_and_in_the_title()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var recipes = catalog.Create("recipes", index => index.MultiWord());
        recipes.Upsert(1, "Apfel-Zimt-Kuchen", 10);
        recipes.Upsert(2, "Zimtschnecken", 20);

        foreach (var typed in new[] { "zimt kuchen", "kuchen zimt", "apfel-zimt", "apfelzimtkuchen", "zimt-kuchen" })
        {
            recipes.Query(typed).Execute().Hits.Select(h => h.Key).ShouldBe([1L], $"for '{typed}'");
        }
    }

    [Fact]
    public void The_priority_comes_first_for_a_multi_word_index_too()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var recipes = catalog.Create("recipes", index => index.MultiWord());
        recipes.Upsert(1, "Apfel Kuchen", 900);
        recipes.Upsert(2, "Brat Apfel Kuchen", 0, priority: LuiPriority.System);

        recipes.Query("apfel kuchen").Execute().Hits.Select(h => h.Key).ShouldBe([2L, 1L]);
        recipes.Query("apfel kuchen").PriorityMode(LuiPriorityMode.CandidatesOnly).Execute().Hits.Select(h => h.Key).ShouldBe([1L, 2L]);
    }

    [Fact]
    public void A_title_is_found_by_its_words_in_any_case()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var people = catalog.Create("people", index => index.MultiWord());
        people.Upsert(1, "Anna Müller", 10);
        people.Upsert(2, "Anna Schmidt", 20);
        people.Upsert(3, "Hans Müller", 30);

        people.Query("müller anna").Execute().Hits.ShouldHaveSingleItem().Key.ShouldBe(1);
        people.Query("MUELLER").Execute().Hits.Select(h => h.Key).ShouldBe([3L, 1L]);
        people.Query("anna").Execute().Hits.Select(h => h.Key).ShouldBe([2L, 1L]);
    }

    [Fact]
    public void The_compact_column_finds_everything_the_two_columns_of_the_specification_found_and_little_more()
    {
        // §8.2 as written: (s_norm : (w1 AND w2)) OR (s_compact : "w1w2"). The search index holds s_compact only
        // (ADR-0011); its answer must contain that set, and the rest must be titles whose words run across a
        // boundary of the title — checked here without FTS5, on the folded texts.
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var recipes = catalog.Create("recipes", index => index.MultiWord());

        var random = new Random(4);
        var titles = RandomTitles(random, 1_500);
        Load(catalog, recipes, titles);

        var rowIds = RowIds(database, titles.Count);
        var searchForms = titles.Select(t => Normalizer.ToSearchForm(t)).ToArray();
        var compactForms = titles.Select(t => Normalizer.ToCompactForm(t)).ToArray();

        using var raw = database.Connect();
        var checkedQueries = 0;
        var extra = 0;
        for (var i = 0; i < 600; i++)
        {
            var query = RandomQuery(titles, random);
            var prepared = Querying.PreparedQuery.Create(query, NormalizationOptions.Default, MultiWordRules.Instance);
            var longWords = prepared.Words.Where(w => w.Length >= 3).ToArray();
            if (prepared.IsPrefixPath || longWords.Length == 0)
            {
                continue;
            }

            var ours = Match(raw, prepared.Expression);

            // the specification's set, computed on the folded texts
            var specification = Enumerable.Range(0, titles.Count)
                .Where(t => longWords.All(w => searchForms[t].Contains(w, StringComparison.Ordinal))
                         || compactForms[t].Contains(prepared.Compact, StringComparison.Ordinal))
                .Select(t => rowIds[t])
                .Order()
                .ToArray();

            // the compact column: every title whose compact text holds every word
            var expected = Enumerable.Range(0, titles.Count)
                .Where(t => longWords.All(w => compactForms[t].Contains(w, StringComparison.Ordinal)))
                .Select(t => rowIds[t])
                .Order()
                .ToArray();

            ours.ShouldBe(expected, $"for '{query}'");
            specification.ShouldBeSubsetOf(ours, $"for '{query}'");
            extra += ours.Length - specification.Length;
            checkedQueries++;
        }

        checkedQueries.ShouldBeGreaterThan(300);
        extra.ShouldBeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void A_word_that_runs_across_a_boundary_of_the_title_is_found_but_no_word_start()
    {
        // the one thing the compact column adds to the two columns of the first version of §8.2 (ADR-0011)
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var recipes = catalog.Create("recipes", index => index.MultiWord());
        recipes.Upsert(1, "Apfel Kuchen Torte", 10);
        recipes.Upsert(2, "Elke Ortmann", 5);

        var hits = recipes.Query("elk ort").Execute().Hits;

        hits.Select(h => h.Key).ShouldBe([1L, 2L], ignoreOrder: true);
        hits.Single(h => h.Key == 1).AtWordStart.ShouldBeFalse();
        hits.Single(h => h.Key == 2).AtWordStart.ShouldBeFalse("the compact form elkort is not at a word start of elkeortmann either");
    }

    [Fact]
    public void A_change_of_the_separators_alone_does_not_touch_the_search_index()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var recipes = catalog.Create("recipes", index => index.MultiWord());
        recipes.Upsert(1, "Apfel Kuchen", 10);
        var before = database.Scalar<string>("SELECT count(*) || ':' || coalesce(sum(length(block)), 0) || ':' || coalesce(sum(id), 0) FROM lui_recipes_fts_data");

        recipes.Upsert(1, "Apfel-Kuchen", 10);

        database.Scalar<string>("SELECT count(*) || ':' || coalesce(sum(length(block)), 0) || ':' || coalesce(sum(id), 0) FROM lui_recipes_fts_data").ShouldBe(before);
        recipes.Query("apfel kuchen").Execute().Hits.ShouldHaveSingleItem().Text.ShouldBe("Apfel-Kuchen");
    }

    private static long[] Keys(ILuiEmbeddedIndex index, string typed) =>
        [.. index.Query(typed).Execute().Hits.Select(h => h.Key.ToInt64()).Order()];

    private static List<string> RandomTitles(Random random, int count)
    {
        var words = File.ReadLines(TestCorpus.SampleWordListPath)
            .Where(w => w.Length >= 3).ToList();

        return [.. Enumerable.Range(0, count)
            .Select(_ => string.Join(random.Next(3) == 0 ? "-" : " ", Enumerable.Range(0, random.Next(1, 4)).Select(_ => words[random.Next(words.Count)])))];
    }

    private static void Load(LuiCatalog catalog, ILuiEmbeddedIndex index, List<string> titles)
    {
        using var connection = catalog.Database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        for (var i = 0; i < titles.Count; i++)
        {
            index.Upsert(i + 1, titles[i], i, transaction: transaction);
        }

        transaction.Commit();
    }

    private static string RandomQuery(List<string> titles, Random random)
    {
        var title = titles[random.Next(titles.Count)].Replace('-', ' ');
        var parts = title.Split(' ');
        if (random.Next(2) == 0)
        {
            var length = Math.Min(title.Length, random.Next(3, 9));
            return title.Substring(random.Next(title.Length - length + 1), length).Trim();
        }

        var pieces = parts.Take(2).Select(w => w[..Math.Min(w.Length, random.Next(3, 6))]).ToArray();
        if (random.Next(2) == 0)
        {
            Array.Reverse(pieces);
        }

        return string.Join(' ', pieces);
    }

    /// <summary>The rowid of the title that was added as number <c>i + 1</c>, by position.</summary>
    private static long[] RowIds(TestDatabase database, int count)
    {
        var rowIds = new long[count];
        using var connection = database.Connect();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT CAST(key AS INTEGER), s_rowid FROM lui_recipes;";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            rowIds[reader.GetInt64(0) - 1] = reader.GetInt64(1);
        }

        return rowIds;
    }

    private static long[] Match(SqliteConnection connection, string expression)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT rowid FROM lui_recipes_fts WHERE lui_recipes_fts MATCH $e ORDER BY rowid;";
        command.Parameters.AddWithValue("$e", expression);
        var rows = new List<long>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(reader.GetInt64(0));
        }

        return [.. rows];
    }

    private static Fixture Recipes(TestDatabase database)
    {
        var catalog = database.Open();
        var recipes = catalog.Create("recipes", index => index.MultiWord());
        recipes.Upsert(1, "Apfelkuchen", 900);
        recipes.Upsert(2, "Gedeckter Apfelkuchen", 800);
        recipes.Upsert(3, "Apfel Kuchen", 700);
        recipes.Upsert(4, "Apfelkuchen mit Sahne", 400);
        recipes.Upsert(5, "Bratapfelkuchen", 9_000);
        recipes.Upsert(6, "Apfelstrudel", 5_000);
        recipes.Upsert(7, "Apfelkuchen mit Äpfeln", 300);
        recipes.Upsert(8, "Hauptstraße", 100);
        return new Fixture(recipes);
    }

    private static List<string> Columns(TestDatabase database, string table)
    {
        using var connection = database.Connect();
        using var command = connection.CreateCommand();
        // table_xinfo lists the generated columns of a table; the columns of a search index are plain
        command.CommandText = $"SELECT name FROM pragma_table_xinfo('{table}') WHERE hidden IN (0, 2, 3) ORDER BY cid;";
        var names = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    private sealed class Fixture(ILuiEmbeddedIndex index) : IDisposable
    {
        internal ILuiEmbeddedIndex Index => index;

        public void Dispose()
        {
        }
    }
}
