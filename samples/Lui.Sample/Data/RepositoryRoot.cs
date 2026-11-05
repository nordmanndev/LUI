namespace Nordmann.LUI.Sample.Data;

/// <summary>
/// Where the repository is on disk: the sample finds the word list in it, the way the tools of the repository put it there.
/// The root is the directory that holds <c>Directory.Build.props</c> and <c>src/</c> — the same rule the tests use.
/// </summary>
internal static class RepositoryRoot
{
    private static readonly Lazy<string?> Root = new(Find);

    /// <summary>The root of the repository, or <see langword="null"/> when the sample runs outside of it (published, copied).</summary>
    internal static string? Location => Root.Value;

    private static string? Find()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            if (File.Exists(System.IO.Path.Combine(current.FullName, "Directory.Build.props"))
                && Directory.Exists(System.IO.Path.Combine(current.FullName, "src")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return null;
    }
}
