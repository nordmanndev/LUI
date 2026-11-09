using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Nordmann.LUI.Testing;
using Shouldly;

namespace Nordmann.LUI.Architecture.Tests;

/// <summary>
/// The rules of the sample's client (PR-011, ADR-0027) that must not depend on someone remembering them: the page is plain ES
/// modules separated by concern, there is one fetch boundary, no module writes HTML from a string, the page has no inline
/// script or style (its policy forbids them), and the vendored library is the file it says it is. The client has no build, so
/// these are checks of the *source*.
/// </summary>
public sealed partial class SampleClientTests
{
    private static readonly string JsRoot = Path.Combine(Repository.SampleWebRoot, "js");

    /// <summary>Which folder may import from which: the direction of the dependencies, as a table.</summary>
    private static readonly Dictionary<string, string[]> MayImport = new(StringComparer.Ordinal)
    {
        ["core"] = ["core"],
        ["net"] = ["core", "net"],
        ["suggest"] = ["core", "suggest"],
        ["scenarios"] = ["core", "suggest", "scenarios"],
        ["panels"] = ["core", "net", "suggest", "vendor", "panels"],
        ["."] = ["core", "net", "suggest", "scenarios", "panels"],
    };

    [GeneratedRegex(@"^\s*(?:import|export)\s[^'""]*?from\s+['""](?<path>[^'""]+)['""]|^\s*import\s+['""](?<path>[^'""]+)['""]", RegexOptions.Multiline | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 2000)]
    private static partial Regex ImportStatement();

    [GeneratedRegex(@"\bfetch\s*\(", RegexOptions.None, matchTimeoutMilliseconds: 2000)]
    private static partial Regex FetchCall();

    [GeneratedRegex(@"\.fetch\s*=(?!=)", RegexOptions.None, matchTimeoutMilliseconds: 2000)]
    private static partial Regex FetchAssignment();

    private static IEnumerable<string> OwnModules() =>
        Directory.EnumerateFiles(JsRoot, "*.js", SearchOption.AllDirectories)
            .Where(file => !Relative(file).StartsWith("vendor/", StringComparison.Ordinal))
            .OrderBy(file => file, StringComparer.Ordinal);

    private static string Relative(string file) => Path.GetRelativePath(JsRoot, file).Replace('\\', '/');

    /// <summary>The code of a module without its comments, so that a rule does not trip over a sentence about the thing it forbids.</summary>
    private static string Code(string file) =>
        MultilineComment().Replace(LineComment().Replace(File.ReadAllText(file), string.Empty), string.Empty);

