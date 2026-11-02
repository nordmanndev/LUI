using Microsoft.Data.Sqlite;
using Nordmann.LUI.Normalization;
using Shouldly;

namespace Nordmann.LUI.Tests;

/// <summary>
/// The <c>MultiWord</c> index: the schema, the candidates and the ranking of names and titles
/// (LUI-Spezifikation §4, §7, §8.2, §9, Anhang B).
/// </summary>
public sealed class MultiWordIndexTests
{
    [Fact]
    public void Ensure_creates_the_compact_column_and_a_search_index_over_both_columns()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();

        catalog.Index<long, Chip>("recipes").MultiWord().Owned().Ensure();

        Columns(database, "lui_recipes").Contains("s_compact", StringComparer.Ordinal).ShouldBeTrue();
        Columns(database, "lui_recipes_fts").ShouldBe(["s_norm", "s_compact"]);
        database.Scalar<string>("SELECT kind FROM lui_meta WHERE index_name = 'recipes'").ShouldBe("MultiWord");
    }

    [Fact]
    public void A_single_word_index_has_no_compact_column()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();

        catalog.Index<long, Chip>("tags").SingleWord().Owned().Ensure();

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
        var index = catalog.Index<long, Chip>("recipes").MultiWord().Owned().Ensure();

        index.Upsert(1, text, 1);

        // the column is not folded (the tokenizer folds it); once folded it is the compact form
        var column = database.Scalar<string>("SELECT s_compact FROM lui_recipes");
        Normalizer.ToSearchForm(column).ShouldBe(Normalizer.ToCompactForm(text));
    }

    [Fact]
    public void A_different_kind_is_a_different_definition()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        catalog.Index<long, Chip>("recipes").MultiWord().Owned().Ensure();

        Should.Throw<LuiDefinitionMismatchException>(
            () => catalog.Index<long, Chip>("recipes").SingleWord().Owned().Ensure());
    }

    [Fact]
    public void The_default_similarity_of_a_multi_word_index_is_n_gram_dice()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var index = catalog.Index<long, Chip>("recipes").MultiWord().Owned().Ensure();
        index.Upsert(1, "Apfelkuchen", 1);

        var expected = Similarity.Similarity.NgramDice.Similarity("apfelkuch", "apfelkuchen");
        var jaro = Similarity.Similarity.JaroWinkler.Similarity("apfelkuch", "apfelkuchen");

        index.Query("apfelkuch").Execute().Hits.ShouldHaveSingleItem().Similarity.ShouldBe(expected, 1e-9);
        expected.ShouldNotBe(jaro, "the two measures must differ here, or the test proves nothing");
    }

    [Fact]
    public void The_search_index_stays_consistent_through_every_kind_of_write()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var index = catalog.Index<long, Chip>("recipes").MultiWord().Owned().Ensure();

        for (var i = 1; i <= 40; i++)
        {
            index.Upsert(i, $"Gedeckter Apfelkuchen Nummer {i}", i);
        }

        for (var i = 1; i <= 40; i += 2)
        {
            index.UpdateWeight(i, 1_000 - i);
            index.UpdatePriority(i + 1, Priority.High);
        }

        index.Upsert(3, "Birnen Torte", 3);
        index.Remove(5);

        using var connection = database.Connect();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO lui_recipes_fts(lui_recipes_fts, rank) VALUES ('integrity-check', 1);";
        Should.NotThrow(() => command.ExecuteNonQuery());
        index.Query("birnen torte").Execute().Hits.ShouldHaveSingleItem().Key.ShouldBe(3);
        index.Query("apfelkuchen nummer").Take(100).Execute().Hits.Select(h => h.Key).ShouldNotContain(5L);
    }

    // ---- Anhang B -----------------------------------------------------------------------------

    [Fact]
    public void apfelk_finds_the_apfelkuchen_family()
    {
        using var database = new TestDatabase();
        using var index = Recipes(database);

        var texts = index.Index.Query("apfelk").Execute().Hits.Select(h => h.Text).ToArray();

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
        using var index = Recipes(database);

        var hits = index.Index.Query(typed).Execute().Hits;

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
        using var index = Recipes(database);

        var hits = index.Index.Query("kuchen apfel").Execute().Hits.Select(h => h.Text).ToArray();

        hits.Take(2).ShouldBe(["Apfel Kuchen", "Apfelkuchen"], ignoreOrder: true);
        hits.Length.ShouldBe(6, "every title that holds both words");

        index.Index.Query("sahne apfelkuchen").Execute().Hits.ShouldHaveSingleItem().Text.ShouldBe("Apfelkuchen mit Sahne");
    }

    [Fact]
    public void A_second_word_narrows_the_search()
    {
        using var database = new TestDatabase();
        using var index = Recipes(database);

        index.Index.Query("apfelkuchen sahne").Execute().Hits.ShouldHaveSingleItem().Text.ShouldBe("Apfelkuchen mit Sahne");
        index.Index.Query("apfelkuchen strudel").Execute().Hits.ShouldBeEmpty();
    }

    [Fact]
    public void A_word_of_fewer_than_three_characters_decides_the_ranking_not_the_candidates()
    {
        using var database = new TestDatabase();
        using var index = Recipes(database);

        var hits = index.Index.Query("apfelkuchen mi").Execute().Hits;

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
        using var index = Recipes(database);

        index.Index.Query("gedeckter apfel").Execute().Hits[0].Text.ShouldBe("Gedeckter Apfelkuchen");
        index.Index.Query("apfel mit").Execute().Hits.Select(h => h.Text)
            .ShouldBe(["Apfelkuchen mit Sahne", "Apfelkuchen mit Äpfeln"], ignoreOrder: true);
    }

    [Theory]
    [InlineData("äpfeln")]
    [InlineData("aepfeln")]
    [InlineData("ÄPFELN")]
    public void The_umlaut_and_its_spelling_find_the_same_title(string typed)
    {
        using var database = new TestDatabase();
        using var index = Recipes(database);

        index.Index.Query(typed).Execute().Hits.ShouldHaveSingleItem().Text.ShouldBe("Apfelkuchen mit Äpfeln");
    }

    [Theory]
    [InlineData("strasse")]
    [InlineData("Straße")]
    public void Sharp_s_and_double_s_are_the_same(string typed)
    {
        using var database = new TestDatabase();
        using var index = Recipes(database);

        index.Index.Query(typed).Execute().Hits.ShouldHaveSingleItem().Text.ShouldBe("Hauptstraße");
    }

    [Fact]
    public void One_and_two_letters_give_the_most_popular_titles_that_begin_so()
    {
        using var database = new TestDatabase();
        using var index = Recipes(database);

        index.Index.Query("a").Execute().Hits.Select(h => h.Text)
            .Take(3).ShouldBe(["Apfelstrudel", "Apfelkuchen", "Apfel Kuchen"]);
        index.Index.Query("ap").Execute().Hits.Select(h => h.Text)
            .Take(3).ShouldBe(["Apfelstrudel", "Apfelkuchen", "Apfel Kuchen"]);
    }

    [Fact]
    public void A_hyphen_separates_words_in_the_input_and_in_the_title()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var index = catalog.Index<long, Chip>("recipes").MultiWord().Owned().Ensure();
        index.Upsert(1, "Apfel-Zimt-Kuchen", 10);
        index.Upsert(2, "Zimtschnecken", 20);

        foreach (var typed in new[] { "zimt kuchen", "kuchen zimt", "apfel-zimt", "apfelzimtkuchen", "zimt-kuchen" })
        {
            index.Query(typed).Execute().Hits.Select(h => h.Key).ShouldBe([1L], $"for '{typed}'");
        }
    }

    [Fact]
    public void The_priority_comes_first_for_a_multi_word_index_too()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var index = catalog.Index<long, Chip>("recipes").MultiWord().Owned().Ensure();
        index.Upsert(1, "Apfel Kuchen", 900);
        index.Upsert(2, "Brat Apfel Kuchen", 0, priority: Priority.System);

        index.Query("apfel kuchen").Execute().Hits.Select(h => h.Key).ShouldBe([2L, 1L]);
        index.Query("apfel kuchen").Priorities(PriorityRanking.CandidatesOnly).Execute().Hits.Select(h => h.Key).ShouldBe([1L, 2L]);
    }

    [Fact]
    public void A_title_is_found_by_its_words_in_any_case()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var index = catalog.Index<long, Chip>("people").MultiWord().Owned().Ensure();
        index.Upsert(1, "Anna Müller", 10);
        index.Upsert(2, "Anna Schmidt", 20);
        index.Upsert(3, "Hans Müller", 30);

        index.Query("müller anna").Execute().Hits.ShouldHaveSingleItem().Key.ShouldBe(1);
        index.Query("MUELLER").Execute().Hits.Select(h => h.Key).ShouldBe([3L, 1L]);
        index.Query("anna").Execute().Hits.Select(h => h.Key).ShouldBe([2L, 1L]);
    }

    [Fact]
    public void One_word_in_the_compact_column_finds_exactly_what_the_two_arms_of_the_specification_find()
    {
        // §8.2 asks for (s_norm : "w") OR (s_compact : "w"); the compact column alone is the same set
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var index = catalog.Index<long, Chip>("recipes").MultiWord().Owned().Ensure();

        var words = File.ReadLines(Path.Combine(Testing.Repository.RootPath, "tests", "Nordmann.LUI.TestData", "wordlist", "german-words.sample.txt"))
            .Where(w => w.Length >= 3).ToList();
        var random = new Random(4);
        var titles = Enumerable.Range(0, 1_500)
            .Select(_ => string.Join(random.Next(3) == 0 ? "-" : " ", Enumerable.Range(0, random.Next(1, 4)).Select(_ => words[random.Next(words.Count)])))
            .ToList();

        using (var connection = catalog.OpenConnection())
        {
            using var transaction = connection.BeginTransaction();
            for (var i = 0; i < titles.Count; i++)
            {
                index.Upsert(i + 1, titles[i], i, transaction: transaction);
            }

            transaction.Commit();
        }

        using var raw = database.Connect();
        var checkedQueries = 0;
        for (var i = 0; i < 400; i++)
        {
            var title = titles[random.Next(titles.Count)].Replace(' ', '-');
            var length = Math.Min(title.Length, random.Next(3, 9));
            var piece = title.Substring(random.Next(title.Length - length + 1), length).Replace("-", string.Empty, StringComparison.Ordinal);
            var query = Querying.PreparedQuery.Create(piece, NormalizationOptions.Default, IndexKind.MultiWord);
            if (query.IsPrefixPath || query.Words.Count != 1 || !string.Equals(query.Words[0], query.Compact, StringComparison.Ordinal))
            {
                continue;
            }

            var quoted = Querying.PreparedQuery.Quote(query.Compact);
            var specification = Match(raw, $"(s_norm : {quoted}) OR (s_compact : {quoted})");
            var ours = Match(raw, query.Expression);

            ours.ShouldBe(specification, $"for '{piece}'");
            checkedQueries++;
        }

        checkedQueries.ShouldBeGreaterThan(150);
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
        var index = catalog.Index<long, Chip>("recipes").MultiWord().Owned().Ensure();
        index.Upsert(1, "Apfelkuchen", 900);
        index.Upsert(2, "Gedeckter Apfelkuchen", 800);
        index.Upsert(3, "Apfel Kuchen", 700);
        index.Upsert(4, "Apfelkuchen mit Sahne", 400);
        index.Upsert(5, "Bratapfelkuchen", 9_000);
        index.Upsert(6, "Apfelstrudel", 5_000);
        index.Upsert(7, "Apfelkuchen mit Äpfeln", 300);
        index.Upsert(8, "Hauptstraße", 100);
        return new Fixture(index);
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

    private sealed class Fixture(ILuiIndex<long, Chip> index) : IDisposable
    {
        internal ILuiIndex<long, Chip> Index => index;

        public void Dispose()
        {
        }
    }
}
