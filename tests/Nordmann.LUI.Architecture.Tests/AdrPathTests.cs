using System.Globalization;

using Shouldly;

using Nordmann.LUI.Testing;

namespace Nordmann.LUI.Architecture.Tests;

/// <summary>
/// The records of the decisions name the files they are about, in the list of the affected files. A
/// path that names a file that is not there any more is a broken pointer: the reader follows it and
/// finds nothing — and the files of the measurements moved twice, which left six such pointers
/// behind (issue #137).
/// <para>
/// The records before ADR-0049 keep the names they were written with: history stays as written, and
/// the table of that record is the key to them. The records from ADR-0049 on name the tree as it is
/// today, and every path of theirs has to be there.
/// </para>
/// </summary>
public sealed class AdrPathTests
{
    /// <summary>The papers of the work: the records of the decisions.</summary>
    private const string RecordsDirectory = ".agents/adr";

    /// <summary>
    /// The record that is the key to the old names (ADR-0049): the records before it name the files as
    /// they were, the records from it on name them as they are.
    /// </summary>
    private const int FirstCurrentRecord = 49;

    /// <summary>
    /// The extensions of the files of the repository. A token that ends with another one is a name —
    /// a class, a member, a version — and not the path of a file.
    /// </summary>
    private static readonly string[] Extensions =
        [".cs", ".csproj", ".md", ".sh", ".yml", ".yaml", ".txt", ".props", ".json", ".js", ".css", ".html", ".py", ".gz", ".sln"];

    [Fact]
    public void Every_path_of_a_record_names_a_file_that_exists()
    {
        var offenders = new List<string>();

        foreach (var record in CurrentRecords())
        {
            foreach (var path in PathsOfTheAffectedFiles(record))
            {
                if (!File.Exists(Path.Combine(Repository.RootPath, path.Replace('/', Path.DirectorySeparatorChar))))
                {
                    offenders.Add($"{Relative(record)}: {path}");
                }
            }
        }

        offenders.ShouldBeEmpty(
            "every path of a record names a file that exists — a path that points at nothing is a broken promise (issue #137).");
    }

    /// <summary>The records that name the tree as it is today (ADR-0049).</summary>
    private static IEnumerable<string> CurrentRecords()
    {
        var directory = Path.Combine(Repository.RootPath, RecordsDirectory.Replace('/', Path.DirectorySeparatorChar));

        return Directory
            .EnumerateFiles(directory, "*.md")
            .Where(file => Number(file) >= FirstCurrentRecord)
            .OrderBy(file => file, StringComparer.Ordinal);
    }

    /// <summary>The number of a record: the four digits its file name starts with.</summary>
    private static int Number(string file)
    {
        var name = Path.GetFileName(file);

        return name.Length >= 4 && int.TryParse(name[..4], NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            ? number
            : 0;
    }

    /// <summary>
    /// The paths of the list of the affected files of a record. The section runs from its marker to
    /// the next heading; a token is a path of the repository when it carries a folder and ends with the
    /// extension of a file of the repository. A path that the record marks as <c>**deleted**</c> is a
    /// path on purpose: the file is gone, and the record says so (ADR-0061).
    /// </summary>
    private static IEnumerable<string> PathsOfTheAffectedFiles(string record)
    {
        var text = Repository.NormalizedText(record);
        var start = text.IndexOf("**Affects:**", StringComparison.Ordinal);

        if (start < 0)
        {
            yield break;
        }

        var end = text.IndexOf("\n## ", start, StringComparison.Ordinal);
        var section = end < 0 ? text[start..] : text[start..end];

        foreach (var line in section.Split('\n'))
        {
            var spans = CodeSpans(line).ToArray();

            for (var index = 0; index < spans.Length; index++)
            {
                var span = spans[index];

                if (!IsPathOfTheRepository(span.Value))
                {
                    continue;
                }

                var tail = index + 1 < spans.Length
                    ? line[span.End..spans[index + 1].Start]
                    : line[span.End..];

                if (tail.Contains("**deleted**", StringComparison.Ordinal))
                {
                    continue;
                }

                yield return span.Value;
            }
        }
    }

    /// <summary>Whether a token is the path of a file of the repository.</summary>
    private static bool IsPathOfTheRepository(string token) =>
        token.Contains('/')
        && !token.Contains("://", StringComparison.Ordinal)
        && !token.Contains(' ')
        && Extensions.Contains(Path.GetExtension(token), StringComparer.OrdinalIgnoreCase);

    /// <summary>The texts between the backticks of a text — the names the records write in code style.</summary>
    private static IEnumerable<(string Value, int Start, int End)> CodeSpans(string text)
    {
        var start = -1;

        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] != '`')
            {
                continue;
            }

            if (start < 0)
            {
                start = index + 1;
                continue;
            }

            var span = text[start..index];

            if (span.Contains('\n'))
            {
                start = -1;
                continue;
            }

            yield return (span, start, index);
            start = -1;
        }
    }

    private static string Relative(string file) =>
        Path.GetRelativePath(Repository.RootPath, file).Replace('\\', '/');
}
