using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Nordmann.LUI.Normalization;
using Nordmann.LUI.Schema;
using Shouldly;

namespace Nordmann.LUI.Integration.Tests.Querying;

/// <summary>
/// The prefix path (one or two characters) reads only the rowid of its candidates (ADR-0028, issue #173): the text of the
/// 500 candidates is never needed — the ranking is the weight alone — and the hits take it from the statement that reads
/// them anyway. The answer is what it was.
/// </summary>
public sealed class PrefixPathTests
{
    private static readonly string[] Prefixes = ["a", "ä", "b", "k", "ka", "ap", "s", "st", "ü", "x", "ß", "-", "ab", "zu", "é"];

    [Fact]
    public void The_candidate_statement_of_the_prefix_path_of_an_embedded_index_reads_no_text()
    {
        foreach (var length in new[] { 1, 2 })
        {
            var sql = Source(LuiIndexKind.SingleWord).PrefixSql(length);

            Regex.IsMatch(sql, @"SELECT[^;]*\btext\b[^;]*\bFROM\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))
                .ShouldBeFalse($"the prefix path reads the rowid and nothing else: {sql}");
        }
    }

    [Fact]
    public void The_candidate_statement_of_the_search_index_still_reads_the_text()
    {
        Source(LuiIndexKind.SingleWord).CandidateSql.ShouldContain("c.text", customMessage: "the FTS5 path is unchanged");
        Source(LuiIndexKind.MultiWord).CandidateSql.ShouldContain("c.text");
    }

    [Theory]
    [InlineData(LuiIndexKind.SingleWord)]
    [InlineData(LuiIndexKind.MultiWord)]
    public void The_prefix_path_answers_what_it_answered_before(LuiIndexKind kind)
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var index = catalog.Create("words", builder => kind == LuiIndexKind.SingleWord ? builder.SingleWord() : builder.MultiWord());
        index.UpsertMany(Entries(kind));

        var lines = new List<string>();
        foreach (var prefix in Prefixes)
        {
            var result = index.Search(prefix);
            var sb = new StringBuilder().Append(CultureInfo.InvariantCulture, $"{prefix}: count={result.CandidatesConsidered} truncated={result.Truncated} corrected={result.CorrectedQuery ?? "-"}");
            foreach (var hit in result.Hits)
            {
                sb.Append(CultureInfo.InvariantCulture, $" | {hit.Key}={hit.Text} w{hit.Weight} p{(int)hit.Priority} s{hit.AtWordStart} {hit.Similarity:F4}");
            }

            lines.Add(sb.ToString());
        }

        var path = Environment.GetEnvironmentVariable("LUI_GOLDEN_OUT");
        if (path is not null)
        {
            File.AppendAllText(path, $"## {kind}\n{string.Join('\n', lines)}\n");
            return;
        }

        lines.ShouldBe(Golden.For(kind));
    }

    /// <summary>The statements of the index <c>tags</c>, as the query path gets them from its content source.</summary>
    private static EmbeddedSource Source(LuiIndexKind kind) =>
        new("tags", new IndexDefinition(kind, EmbeddedSource.SourceName, "long", GermanTransliteration: true, TokenizerFold.Version));

    private static List<LuiEntry> Entries(LuiIndexKind kind)
    {
        string[] words =
        [
            "apfel", "apfelkuchen", "Apfel-Zimt-Kuchen", "ananas", "Ärger", "ärztlich", "Übung", "über", "Überfall", "ß-Test", "straße",
            "strasse", "stein", "Stern", "stahl", "kartoffel", "Kartoffelsuppe", "kaffee", "kakao", "käse", "Käsekuchen", "kuchen",
            "bohne", "Birne", "banane", "brot", "zucker", "zucchini", "zimt", "zwiebel", "xylophon", "éclair", "École", "-bindestrich",
            " leerzeichen", "ab", "abc", "ap", "k", "s", "x", "a", "b", "zu", "Zug", "Zukunft", "Supermarkt", "suppe", "salz", "senf",
        ];

        var entries = new List<LuiEntry>();
        var key = 1;
        for (var round = 0; round < 12; round++)
        {
            foreach (var word in words)
            {
                var text = round == 0 ? word : $"{word} {round}";
                var priority = key % 11 == 0 ? LuiPriority.High : key % 13 == 0 ? LuiPriority.System : key % 5 == 0 ? LuiPriority.Elevated : LuiPriority.Normal;
                entries.Add(new LuiEntry(key, text) { Weight = (key * 7) % 53, Priority = priority });
                key++;
            }
        }

        return kind == LuiIndexKind.SingleWord ? [.. entries.Where(entry => !entry.Text.Contains(' ', StringComparison.Ordinal)).DistinctBy(entry => entry.Text, StringComparer.Ordinal)] : entries;
    }

