using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace Nordmann.LUI.Testing;

/// <summary>
/// A corpus file of the repository (ADR-0059): the entries of a fixed file, their weights and their context.
/// <para>
/// The files are gzip-compressed and the loader reads them transparently. The <b>header</b> is the block of comment
/// lines at the top of the file, ended by an empty line — a <c>#</c> inside an entry is <b>data</b>, not a comment: a
/// movie title that begins with <c>#</c> is a title. A convention that eats real data is a silent filter, not a
/// convention.
/// </para>
/// <para>
/// The entries of a file are sorted by the hash of their text (the header says so), so a <b>prefix</b> of the file is
/// a representative sample and never "the alphabetically first N" — that was the bug of the old generator.
/// </para>
/// <para>
/// The formats are three: <c>text</c>, <c>text&lt;TAB&gt;weight</c> and
/// <c>text&lt;TAB&gt;weight&lt;TAB&gt;context</c>. The context is metadata and is not indexed — it is what lets a corpus
/// carry the country, the region and the district of an entry beside it. A file without a weight column gets the
/// derived weight of its text.
/// </para>
/// </summary>
public sealed class CorpusFile
{
    /// <summary>
    /// The formats a corpus file may name: the columns of an entry, in the order of the line. The header of a file names
    /// its format, and the reader **checks** it against the columns it finds — a self-description that nobody reads is a
    /// second truth beside the file (the same rule the reader of the scenario file follows, ADR-0058).
    /// </summary>
    private static readonly string[][] Formats =
    [
        ["text"],
        ["text", "weight"],
        ["text", "weight", "context"]
    ];

    /// <summary>The directory of the corpora in the repository.</summary>
    public static string Directory { get; } = Path.Combine(Repository.RootPath, "tests", "TestData", "corpora");

    /// <summary>
    /// The names of the corpora of the repository — the file name is the identity of a corpus, there is no manifest.
    /// </summary>
    public static IReadOnlyList<string> Names { get; } = ["words.txt", "places.tsv", "movies.tsv", "wiki-titles.tsv"];

    private CorpusFile(
        string name,
        int entryCount,
        CorpusKind kind,
        string source,
        string licence,
        string snapshot,
        IReadOnlyList<string> texts,
        IReadOnlyList<int> weights,
        IReadOnlyList<string> contexts)
    {
        Name = name;
        EntryCount = entryCount;
        Kind = kind;
        Source = source;
        Licence = licence;
        Snapshot = snapshot;
        Texts = texts;
        Weights = weights;
        Contexts = contexts;
    }

    /// <summary>The name of the corpus: the file name is its identity.</summary>
    public string Name { get; }

    /// <summary>The number of entries the file has — its header names it.</summary>
    public int EntryCount { get; }

    /// <summary>The kind of the entries: single words or titles.</summary>
    public CorpusKind Kind { get; }

    /// <summary>Where the entries come from — the header names it.</summary>
    public string Source { get; }

    /// <summary>The licence of the source — the header names it.</summary>
    public string Licence { get; }

    /// <summary>When the source was fetched — the header names it.</summary>
    public string Snapshot { get; }

    /// <summary>The texts of the prefix that was read.</summary>
    public IReadOnlyList<string> Texts { get; }

    /// <summary>The weights of the entries of the prefix.</summary>
    public IReadOnlyList<int> Weights { get; }

    /// <summary>The context of the entries of the prefix — metadata, not indexed.</summary>
    public IReadOnlyList<string> Contexts { get; }

    /// <summary>
    /// Opens a corpus file of the repository and reads a prefix of it.
    /// </summary>
    /// <param name="name">The name of the corpus — the file name, as the budget of a target names it.</param>
    /// <param name="prefixLength">How many entries of the file the run takes — a prefix is a representative sample.</param>
    /// <param name="minimumWeight">The smallest weight an entry may have; the entries below it are not measured.</param>
    /// <returns>The corpus file with its prefix.</returns>
    public static CorpusFile Open(string name, int prefixLength, int minimumWeight)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentOutOfRangeException.ThrowIfLessThan(prefixLength, 1);

