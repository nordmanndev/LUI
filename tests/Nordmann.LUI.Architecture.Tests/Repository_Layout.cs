using Shouldly;

using Nordmann.LUI.Testing;

namespace Nordmann.LUI.Architecture.Tests;

/// <summary>
/// The repository's build wiring is part of the contract: one place for
/// versions, one packable project, no project that reaches into a project it
/// should not know.
/// </summary>
public sealed class Repository_Layout
{
    [Fact]
    public void No_PackageReference_carries_a_Version()
    {
        var offenders = Repository
            .ProjectFiles()
            .SelectMany(file => (Repository.Elements(Repository.LoadXml(file), "PackageReference")
                .Where(reference => reference.Attribute("Version") is not null)
                .Select(reference => $"{Path.GetFileName(file)}: {reference.Attribute("Include")?.Value} Version={reference.Attribute("Version")?.Value}")))
            .ToArray();

        offenders.ShouldBeEmpty(
            "Package versions live in Directory.Packages.props only (standards: architecture/layering.md).");
    }

    [Fact]
    public void Every_PackageReference_has_a_Central_PackageVersion()
    {
        var central = Repository
            .Elements(Repository.LoadXml(Path.Combine(Repository.RootPath, "Directory.Packages.props")), "PackageVersion")
            .Select(version => version.Attribute("Include")?.Value)
            .Where(name => name is not null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var undeclared = Repository
            .ProjectFiles()
            .SelectMany(file => (Repository.Elements(Repository.LoadXml(file), "PackageReference")
                .Select(reference => reference.Attribute("Include")?.Value)
                .Where(name => name is not null && !central.Contains(name))
                .Select(name => $"{Path.GetFileName(file)}: {name}")))
            .ToArray();

        undeclared.ShouldBeEmpty(
            "Every referenced package needs an exact version in Directory.Packages.props.");
    }

    [Fact]
    public void Only_the_library_project_is_packable()
    {
        var packable = Repository
            .ProjectFiles()
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}src{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(file => IsPackable(file))
            .Select(Path.GetFileName)
            .ToArray();

        packable.ShouldBeEmpty(
            "The sample, the benchmarks and the tests are not shipped (ADR-0001: exactly one package).");
    }

    [Fact]
    public void The_library_project_references_no_other_project()
    {
        var references = Repository
            .Elements(Repository.LoadXml(LibraryProject()), "ProjectReference")
            .Select(reference => reference.Attribute("Include")?.Value)
            .ToArray();

        references.ShouldBeEmpty(
            "A library that references the sample or the tests would drag them into every consumer's build.");
    }

    [Fact]
    public void The_library_ships_the_readme_and_the_mit_license()
    {
        var libraryProject = Repository.LoadXml(Path.Combine(Repository.LibraryDirectory, "Nordmann.LUI.csproj"));

        Repository
            .Elements(libraryProject, "PackageReadmeFile")
            .Select(element => element.Value)
            .ShouldBe(["README.md"]);

        var license = Repository
            .Elements(Repository.LoadXml(Path.Combine(Repository.RootPath, "Directory.Build.props")), "PackageLicenseExpression")
            .Select(element => element.Value)
            .ShouldHaveSingleItem();

        license.ShouldBe("MIT");
    }

    [Fact]
    public void The_library_references_only_the_documented_dependencies()
    {
        // ADR-0004 fixes the dependency set. A new package is a decision,
        // not something that sneaks in through a using directive.
        var documented = new[]
        {
            "F23.StringSimilarity",
            "Meziantou.Analyzer",
            "Microsoft.Data.Sqlite",
            "Microsoft.Extensions.DependencyInjection.Abstractions",
            "Microsoft.Extensions.Hosting.Abstractions",
            "Microsoft.Extensions.Logging.Abstractions",
            "Microsoft.SourceLink.GitHub",
            "symspell"
        };

        var referenced = Repository
            .Elements(Repository.LoadXml(LibraryProject()), "PackageReference")
            .Select(reference => reference.Attribute("Include")?.Value)
            .Where(name => name is not null)
            .ToArray();

        var undocumented = referenced
            .Where(name => !documented.Contains(name, StringComparer.OrdinalIgnoreCase))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        undocumented.ShouldBeEmpty("Every package the library references has to be documented (ADR-0004).");
    }

    [Fact]
    public void The_library_ships_only_the_documented_runtime_dependencies()
    {
        // What a consumer gets when they install the package: exactly these (LUI-Spezifikation §17.3).
        // The Microsoft.Extensions abstractions come back with the dependency injection (PR-008), not before:
        // a console application without a host should not carry them (ADR-0013).
        // Build-time packages carry PrivateAssets="all" and stay out.
        var runtime = Repository
            .Elements(Repository.LoadXml(LibraryProject()), "PackageReference")
            .Where(reference => reference.Attribute("PrivateAssets") is null)
            .Select(reference => reference.Attribute("Include")?.Value)
            .Where(name => name is not null)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        runtime.ShouldBe(
        [
            "F23.StringSimilarity",
            "Microsoft.Data.Sqlite",
            "symspell"
        ]);
    }

    private static string LibraryProject() =>
        Path.Combine(Repository.LibraryDirectory, "Nordmann.LUI.csproj");

    private static bool IsPackable(string projectFile)
    {
        var isPackable = Repository
            .Elements(Repository.LoadXml(projectFile), "IsPackable")
            .Select(element => element.Value)
            .FirstOrDefault();

        return !string.Equals(isPackable, "false", StringComparison.OrdinalIgnoreCase);
    }
}