    [Fact]
    public void A_source_whose_text_can_change_under_the_index_reads_the_text_on_the_prefix_path()
    {
        // the coupling of the two flags: a linked index checks the text against the table, so it needs it on every path
        var linked = typeof(LinkedSource);
        var source = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(linked);
        var changes = (bool)linked.GetProperty("TextCanChangeUnderTheIndex", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(source)!;
        var reads = (bool)linked.GetProperty("PrefixReadsText", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(source)!;

        changes.ShouldBeTrue();
        reads.ShouldBeTrue("a source that checks the text against the table reads it on the prefix path too");
    }

    [Fact]
    public void A_long_text_and_a_truncated_prefix_path_give_the_hit_its_own_text_and_word_start()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var index = catalog.Create("words", builder => builder.MultiWord());
        var longText = "Ärger " + string.Join(' ', Enumerable.Repeat("fragment", 40));
        var entries = Enumerable.Range(1, 60).Select(i => new LuiEntry(i, i == 1 ? longText : "ärger " + i.ToString(CultureInfo.InvariantCulture)) { Weight = i == 1 ? 1 : 100 + i }).ToList();
        index.UpsertMany(entries);

        var result = index.Search("ä", new LuiQueryOptions { Candidates = 10, MaxResults = 20 });

        result.Truncated.ShouldBeTrue("the candidates are fewer than the entries");
        result.Hits.ShouldAllBe(hit => hit.AtWordStart && hit.Text.Length > 0);
        foreach (var hit in result.Hits)
        {
            hit.Text.ShouldBe(entries.Single(entry => entry.Key.Equals(hit.Key)).Text, "the text of the hit is the stored one");
        }

        var longHit = index.Search("ä", new LuiQueryOptions { Candidates = 200, MaxResults = 60 }).Hits.Single(hit => hit.Text.Length > 100);
        longHit.Text.ShouldBe(longText);
        longHit.AtWordStart.ShouldBeTrue();
    }

    private static class Golden
    {
        internal static string[] For(LuiIndexKind kind) => kind == LuiIndexKind.SingleWord ? SingleWord : MultiWord;

        private static readonly string[] SingleWord =
        [
            "a: count=10 truncated=False corrected=- | 5=Ärger w35 p2 sTrue 0.7500 | 37=abc w47 p3 sTrue 0.8000 | 6=ärztlich w42 p3 sTrue 0.7333 | 36=ab w40 p3 sTrue 0.8500 | 42=a w29 p3 sTrue 1.0000 | 4=ananas w28 p3 sTrue 0.7500 | 3=Apfel-Zimt-Kuchen w21 p3 sTrue 0.6863 | 2=apfelkuchen w14 p3 sTrue 0.6970 | 1=apfel w7 p3 sTrue 0.7600 | 38=ap w1 p3 sTrue 0.8500",
            "ä: count=2 truncated=False corrected=- | 5=Ärger w35 p2 sTrue 0.8222 | 6=ärztlich w42 p3 sTrue 0.7926",
            "b: count=5 truncated=False corrected=- | 26=brot w23 p0 sTrue 0.7750 | 25=banane w16 p2 sTrue 0.7500 | 43=b w36 p3 sTrue 1.0000 | 24=Birne w9 p3 sTrue 0.7600 | 23=bohne w2 p3 sTrue 0.7600",
            "k: count=8 truncated=False corrected=- | 39=k w8 p0 sTrue 1.0000 | 22=kuchen w48 p1 sTrue 0.7500 | 20=käse w34 p2 sTrue 0.7600 | 21=Käsekuchen w41 p3 sTrue 0.6970 | 19=kakao w27 p3 sTrue 0.7600 | 18=kaffee w20 p3 sTrue 0.7500 | 17=Kartoffelsuppe w13 p3 sTrue 0.6905 | 16=kartoffel w6 p3 sTrue 0.7333",
            "ka: count=6 truncated=False corrected=- | 20=käse w34 p2 sTrue 0.8400 | 21=Käsekuchen w41 p3 sTrue 0.7769 | 19=kakao w27 p3 sTrue 0.8400 | 18=kaffee w20 p3 sTrue 0.8222 | 17=Kartoffelsuppe w13 p3 sTrue 0.7551 | 16=kartoffel w6 p3 sTrue 0.7926",
            "ap: count=4 truncated=False corrected=- | 3=Apfel-Zimt-Kuchen w21 p3 sTrue 0.7405 | 2=apfelkuchen w14 p3 sTrue 0.7769 | 1=apfel w7 p3 sTrue 0.8400 | 38=ap w1 p3 sTrue 1.0000",
            "s: count=11 truncated=False corrected=- | 13=stein w38 p0 sTrue 0.7600 | 11=straße w24 p1 sTrue 0.7429 | 15=stahl w52 p2 sTrue 0.7600 | 50=senf w32 p2 sTrue 0.7750 | 10=ß-Test w17 p2 sTrue 0.7429 | 40=s w15 p2 sTrue 1.0000 | 14=Stern w45 p3 sTrue 0.7600 | 12=strasse w31 p3 sTrue 0.7429 | 49=salz w25 p3 sTrue 0.7750 | 48=suppe w18 p3 sTrue 0.7600 | 47=Supermarkt w11 p3 sTrue 0.7000",
            "st: count=5 truncated=False corrected=- | 13=stein w38 p0 sTrue 0.8400 | 11=straße w24 p1 sTrue 0.8095 | 15=stahl w52 p2 sTrue 0.8400 | 14=Stern w45 p3 sTrue 0.8400 | 12=strasse w31 p3 sTrue 0.8095",
            "ü: count=3 truncated=False corrected=- | 7=Übung w49 p3 sTrue 0.8222 | 9=Überfall w10 p3 sTrue 0.7926 | 8=über w3 p3 sTrue 0.8400",
            "x: count=2 truncated=False corrected=- | 41=x w22 p3 sTrue 1.0000 | 31=xylophon w5 p3 sTrue 0.7375",
            "ß: count=1 truncated=False corrected=- | 10=ß-Test w17 p2 sTrue 0.8095",
            "-: count=1 truncated=False corrected=- | 34=-bindestrich w26 p3 sFalse 0.6944",
            "ab: count=2 truncated=False corrected=- | 37=abc w47 p3 sTrue 0.9111 | 36=ab w40 p3 sTrue 1.0000",
            "zu: count=5 truncated=False corrected=- | 44=zu w43 p1 sTrue 1.0000 | 45=Zug w50 p2 sTrue 0.9111 | 28=zucchini w37 p3 sTrue 0.8000 | 27=zucker w30 p3 sTrue 0.8222 | 46=Zukunft w4 p3 sTrue 0.8095",
            "é: count=0 truncated=False corrected=-",
        ];

        private static readonly string[] MultiWord =
        [
            "a: count=120 truncated=False corrected=- | 52=apfelkuchen 1 w46 p0 sTrue 0.0000 | 104=ananas 2 w39 p0 sTrue 0.0000 | 338=ap 6 w34 p0 sTrue 0.0000 | 156=ärztlich 3 w32 p0 sTrue 0.0000 | 442=a 8 w20 p0 sTrue 0.0000 | 351=apfel 7 w19 p0 sTrue 0.0000 | 403=Apfel-Zimt-Kuchen 8 w12 p0 sTrue 0.0000 | 455=Ärger 9 w5 p0 sTrue 0.0000 | 242=a 4 w51 p1 sTrue 0.0000 | 506=ärztlich 10 w44 p1 sTrue 0.0000 | 286=ab 5 w41 p1 sTrue 0.0000 | 187=abc 3 w37 p1 sTrue 0.0000 | 88=ap 1 w33 p1 sTrue 0.0000 | 451=apfel 9 w30 p1 sTrue 0.0000 | 352=apfelkuchen 7 w26 p1 sTrue 0.0000 | 253=Apfel-Zimt-Kuchen 5 w22 p1 sTrue 0.0000 | 154=ananas 3 w18 p1 sTrue 0.0000 | 55=Ärger 1 w14 p1 sTrue 0.0000 | 355=Ärger 7 w47 p2 sTrue 0.0000 | 105=Ärger 2 w46 p2 sTrue 0.0000",
            "ä: count=24 truncated=False corrected=- | 156=ärztlich 3 w32 p0 sTrue 0.1818 | 455=Ärger 9 w5 p0 sTrue 0.2857 | 506=ärztlich 10 w44 p1 sTrue 0.1667 | 55=Ärger 1 w14 p1 sTrue 0.2857 | 355=Ärger 7 w47 p2 sTrue 0.2857 | 105=Ärger 2 w46 p2 sTrue 0.2857 | 505=Ärger 10 w37 p2 sTrue 0.2500 | 255=Ärger 5 w36 p2 sTrue 0.2857 | 5=Ärger w35 p2 sTrue 0.4000 | 405=Ärger 8 w26 p2 sTrue 0.2857 | 155=Ärger 3 w25 p2 sTrue 0.2857 | 555=Ärger 11 w16 p2 sTrue 0.2500 | 305=Ärger 6 w15 p2 sTrue 0.2857 | 205=Ärger 4 w4 p2 sTrue 0.2857 | 256=ärztlich 5 w43 p3 sTrue 0.1818 | 6=ärztlich w42 p3 sTrue 0.2222 | 406=ärztlich 8 w33 p3 sTrue 0.1818 | 556=ärztlich 11 w23 p3 sTrue 0.1667 | 306=ärztlich 6 w22 p3 sTrue 0.1818 | 56=ärztlich 1 w21 p3 sTrue 0.1818",
            "b: count=72 truncated=False corrected=- | 325=banane 6 w49 p0 sTrue 0.0000 | 234=-bindestrich 4 w48 p0 sTrue 0.0000 | 26=brot w23 p0 sTrue 0.0000 | 273=bohne 5 w3 p0 sTrue 0.0000 | 484=-bindestrich 9 w49 p1 sTrue 0.0000 | 143=b 2 w47 p1 sTrue 0.0000 | 473=bohne 9 w25 p1 sTrue 0.0000 | 374=Birne 7 w21 p1 sTrue 0.0000 | 275=banane 5 w17 p1 sTrue 0.0000 | 176=brot 3 w13 p1 sTrue 0.0000 | 575=banane 11 w50 p2 sTrue 0.0000 | 75=banane 1 w48 p2 sTrue 0.0000 | 475=banane 9 w39 p2 sTrue 0.0000 | 225=banane 4 w38 p2 sTrue 0.0000 | 375=banane 7 w28 p2 sTrue 0.0000 | 125=banane 2 w27 p2 sTrue 0.0000 | 525=banane 10 w18 p2 sTrue 0.0000 | 25=banane w16 p2 sTrue 0.0000 | 425=banane 8 w7 p2 sTrue 0.0000 | 175=banane 3 w6 p2 sTrue 0.0000",
            "k: count=96 truncated=False corrected=- | 416=kartoffel 8 w50 p0 sTrue 0.0000 | 468=kaffee 9 w43 p0 sTrue 0.0000 | 520=käse 10 w36 p0 sTrue 0.0000 | 117=Kartoffelsuppe 2 w24 p0 sTrue 0.0000 | 169=kakao 3 w17 p0 sTrue 0.0000 | 221=Käsekuchen 4 w10 p0 sTrue 0.0000 | 39=k w8 p0 sTrue 1.0000 | 121=Käsekuchen 2 w52 p1 sTrue 0.0000 | 22=kuchen w48 p1 sTrue 0.0000 | 66=kartoffel 1 w38 p1 sTrue 0.0000 | 572=kuchen 11 w29 p1 sTrue 0.0000 | 517=Kartoffelsuppe 10 w15 p1 sTrue 0.0000 | 418=kaffee 8 w11 p1 sTrue 0.0000 | 539=k 10 w10 p1 sTrue 0.0000 | 319=kakao 6 w7 p1 sTrue 0.0000 | 220=käse 4 w3 p1 sTrue 0.0000 | 370=käse 7 w46 p2 sTrue 0.0000 | 120=käse 2 w45 p2 sTrue 0.0000 | 270=käse 5 w35 p2 sTrue 0.0000 | 20=käse w34 p2 sTrue 0.0000",
            "ka: count=72 truncated=False corrected=- | 416=kartoffel 8 w50 p0 sTrue 0.1818 | 468=kaffee 9 w43 p0 sTrue 0.2500 | 520=käse 10 w36 p0 sTrue 0.2500 | 117=Kartoffelsuppe 2 w24 p0 sTrue 0.1250 | 169=kakao 3 w17 p0 sTrue 0.3333 | 221=Käsekuchen 4 w10 p0 sTrue 0.1538 | 121=Käsekuchen 2 w52 p1 sTrue 0.1538 | 66=kartoffel 1 w38 p1 sTrue 0.1818 | 517=Kartoffelsuppe 10 w15 p1 sTrue 0.1176 | 418=kaffee 8 w11 p1 sTrue 0.2500 | 319=kakao 6 w7 p1 sTrue 0.3333 | 220=käse 4 w3 p1 sTrue 0.2857 | 370=käse 7 w46 p2 sTrue 0.2857 | 120=käse 2 w45 p2 sTrue 0.2857 | 270=käse 5 w35 p2 sTrue 0.2857 | 20=käse w34 p2 sTrue 0.4000 | 420=käse 8 w25 p2 sTrue 0.2857 | 170=käse 3 w24 p2 sTrue 0.2857 | 570=käse 11 w15 p2 sTrue 0.2500 | 320=käse 6 w14 p2 sTrue 0.2857",
            "ap: count=48 truncated=False corrected=- | 52=apfelkuchen 1 w46 p0 sTrue 0.1538 | 338=ap 6 w34 p0 sTrue 0.5000 | 351=apfel 7 w19 p0 sTrue 0.2857 | 403=Apfel-Zimt-Kuchen 8 w12 p0 sTrue 0.1053 | 88=ap 1 w33 p1 sTrue 0.5000 | 451=apfel 9 w30 p1 sTrue 0.2857 | 352=apfelkuchen 7 w26 p1 sTrue 0.1538 | 253=Apfel-Zimt-Kuchen 5 w22 p1 sTrue 0.1053 | 401=apfel 8 w51 p3 sTrue 0.2857 | 151=apfel 3 w50 p3 sTrue 0.2857 | 552=apfelkuchen 11 w48 p3 sTrue 0.1429 | 302=apfelkuchen 6 w47 p3 sTrue 0.1538 | 438=ap 8 w45 p3 sTrue 0.5000 | 188=ap 3 w44 p3 sTrue 0.5000 | 453=Apfel-Zimt-Kuchen 9 w44 p3 sTrue 0.1053 | 203=Apfel-Zimt-Kuchen 4 w43 p3 sTrue 0.1053 | 551=apfel 11 w41 p3 sTrue 0.2500 | 301=apfel 6 w40 p3 sTrue 0.2857 | 51=apfel 1 w39 p3 sTrue 0.2857 | 452=apfelkuchen 9 w37 p3 sTrue 0.1538",
            "s: count=132 truncated=False corrected=- | 598=suppe 11 w52 p0 sTrue 0.0000 | 13=stein w38 p0 sTrue 0.0000 | 247=Supermarkt 4 w33 p0 sTrue 0.0000 | 65=stahl 1 w31 p0 sTrue 0.0000 | 390=s 7 w27 p0 sTrue 0.0000 | 299=salz 5 w26 p0 sTrue 0.0000 | 260=ß-Test 5 w18 p0 sTrue 0.0000 | 312=strasse 6 w11 p0 sTrue 0.0000 | 364=Stern 7 w4 p0 sTrue 0.0000 | 363=stein 7 w50 p1 sTrue 0.0000 | 264=Stern 5 w46 p1 sTrue 0.0000 | 165=stahl 3 w42 p1 sTrue 0.0000 | 550=senf 10 w34 p1 sTrue 0.0000 | 110=ß-Test 2 w28 p1 sTrue 0.0000 | 11=straße w24 p1 sTrue 0.0000 | 297=Supermarkt 5 w12 p1 sTrue 0.0000 | 198=suppe 3 w8 p1 sTrue 0.0000 | 440=s 8 w6 p1 sTrue 0.0000 | 561=straße 11 w5 p1 sTrue 0.0000 | 99=salz 1 w4 p1 sTrue 0.0000",
            "st: count=60 truncated=False corrected=- | 13=stein w38 p0 sTrue 0.4000 | 65=stahl 1 w31 p0 sTrue 0.2857 | 312=strasse 6 w11 p0 sTrue 0.2222 | 364=Stern 7 w4 p0 sTrue 0.2857 | 363=stein 7 w50 p1 sTrue 0.2857 | 264=Stern 5 w46 p1 sTrue 0.2857 | 165=stahl 3 w42 p1 sTrue 0.2857 | 11=straße w24 p1 sTrue 0.2857 | 561=straße 11 w5 p1 sTrue 0.2000 | 462=strasse 9 w1 p1 sTrue 0.2222 | 15=stahl w52 p2 sTrue 0.4000 | 415=stahl 8 w43 p2 sTrue 0.2857 | 565=stahl 11 w33 p2 sTrue 0.2500 | 315=stahl 6 w32 p2 sTrue 0.2857 | 465=stahl 9 w22 p2 sTrue 0.2857 | 215=stahl 4 w21 p2 sTrue 0.2857 | 365=stahl 7 w11 p2 sTrue 0.2857 | 115=stahl 2 w10 p2 sTrue 0.2857 | 515=stahl 10 w1 p2 sTrue 0.2500 | 265=stahl 5 w0 p2 sTrue 0.2857",
            "ü: count=36 truncated=False corrected=- | 507=Übung 10 w51 p0 sTrue 0.2222 | 559=Überfall 11 w44 p0 sTrue 0.1667 | 208=über 4 w25 p0 sTrue 0.2857 | 407=Übung 8 w40 p1 sTrue 0.2500 | 308=über 6 w36 p1 sTrue 0.2857 | 209=Überfall 4 w32 p1 sTrue 0.1818 | 257=Übung 5 w50 p3 sTrue 0.2500 | 7=Übung w49 p3 sTrue 0.3333 | 408=über 8 w47 p3 sTrue 0.2857 | 158=über 3 w46 p3 sTrue 0.2857 | 309=Überfall 6 w43 p3 sTrue 0.1818 | 59=Überfall 1 w42 p3 sTrue 0.1818 | 157=Übung 3 w39 p3 sTrue 0.2500 | 558=über 11 w37 p3 sTrue 0.2500 | 58=über 1 w35 p3 sTrue 0.2857 | 459=Überfall 9 w33 p3 sTrue 0.1818 | 557=Übung 11 w30 p3 sTrue 0.2222 | 307=Übung 6 w29 p3 sTrue 0.2500 | 57=Übung 1 w28 p3 sTrue 0.2500 | 458=über 9 w26 p3 sTrue 0.2857",
            "x: count=24 truncated=False corrected=- | 481=xylophon 9 w28 p0 sTrue 0.0000 | 91=x 1 w1 p0 sTrue 0.0000 | 231=xylophon 4 w27 p1 sTrue 0.0000 | 341=x 6 w2 p1 sTrue 0.0000 | 431=xylophon 8 w49 p3 sTrue 0.0000 | 181=xylophon 3 w48 p3 sTrue 0.0000 | 491=x 9 w45 p3 sTrue 0.0000 | 241=x 4 w44 p3 sTrue 0.0000 | 581=xylophon 11 w39 p3 sTrue 0.0000 | 331=xylophon 6 w38 p3 sTrue 0.0000 | 81=xylophon 1 w37 p3 sTrue 0.0000 | 391=x 7 w34 p3 sTrue 0.0000 | 141=x 2 w33 p3 sTrue 0.0000 | 541=x 10 w24 p3 sTrue 0.0000 | 291=x 5 w23 p3 sTrue 0.0000 | 41=x w22 p3 sTrue 1.0000 | 381=xylophon 7 w17 p3 sTrue 0.0000 | 131=xylophon 2 w16 p3 sTrue 0.0000 | 441=x 8 w13 p3 sTrue 0.0000 | 191=x 3 w12 p3 sTrue 0.0000",
            "ß: count=12 truncated=False corrected=- | 260=ß-Test 5 w18 p0 sTrue 0.2222 | 110=ß-Test 2 w28 p1 sTrue 0.2222 | 560=ß-Test 11 w51 p2 sTrue 0.2000 | 310=ß-Test 6 w50 p2 sTrue 0.2222 | 60=ß-Test 1 w49 p2 sTrue 0.2222 | 460=ß-Test 9 w40 p2 sTrue 0.2222 | 210=ß-Test 4 w39 p2 sTrue 0.2222 | 360=ß-Test 7 w29 p2 sTrue 0.2222 | 510=ß-Test 10 w19 p2 sTrue 0.2000 | 10=ß-Test w17 p2 sTrue 0.2857 | 410=ß-Test 8 w8 p2 sTrue 0.2222 | 160=ß-Test 3 w7 p2 sTrue 0.2222",
            "-: count=0 truncated=False corrected=-",
            "ab: count=24 truncated=False corrected=- | 286=ab 5 w41 p1 sTrue 0.5000 | 187=abc 3 w37 p1 sTrue 0.4000 | 386=ab 7 w52 p3 sTrue 0.5000 | 136=ab 2 w51 p3 sTrue 0.5000 | 537=abc 10 w49 p3 sTrue 0.3333 | 287=abc 5 w48 p3 sTrue 0.4000 | 37=abc w47 p3 sTrue 0.6667 | 536=ab 10 w42 p3 sTrue 0.4000 | 36=ab w40 p3 sTrue 1.0000 | 437=abc 8 w38 p3 sTrue 0.4000 | 436=ab 8 w31 p3 sTrue 0.5000 | 186=ab 3 w30 p3 sTrue 0.5000 | 587=abc 11 w28 p3 sTrue 0.3333 | 337=abc 6 w27 p3 sTrue 0.4000 | 87=abc 1 w26 p3 sTrue 0.4000 | 586=ab 11 w21 p3 sTrue 0.4000 | 336=ab 6 w20 p3 sTrue 0.5000 | 86=ab 1 w19 p3 sTrue 0.5000 | 487=abc 9 w17 p3 sTrue 0.4000 | 237=abc 4 w16 p3 sTrue 0.4000",
            "zu: count=60 truncated=False corrected=- | 377=zucker 7 w42 p0 sTrue 0.2500 | 195=Zug 3 w40 p0 sTrue 0.4000 | 78=zucchini 1 w16 p0 sTrue 0.2000 | 494=zu 9 w13 p0 sTrue 0.5000 | 546=Zukunft 10 w6 p0 sTrue 0.2000 | 44=zu w43 p1 sTrue 1.0000 | 528=zucchini 10 w39 p1 sTrue 0.1818 | 594=zu 11 w24 p1 sTrue 0.4000 | 495=Zug 9 w20 p1 sTrue 0.4000 | 396=Zukunft 7 w16 p1 sTrue 0.2222 | 77=zucker 1 w9 p1 sTrue 0.2500 | 545=Zug 10 w52 p2 sTrue 0.3333 | 295=Zug 5 w51 p2 sTrue 0.4000 | 45=Zug w50 p2 sTrue 0.6667 | 445=Zug 8 w41 p2 sTrue 0.4000 | 595=Zug 11 w31 p2 sTrue 0.3333 | 345=Zug 6 w30 p2 sTrue 0.4000 | 95=Zug 1 w29 p2 sTrue 0.4000 | 245=Zug 4 w19 p2 sTrue 0.4000 | 395=Zug 7 w9 p2 sTrue 0.4000",
            "é: count=0 truncated=False corrected=-",
        ];
    }
}
