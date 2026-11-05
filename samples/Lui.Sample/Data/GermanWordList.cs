using System.Text;

namespace Nordmann.LUI.Sample.Data;

/// <summary>
/// The German word list — the corpus of the <c>words</c> index. It is not checked into the repository: the full list (685,829
/// words, CC0) is downloaded by <c>tools/fetch-wordlist.sh</c> into <c>artifacts/wordlist/</c>, and the repository tracks a
/// curated subset of it (7,071 words) for the tests. The sample looks for the full list first and falls back to the subset —
/// and says which one it found, because a sample that offers 7,071 words where 685,829 are available would be a lie by
/// omission (ADR-0029).
/// </summary>
internal sealed class GermanWordList
{
    private const string EnvironmentVariable = "LUI_WORDLIST";
    private const string Downloaded = "artifacts/wordlist/german-words.txt";
    private const string Curated = "tests/Nordmann.LUI.TestData/wordlist/german-words.sample.txt";

    private readonly long _count;

    private GermanWordList(string filePath, long count)
    {
        FilePath = filePath;
        _count = count;
    }

    /// <summary>Where the list was found.</summary>
    internal string FilePath { get; }

    /// <summary>How many words it holds.</summary>
    internal long Count => _count;

    /// <summary>What the index panel shows: the list and its size.</summary>
    internal string Origin => $"the German word list — {Count:N0} words ({DisplayPath()})";

    /// <summary>The path of the list, relative to the repository when it is inside it — the panel shows it.</summary>
    private string DisplayPath()
    {
        var root = RepositoryRoot.Location;

        if (root is not null && FilePath.StartsWith(root + System.IO.Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            return FilePath[(root.Length + 1)..];
        }

        return FilePath;
    }

    /// <summary>The list of the machine, or <see langword="null"/> when there is none.</summary>
    internal static GermanWordList? Find()
    {
        foreach (var candidate in Candidates())
        {
            if (candidate is not null && File.Exists(candidate))
            {
                return new GermanWordList(candidate, CountWords(candidate));
            }
        }

        return null;
    }

    /// <summary>The first <paramref name="count"/> words of the list, in the order of the file.</summary>
    internal IEnumerable<string> Words(int count)
    {
        var taken = 0;
        var first = true;

        foreach (var line in File.ReadLines(FilePath, Encoding.UTF8))
        {
            var word = line.TrimEnd('\r');
            if (first)
            {
                first = false;
                word = word.TrimStart('﻿');
            }

            if (word.Length == 0)
            {
                continue;
            }

            yield return word;

            taken++;
            if (taken >= count)
            {
                yield break;
            }
        }
    }

    private static IEnumerable<string?> Candidates()
    {
        yield return Environment.GetEnvironmentVariable(EnvironmentVariable);

        var root = RepositoryRoot.Location;
        if (root is not null)
        {
            yield return System.IO.Path.Combine(root, Downloaded);
            yield return System.IO.Path.Combine(root, Curated);
        }
    }

    private static long CountWords(string path)
    {
        var count = 0L;

        foreach (var line in File.ReadLines(path))
        {
            if (line.Trim().Length > 0)
            {
                count++;
            }
        }

        return count;
    }
}
