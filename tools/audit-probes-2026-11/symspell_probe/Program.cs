// Probe: the typo correction of LUI, reproduced with the repository's own parameters.
//
// src/Nordmann.LUI/Typos/TypoDictionary.cs
//   - the dictionary holds the BEGINNINGS of 3..10 code points of every word of every entry
//   - SymSpell: maxDictionaryEditDistance = 1, prefixLength = 7, countThreshold = 1
//   - the lookup takes the first 10 code points of the typed word and asks for Verbosity.Closest
//
// The corpus is the repository's own file, the first 200 000 entries of tests/TestData/corpora/words.txt.gz —
// the corpus of the CorrectionBenchmarks.
//
// The normalisation is simplified (lower case + the German transliteration): the four typed inputs are
// ASCII, so the keys of the lookups are the same ones the library looks up.

using System.Diagnostics;
using System.Text;


const string Corpus = "/workspace/LUI/tests/TestData/corpora/words.txt.gz";
const int Entries = 200_000;
const int LongestBeginning = 10;
const int MinimumWordLength = 3;
const int Distance = 1;

var symSpell = new SymSpell(initialCapacity: 1 << 16, maxDictionaryEditDistance: Distance, prefixLength: 7, countThreshold: 1);

var begun = Stopwatch.StartNew();
var words = new List<string>();
using (var stream = new System.IO.Compression.GZipStream(System.IO.File.OpenRead(Corpus), System.IO.Compression.CompressionMode.Decompress))
using (var reader = new StreamReader(stream, Encoding.UTF8))
{
    while (await reader.ReadLineAsync() is { } line && line.Length > 0) { }   // the header block
    while (await reader.ReadLineAsync() is { } line)
    {
        if (line.Length == 0) continue;
        var text = line.Split('\t')[0];
        words.Add(text);
        if (words.Count >= Entries) break;
    }
}

var stage = new SymSpell.SuggestionStage(1 << 16);
long beginnings = 0;
foreach (var text in words)
{
    var search = SearchForm(text);
    foreach (var word in Words(search))
    {
        var codePoints = 0;
        for (var i = 0; i < word.Length; i++)
        {
            if (char.IsLowSurrogate(word[i])) continue;
            if (++codePoints > LongestBeginning) break;
            if (codePoints < MinimumWordLength) continue;
            var end = i + 1 < word.Length && char.IsLowSurrogate(word[i + 1]) ? i + 2 : i + 1;
            symSpell.CreateDictionaryEntry(word[..end], 1L, stage);
            beginnings++;
        }
    }
}
symSpell.CommitStaged(stage);
begun.Stop();
Console.WriteLine($"{words.Count} entries, {beginnings} beginnings, the dictionary built in {begun.Elapsed.TotalSeconds:F1} s");

Console.WriteLine("\nThe lookup of the four inputs of the CorrectionBenchmarks (head = the first 10 code points, Closest, distance 1):");
foreach (var input in new[] { "kartofel", "bahmhof", "fusbal", "strasee", "apfelkuch", "bahnhof" })
{
    var head = input.Length > LongestBeginning ? input[..LongestBeginning] : input;
    var found = symSpell.Lookup(head, SymSpell.Verbosity.Closest, Distance);
    var rendered = found.Count == 0 ? "NOTHING" : string.Join(", ", found.Select(item => $"{item.term} (distance {item.distance})"));
    Console.WriteLine($"  '{input}' -> head '{head}': {rendered}");
}

Console.WriteLine("\nHow many words of the sample begin with the key of the lookup:");
foreach (var key in new[] { "bahnhof", "fusbal", "kartofe", "strasee", "apfelku" })
{
    var count = words.Count(text => text.ToLowerInvariant().StartsWith(key, StringComparison.Ordinal));
    Console.WriteLine($"  '{key}': {count}");
}

static string SearchForm(string text) => text.ToLowerInvariant()
    .Replace("ä", "ae").Replace("ö", "oe").Replace("ü", "ue").Replace("ß", "ss");

static IEnumerable<string> Words(string search)
{
    var start = 0;
    for (var i = 0; i <= search.Length; i++)
    {
        if (i == search.Length || search[i] == ' ' || search[i] == '-' || search[i] == '_' || search[i] == '/')
        {
            if (i > start) yield return search[start..i];
            start = i + 1;
        }
    }
}
