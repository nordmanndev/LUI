using System.Text;

namespace Nordmann.LUI.Sample.Data;

/// <summary>
/// The German word list — the corpus of the <c>words</c> index. It is not checked into the repository: the full list is
/// published under CC0 (public domain dedication), and <c>tools/fetch-wordlist.sh</c> downloads it into
/// <c>artifacts/wordlist/</c>. The sample downloads it on its own when it is asked to fill the index — one button, the whole
/// list (ADR-0029). The list is a file of one word per line, UTF-8, Unix line endings.
/// </summary>
internal sealed class GermanWordList
{
    /// <summary>The number of words of the list — the number the specification names, and the number the download checks.</summary>
    internal const long Size = 685_829;

    private const string EnvironmentVariable = "LUI_WORDLIST";
    private const string Downloaded = "artifacts/wordlist/german-words.txt";
    private const string SourceUrl = "https://raw.githubusercontent.com/enz/german-wordlist/master/words";

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

    /// <summary>The list on the machine, or <see langword="null"/> when it is not there.</summary>
    internal static GermanWordList? Find(string? configuredPath)
    {
        foreach (var candidate in Candidates(configuredPath))
        {
            if (candidate is not null && File.Exists(candidate))
            {
                return new GermanWordList(candidate, CountWords(candidate));
            }
        }

        return null;
    }

    /// <summary>
    /// Downloads the full list into the place the repository's tool puts it, and returns it. The number of words is
    /// checked: a list that is not the list is worse than no list.
    /// </summary>
    internal static async Task<GermanWordList> DownloadAsync(Action<string>? note, CancellationToken cancellationToken)
    {
        note?.Invoke("downloading the German word list …");

        var target = TargetPath();
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);

        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        using var response = await client.GetAsync(SourceUrl, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        using (var file = File.Create(target))
        {
            await response.Content.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
        }

        var count = CountWords(target);
        if (count != Size)
        {
            File.Delete(target);
            throw new InvalidOperationException($"The downloaded list has {count:N0} words, not {Size:N0} — the source changed.");
        }

        return new GermanWordList(target, count);
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
                word = word.TrimStart('\uFEFF');
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

    private static IEnumerable<string?> Candidates(string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            yield return configuredPath;
        }

        yield return Environment.GetEnvironmentVariable(EnvironmentVariable);

        var root = RepositoryRoot.Location;
        if (root is not null)
        {
            yield return System.IO.Path.Combine(root, Downloaded);
        }
    }

    private static string TargetPath()
    {
        var root = RepositoryRoot.Location
            ?? throw new InvalidOperationException(
                "There is no repository to download the word list into. Set LUI_WORDLIST to a list, or run tools/fetch-wordlist.sh.");

        return System.IO.Path.Combine(root, Downloaded);
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
