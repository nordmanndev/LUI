using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

using Shouldly;

using Nordmann.LUI.Testing;

namespace Nordmann.LUI.Unit.Tests.Corpora;

/// <summary>
/// The corpora of the repository (ADR-0059): the fixed files the measurements run on. The rules of the loader are the
/// rules of the files:
/// <list type="bullet">
/// <item>the header is a block of comment lines, ended by an empty line</item>
/// <item>a '#' inside an entry is data — a title that begins with '#' is a title</item>
/// <item>the entries are sorted by the hash of their text, so a prefix is a representative sample</item>
/// <item>the weight of an entry without a weight column is the derived weight of its text</item>
/// </list>
/// <para>
/// The corpora of the repository are read from the tree, not from a fixture: the files that the measurements run on are
/// the files these tests check. A test with a corpus of its own would prove that the loader can read a file it wrote
/// itself.
/// </para>
/// </summary>
public sealed class CorpusFileTests
{
    /// <summary>The floor of the issue #123: a corpus with fewer entries is no corpus.</summary>
    private const int Floor = 100_000;

    [Fact]
    public void The_repository_has_four_corpora_and_each_of_them_has_more_than_a_hundred_thousand_entries()
    {
        CorpusFile.Names.Count.ShouldBe(4, "the corpora of the repository: the words, the places, the films, the titles");

        foreach (var name in CorpusFile.Names)
        {
            var file = CorpusFile.Open(name, 1_000, 0);

            file.EntryCount.ShouldBeGreaterThan(Floor, $"the corpus '{name}' has {file.EntryCount} entries");
            file.Texts.Count.ShouldBe(1_000, $"the corpus '{name}' gives the prefix that was asked for");
        }
    }

    [Fact]
    public void The_header_of_a_corpus_names_its_source_its_licence_its_snapshot_and_its_kind()
    {
        foreach (var name in CorpusFile.Names)
        {
            var file = CorpusFile.Open(name, 10, 0);

            file.Source.ShouldNotBeNullOrWhiteSpace($"the corpus '{name}' names its source");
            file.Licence.ShouldNotBeNullOrWhiteSpace($"the corpus '{name}' names its licence");
            file.Snapshot.ShouldNotBeNullOrWhiteSpace($"the corpus '{name}' names its snapshot");
            file.Kind.ShouldBeOneOf(CorpusKind.SingleWord, CorpusKind.MultiWord);
        }

        CorpusFile.Open("words.txt", 10, 0).Kind.ShouldBe(CorpusKind.SingleWord);
        CorpusFile.Open("wiki-titles.tsv", 10, 0).Kind.ShouldBe(CorpusKind.MultiWord);
    }

    [Fact]
    public void The_corpus_of_a_file_without_a_weight_column_carries_the_derived_weight_of_its_text()
    {
        var file = CorpusFile.Open("words.txt", 50, 0);

        for (var i = 0; i < file.Texts.Count; i++)
        {
            file.Weights[i].ShouldBe(
                CorpusFile.DerivedWeight(file.Texts[i]),
                $"the entry '{file.Texts[i]}' carries the weight of its own text");
        }
    }

    [Fact]
    public void The_weight_of_a_place_is_its_population_and_not_the_derived_weight()
    {
        var file = CorpusFile.Open("places.tsv", 100_000, 0);

        // the derived weight never reaches 10,000 — a bigger weight is the population of the place
        file.Weights.Max().ShouldBeGreaterThan(10_000, "the places carry their population");
        file.Contexts.ShouldContain(context => context.Contains('/', StringComparison.Ordinal),
            "the places carry the country, the region and the district of the entry beside them");
    }

    [Fact]
    public void A_prefix_of_a_corpus_is_sorted_by_the_hash_of_its_text_and_not_by_its_text()
    {
        var file = CorpusFile.Open("words.txt", 500, 0);

        var hashes = file.Texts.Select(text => Hash(text)).ToArray();
        for (var i = 1; i < hashes.Length; i++)
        {
            hashes[i].ShouldBeGreaterThanOrEqualTo(hashes[i - 1], $"the entry '{file.Texts[i]}' follows its predecessor");
        }

        // the prefix of a hash-sorted file is spread over the space of the hashes — it is not
        // the alphabetically first words of the list
        file.Texts.ShouldNotBe(file.Texts.OrderBy(text => text, StringComparer.Ordinal).ToArray(),
            "the entries of the prefix are not the alphabetically first ones");
    }

    [Fact]
    public void The_film_corpus_carries_the_titles_that_begin_with_a_hash()
    {
        // the whole file: the titles that begin with a hash stand wherever the hash order puts them
        var file = CorpusFile.Open("movies.tsv", 652_001, 0);

        file.Texts.ShouldContain(text => text.StartsWith('#'), "a title that begins with '#' is a title");
    }