    [GeneratedRegex(@"(?<![:'""`])//[^\n]*", RegexOptions.None, matchTimeoutMilliseconds: 2000)]
    private static partial Regex LineComment();

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline, matchTimeoutMilliseconds: 2000)]
    private static partial Regex MultilineComment();

    [Fact]
    public void The_client_exists_and_is_a_tree_of_modules_by_concern()
    {
        OwnModules().ShouldNotBeEmpty();
        File.Exists(Path.Combine(JsRoot, "main.js")).ShouldBeTrue("the composition root");

        var folders = OwnModules().Select(file => Relative(file).Split('/')[0]).Where(part => !part.EndsWith(".js", StringComparison.Ordinal)).Distinct(StringComparer.Ordinal);
        folders.ShouldBe(["core", "net", "panels", "scenarios", "suggest"], ignoreOrder: true);
    }

    [Fact]
    public void Fetch_is_called_in_net_and_nowhere_else()
    {
        foreach (var file in OwnModules())
        {
            var relative = Relative(file);
            var code = Code(file);
            var inNet = relative.StartsWith("net/", StringComparison.Ordinal);

            if (!inNet)
            {
                FetchCall().IsMatch(code).ShouldBeFalse($"{relative} calls fetch: the page has one fetch boundary, net/");
                FetchAssignment().IsMatch(code).ShouldBeFalse($"{relative} replaces fetch");
            }
        }
    }

    [Theory]
    [InlineData("innerHTML")]
    [InlineData("outerHTML")]
    [InlineData("insertAdjacentHTML")]
    [InlineData("document.write")]
    [InlineData("createContextualFragment")]
    [InlineData("DOMParser")]
    [InlineData("eval(")]
    [InlineData("new Function")]
    [InlineData("setAttribute('style'")]
    public void No_module_of_the_page_writes_HTML_or_code_from_a_string(string forbidden)
    {
        // flexdatalist writes the strings we give it with innerHTML: that is why every displayed property has an escaped
        // twin (scenarios, tested in client-tests). The page's own modules build DOM with text nodes (core/dom.js).
        foreach (var file in OwnModules())
        {
            Code(file).ShouldNotContain(forbidden, Case.Sensitive, $"{Relative(file)} uses {forbidden}");
        }
    }

    [Fact]
    public void Every_import_resolves_and_goes_in_the_direction_the_table_allows()
    {
        foreach (var file in OwnModules())
        {
            var from = Relative(file);
            var folder = from.Contains('/', StringComparison.Ordinal) ? from.Split('/')[0] : ".";

            foreach (Match import in ImportStatement().Matches(File.ReadAllText(file)))
            {
                var path = import.Groups["path"].Value;
                var target = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, path));
                File.Exists(target).ShouldBeTrue($"{from} imports {path}, which does not exist");

                var to = Relative(target);
                var targetFolder = to.Contains('/', StringComparison.Ordinal) ? to.Split('/')[0] : ".";
                MayImport[folder].Contains(targetFolder, StringComparer.Ordinal)
                    .ShouldBeTrue($"{from} imports {to}: {folder} may import {string.Join(", ", MayImport[folder])}");

                if (string.Equals(targetFolder, "vendor", StringComparison.Ordinal))
                {
                    from.ShouldBe("panels/scenario-view.js", "flexdatalist is used in one module: the one that mounts an input");
                }
            }
        }
    }

    [Fact]
    public void Every_module_is_used_by_another_or_is_the_root()
    {
        var imported = OwnModules()
            .SelectMany(file => ImportStatement().Matches(File.ReadAllText(file)).Select(import => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, import.Groups["path"].Value))))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var file in OwnModules().Where(file => !string.Equals(Relative(file), "main.js", StringComparison.Ordinal)))
        {
            imported.Contains(file).ShouldBeTrue($"{Relative(file)} is not imported by any module: dead code");
        }
    }

    [Fact]
    public void The_page_has_no_inline_script_style_or_event_handler_and_loads_nothing_from_elsewhere()
    {
        var html = File.ReadAllText(Path.Combine(Repository.SampleWebRoot, "index.html"));

        InlineScript().IsMatch(html).ShouldBeFalse("a <script> without src");
        html.ShouldNotContain("<style", Case.Insensitive);
        StyleAttribute().IsMatch(html).ShouldBeFalse("a style attribute");
        EventHandlerAttribute().IsMatch(html).ShouldBeFalse("an inline event handler");
        ExternalResource().IsMatch(html).ShouldBeFalse("a script or stylesheet from another origin");

        foreach (var css in Directory.EnumerateFiles(Path.Combine(Repository.SampleWebRoot, "css"), "*.css"))
        {
            var text = File.ReadAllText(css);
            text.ShouldNotContain("@import", Case.Sensitive, css);
            ExternalUrl().IsMatch(text).ShouldBeFalse($"{css} loads something from another origin");
        }
    }

    [GeneratedRegex(@"<script(?![^>]*\ssrc=)[^>]*>", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 2000)]
    private static partial Regex InlineScript();

    [GeneratedRegex(@"<[a-z][^>]*\sstyle\s*=", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 2000)]
    private static partial Regex StyleAttribute();

    [GeneratedRegex(@"<[a-z][^>]*\son[a-z]+\s*=", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 2000)]
    private static partial Regex EventHandlerAttribute();

    [GeneratedRegex(@"<(?:script|link)[^>]*(?:src|href)\s*=\s*[""']https?://", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 2000)]
    private static partial Regex ExternalResource();

    [GeneratedRegex(@"url\(\s*['""]?https?://", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 2000)]
    private static partial Regex ExternalUrl();

    [Fact]
    public void The_vendored_flexdatalist_is_the_file_the_provenance_says_it_is()
    {
        var vendor = Path.Combine(JsRoot, "vendor");
        var readme = File.ReadAllText(Path.Combine(vendor, "README.md"));
        var stated = Sha256InReadme().Match(readme).Groups["hash"].Value;
        stated.ShouldNotBeNullOrEmpty("the provenance names the SHA-256 of the file");

        // the bytes of the repository, not the bytes of the runner: a checkout with CRLF
        // changed the line endings, and the provenance names the file of the repository
        var actual = Convert.ToHexStringLower(SHA256.HashData(Repository.NormalizedBytes(Path.Combine(vendor, "flexdatalist.es.js"))));

        actual.ShouldBe(stated, "the vendored file is not the one the provenance describes — a changed library needs the provenance and the browser check redone");
        readme.ShouldContain("3.1.1");
        File.ReadAllText(Path.Combine(vendor, "flexdatalist.es.js")).ShouldContain("@license MIT");
    }

    [GeneratedRegex(@"SHA-256 \| `(?<hash>[0-9a-f]{64})`", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 2000)]
    private static partial Regex Sha256InReadme();

    [Fact]
    public void The_client_has_no_dependencies_and_no_build()
    {
        var package = File.ReadAllText(Path.Combine(Repository.SampleDirectory, "package.json"));

        package.ShouldNotContain("\"dependencies\"", Case.Sensitive);
        package.ShouldNotContain("\"devDependencies\"", Case.Sensitive);
        package.ShouldContain("\"private\": true");
        Directory.Exists(Path.Combine(Repository.SampleDirectory, "node_modules")).ShouldBeFalse();
        Directory.EnumerateFiles(Repository.SampleDirectory, "*.config.js", SearchOption.TopDirectoryOnly).ShouldBeEmpty("no bundler");
    }
}