        var path = Path.Combine(Directory, name + ".gz");
        if (!File.Exists(path))
        {
            throw new InvalidOperationException(
                $"The corpus file '{name}' is missing ({path}). The corpora are checked in (ADR-0059) — a corpus that is "
                + "fetched at the run is a corpus that differs from the one the budgets were measured on.");
        }

        using var stream = File.OpenRead(path);
        return Read(stream, name, prefixLength, minimumWeight);
    }

    /// <summary>
    /// Reads a corpus file out of a stream of gzip-compressed bytes. The stream is read to the end of the prefix; the
    /// rest of the file is left unread — a corpus of five million entries is read in slices, not in one piece.
    /// </summary>
    /// <param name="content">The compressed bytes of the file.</param>
    /// <param name="name">The name of the corpus — the file name is its identity.</param>
    /// <param name="prefixLength">How many entries of the file the run takes.</param>
    /// <param name="minimumWeight">The smallest weight an entry may have.</param>
    /// <returns>The corpus file with its prefix.</returns>
    public static CorpusFile Read(Stream content, string name, int prefixLength, int minimumWeight)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentOutOfRangeException.ThrowIfLessThan(prefixLength, 1);

        using var gzip = new GZipStream(content, CompressionMode.Decompress, leaveOpen: true);
        using var reader = new StreamReader(gzip, Encoding.UTF8);

        var header = ReadHeader(reader, name);
        var columns = Format(header, name);
        var texts = new List<string>(Math.Min(prefixLength, 1_000_000));
        var weights = new List<int>(texts.Capacity);
        var contexts = new List<string>(texts.Capacity);

        while (texts.Count < prefixLength && reader.ReadLine() is { } line)
        {
            if (line.Length == 0)
            {
                continue;
            }

            var entry = Entry(line, columns, name);
            if (entry.Weight < minimumWeight)
            {
                continue;
            }

            texts.Add(entry.Text);
            weights.Add(entry.Weight);
            contexts.Add(entry.Context);
        }

        var kind = header.GetValueOrDefault("kind") switch
        {
            "single" => CorpusKind.SingleWord,
            "multi" => CorpusKind.MultiWord,
            var other => throw new InvalidOperationException(
                $"The corpus file '{name}' names the kind '{other}', which is not known: a corpus holds single words or "
                + "titles.")
        };

        return new CorpusFile(
            name,
            Integer(header, "entries", name),
            kind,
            header.GetValueOrDefault("source", string.Empty),
            header.GetValueOrDefault("licence", string.Empty),
            header.GetValueOrDefault("snapshot", string.Empty),
            texts,
            weights,
            contexts);
    }

    /// <summary>
    /// The derived weight of a text: <c>1 + floor(9999 · (1 − u)³)</c>, with <c>u</c> the first four bytes of the
    /// SHA-256 of the text.
    /// <para>
    /// The rule is computed in <b>exact integer arithmetic</b>: with <c>v</c> the first four bytes of the hash as a
    /// whole number and <c>t = 2³² − v</c>, the weight is <c>1 + floor(9999 · t³ / 2⁹⁶)</c>. The same number comes out
    /// of the builder that wrote the file and out of this loader — a rule in floating point would be one off at the
    /// border.
    /// </para>
    /// </summary>
    /// <param name="text">The text of the entry.</param>
    /// <returns>The weight of the entry.</returns>
    public static int DerivedWeight(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        var value = BinaryPrimitives.ReadUInt32BigEndian(hash);
        var t = (1UL << 32) - value;
        var numerator = (UInt128)9999 * t * t * t;
        return 1 + (int)(numerator >> 96);
    }

    /// <summary>
    /// The header of the file: the block of comment lines at the top, ended by an empty line. A line that is not a
    /// comment before the end of the header is an error — the header of a corpus file is a block, not a convention.
    /// </summary>
    /// <param name="reader">The reader of the file.</param>
    /// <param name="name">The name of the corpus, for the error message.</param>
    /// <returns>The members of the header.</returns>
    private static Dictionary<string, string> ReadHeader(StreamReader reader, string name)
    {
        var header = new Dictionary<string, string>(StringComparer.Ordinal);
        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0)
            {
                // the end of the header: from here a '#' is an entry
                if (header.Count == 0)
                {
                    throw new InvalidOperationException(
                        $"The corpus file '{name}' has no header. A corpus file names its source, its licence, its "
                        + "number of entries and its format before its first entry.");
                }

                return header;
            }

            if (!line.StartsWith('#'))
            {
                throw new InvalidOperationException(
                    $"The header of the corpus file '{name}' is not a block of comments: the line '{line}' is neither a "
                    + "comment nor the end of the header.");
            }

            var text = line[1..].Trim();
            var colon = text.IndexOf(':', StringComparison.Ordinal);
            if (colon > 0)
            {
                header[text[..colon].Trim()] = text[(colon + 1)..].Trim();
            }
        }

        throw new InvalidOperationException(
            $"The corpus file '{name}' has no header — the file ends before the first entry.");
    }

    /// <summary>
    /// One entry of the file: its text, its weight and its context. The number of columns is checked against the format
    /// the header names — the header of a file is read, not believed.
    /// </summary>
    /// <param name="line">The line of the entry.</param>
    /// <param name="columns">The columns the header names.</param>
    /// <param name="name">The name of the corpus, for the error message.</param>
    /// <returns>The entry.</returns>
    private static (string Text, int Weight, string Context) Entry(string line, string[] columns, string name)
    {
        var parts = line.Split('\t');
        if (parts.Length != columns.Length)
        {
            throw new InvalidOperationException(
                $"The entry '{parts[0]}' of the corpus file '{name}' has {parts.Length} columns, but its header names "
                + $"{columns.Length} ({string.Join("<TAB>", columns)}) — the header of a file is read.");
        }

        var text = parts[0];
        var weight = parts.Length > 1 && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : DerivedWeight(text);
        var context = parts.Length > 2 ? string.Join('\t', parts[2..]) : string.Empty;

        return (text, weight, context);
    }

    /// <summary>
    /// The format of the file: the columns the header names, checked against the formats that exist. A header that names
    /// a format nobody knows is an error, not a shrug.
    /// </summary>
    /// <param name="header">The header of the file.</param>
    /// <param name="name">The name of the corpus, for the error message.</param>
    /// <returns>The columns of an entry, in order.</returns>
    private static string[] Format(Dictionary<string, string> header, string name)
    {
        if (!header.TryGetValue("format", out var format))
        {
            throw new InvalidOperationException(
                $"The header of the corpus file '{name}' does not name its format — the format is what the reader reads "
                + "the columns by.");
        }

        var columns = format.Split("<TAB>", StringSplitOptions.None);
        if (!Formats.Any(known => known.SequenceEqual(columns, StringComparer.Ordinal)))
        {
            throw new InvalidOperationException(
                $"The format '{format}' of the corpus file '{name}' is not one of "
                + $"{string.Join(", ", Formats.Select(known => string.Join("<TAB>", known)))}.");
        }

        return columns;
    }

    /// <summary>A member of the header that is a whole number.</summary>
    /// <param name="header">The header of the file.</param>
    /// <param name="name">The name of the member.</param>
    /// <param name="corpus">The name of the corpus, for the error message.</param>
    /// <returns>The number of the member.</returns>
    private static int Integer(Dictionary<string, string> header, string name, string corpus) =>
        header.TryGetValue(name, out var value)
            && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            ? number
            : throw new InvalidOperationException(
                $"The header of the corpus file '{corpus}' needs the member '{name}' as a whole number.");
}