    [Fact]
    public void A_prefix_of_the_words_holds_words_beyond_the_letter_f()
    {
        // the bug of the old generator: at 200,000 entries the corpus held the letters a to f
        var file = CorpusFile.Open("words.txt", 200_000, 0);

        file.Texts.ShouldContain(
            text => text.Length > 0 && char.ToLowerInvariant(text[0]) > 'f',
            "the prefix of 200,000 words is not the letters a to f");
    }

    [Fact]
    public void The_derived_weight_follows_the_rule_of_the_specification()
    {
        CorpusFile.DerivedWeight("Lui").ShouldBeInRange(1, 10_000);

        // the rule in its exact form: 1 + floor(9999 · t³ / 2⁹⁶), t = 2³² − v
        foreach (var text in new[] { "Lui", "Nordmann", "a", string.Empty, "Grüße aus dem Norden" })
        {
            var value = BinaryPrimitives.ReadUInt32BigEndian(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
            var t = (1UL << 32) - value;
            var expected = 1 + (int)((UInt128)9999 * t * t * t >> 96);

            CorpusFile.DerivedWeight(text).ShouldBe(expected, $"the weight of '{text}'");
        }
    }

    [Fact]
    public void A_title_that_begins_with_a_hash_is_an_entry_and_not_a_comment()
    {
        var content = new MemoryStream();
        using (var gzip = new GZipStream(content, CompressionLevel.Optimal, leaveOpen: true))
        {
            var head = new[]
            {
                "# LUI corpus — a corpus of the test",
                "# format: text<TAB>weight",
                "# kind: single",
                "# source: the test",
                "# licence: the test",
                "# snapshot: 2026-11-12",
                "# entries: 3",
                "",
            };
            foreach (var line in head)
            {
                gzip.Write(Encoding.UTF8.GetBytes(line + "\n"));
            }

            gzip.Write(Encoding.UTF8.GetBytes("#Hash Movie\t42\n"));
            gzip.Write(Encoding.UTF8.GetBytes("Zwei Wörter\t7\n"));
            gzip.Write(Encoding.UTF8.GetBytes("einfach\t3\n"));
        }

        content.Position = 0;
        var file = CorpusFile.Read(content, "test-corpus", 10, 0);

        file.Texts.Count.ShouldBe(3, "the entry that begins with '#' is an entry");
        file.Texts[0].ShouldBe("#Hash Movie");
        file.Weights[0].ShouldBe(42);
        file.Texts[1].ShouldBe("Zwei Wörter");
    }

    [Fact]
    public void A_file_without_a_header_is_refused()
    {
        var content = new MemoryStream();
        using (var gzip = new GZipStream(content, CompressionLevel.Optimal, leaveOpen: true))
        {
            gzip.Write(Encoding.UTF8.GetBytes("einfach\t3\n"));
        }

        content.Position = 0;

        var error = Should.Throw<InvalidOperationException>(() => CorpusFile.Read(content, "test-corpus", 10, 0));
        error.Message.ShouldContain("header");
    }

    [Fact]
    public void A_file_whose_header_is_not_a_block_of_comments_is_refused()
    {
        var content = new MemoryStream();
        using (var gzip = new GZipStream(content, CompressionLevel.Optimal, leaveOpen: true))
        {
            gzip.Write(Encoding.UTF8.GetBytes("# LUI corpus — a corpus of the test\neinfach\t3\n"));
        }

        content.Position = 0;

        var error = Should.Throw<InvalidOperationException>(() => CorpusFile.Read(content, "test-corpus", 10, 0));
        error.Message.ShouldContain("block of comments");
    }

    [Fact]
    public void The_minimum_weight_keeps_the_entries_below_it_out()
    {
        var content = new MemoryStream();
        using (var gzip = new GZipStream(content, CompressionLevel.Optimal, leaveOpen: true))
        {
            var head = new[]
            {
                "# LUI corpus — a corpus of the test",
                "# format: text<TAB>weight",
                "# kind: single",
                "# source: the test",
                "# licence: the test",
                "# snapshot: 2026-11-12",
                "# entries: 3",
                "",
            };
            foreach (var line in head)
            {
                gzip.Write(Encoding.UTF8.GetBytes(line + "\n"));
            }

            gzip.Write(Encoding.UTF8.GetBytes("eins\t9\n"));
            gzip.Write(Encoding.UTF8.GetBytes("zwei\t4\n"));
            gzip.Write(Encoding.UTF8.GetBytes("drei\t1\n"));
        }

        content.Position = 0;
        var file = CorpusFile.Read(content, "test-corpus", 10, 5);

        file.Texts.ShouldBe(["eins"], "the entries below the minimum weight are not measured");
    }

    [Fact]
    public void A_corpus_that_is_not_in_the_repository_is_refused_with_its_path()
    {
        var error = Should.Throw<InvalidOperationException>(() => CorpusFile.Open("there-is-no-such-corpus.txt", 10, 0));

        error.Message.ShouldContain("there-is-no-such-corpus.txt");
    }

    /// <summary>The first four bytes of the SHA-256 of a text — the sort key of the corpora.</summary>
    private static uint Hash(string text) =>
        BinaryPrimitives.ReadUInt32BigEndian(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
