using Shouldly;

using Nordmann.LUI.Testing;

namespace Nordmann.LUI.Architecture.Tests;

/// <summary>
/// The repository's build wiring is part of the contract: one place for
/// versions, two packages released together — the core and the hosting
/// (ADR-0018) —, no project that reaches into a project it should not know.
/// </summary>
public sealed class RepositoryLayoutTests
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
            "Package versions live in Directory.Packages.props only.");
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
    public void Every_Central_PackageVersion_is_referenced_somewhere()
    {
        // The other direction of the rule above. A pinned version that no
        // project references is a version nobody uses — and nobody notices,
        // because it looks like a dependency. Alba was pinned for a test
        // framework that was never taken; the pin survived the test.
        //
        // There is no exception any more. The three abstractions that ADR-0013
        // kept on purpose — "they come back with the dependency injection
        // (PR-008)" — are pinned in Directory.Packages.props: the hosting
        // package references the one it needs (ADR-0034), and the tests of the
        // host reference the other two (ADR-0018).
        var central = Repository
            .Elements(Repository.LoadXml(Path.Combine(Repository.RootPath, "Directory.Packages.props")), "PackageVersion")
            .Select(version => version.Attribute("Include")?.Value)
            .Where(name => name is not null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var referenced = Repository
            .ProjectFiles()
            .SelectMany(file => Repository.Elements(Repository.LoadXml(file), "PackageReference")
                .Select(reference => reference.Attribute("Include")?.Value)
                .Where(name => name is not null))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var unused = central
            .Where(name => !referenced.Contains(name))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        unused.ShouldBeEmpty(
            "Every pinned version is used by a project. A pin nobody references is a dependency that does not exist.");
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
            "The sample, the benchmarks and the tests are not shipped (ADR-0001, ADR-0018: the core and the hosting).");
    }

    [Fact]
    public void The_two_packages_are_the_core_and_the_hosting_and_share_one_version()
    {
        Repository
            .ProjectFiles()
            .Where(file => file.Contains($"{Path.DirectorySeparatorChar}src{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ShouldBe(["Nordmann.LUI.Hosting.csproj", "Nordmann.LUI.csproj"], ignoreOrder: true);

        // released together, with one version: it lives in Directory.Build.props, never in a project (ADR-0018)
        foreach (var project in new[] { LibraryProject(), HostingProject() })
        {
            Repository.Elements(Repository.LoadXml(project), "Version").ShouldBeEmpty(project);
            Repository.Elements(Repository.LoadXml(project), "VersionPrefix").ShouldBeEmpty(project);
            Repository.Elements(Repository.LoadXml(project), "VersionSuffix").ShouldBeEmpty(project);
            Repository.Elements(Repository.LoadXml(project), "PackageVersion").ShouldBeEmpty(project);
        }

        Repository
            .Elements(Repository.LoadXml(Path.Combine(Repository.RootPath, "Directory.Build.props")), "VersionPrefix")
            .ShouldHaveSingleItem();
    }

    [Fact]
    public void The_hosting_references_the_core_and_nothing_else_of_the_repository()
    {
        Repository
            .Elements(Repository.LoadXml(HostingProject()), "ProjectReference")
            .Select(reference => reference.Attribute("Include")?.Value)
            .ShouldBe(["../Nordmann.LUI/Nordmann.LUI.csproj"]);
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
        foreach (var project in new[] { LibraryProject(), HostingProject() })
        {
            Repository
                .Elements(Repository.LoadXml(project), "PackageReadmeFile")
                .Select(element => element.Value)
                .ShouldBe(["README.md"], project);
        }

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
            "Meziantou.Analyzer",
            "Microsoft.Data.Sqlite",
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
        // What a consumer gets when they install the core: exactly these (ADR-0004). The
        // Microsoft.Extensions abstractions belong to the hosting package (ADR-0018): a console application without a
        // host does not carry them. Build-time packages carry PrivateAssets="all" and stay out.
        var runtime = Repository
            .Elements(Repository.LoadXml(LibraryProject()), "PackageReference")
            .Where(reference => reference.Attribute("PrivateAssets") is null)
            .Select(reference => reference.Attribute("Include")?.Value)
            .Where(name => name is not null)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        runtime.ShouldBe(
        [
            "Microsoft.Data.Sqlite",
            "symspell"
        ]);
    }

    [Fact]
    public void The_hosting_ships_the_core_the_dependency_injection_and_the_abstractions_of_the_host()
    {
        // ADR-0034: the dependency set of a package is its contract — a package carries what it uses. The warm-up is a
        // hosted service (ADR-0043), and a hosted service is a type of the host: the abstraction of the host is in the
        // list again, because the package uses it now. The warm-up tells the logger of the host what failed (ADR-0045):
        // the abstraction of the logging was in the closure already, and is named now because the package uses it.
        // The rule did not change — the list did.
        var runtime = Repository
            .Elements(Repository.LoadXml(HostingProject()), "PackageReference")
            .Where(reference => reference.Attribute("PrivateAssets") is null)
            .Select(reference => reference.Attribute("Include")?.Value)
            .Where(name => name is not null)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        runtime.ShouldBe(
        [
            "Microsoft.Extensions.DependencyInjection.Abstractions",
            "Microsoft.Extensions.Hosting.Abstractions",
            "Microsoft.Extensions.Logging.Abstractions"
        ]);
    }

    [Fact]
    public void The_core_assembly_knows_no_microsoft_extensions()
    {
        // what the compiler really bound, not only what the project lists
        System.Reflection.Assembly.Load("Nordmann.LUI")
            .GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .Where(name => name.StartsWith("Microsoft.Extensions", StringComparison.Ordinal))
            .ShouldBeEmpty("The core does not depend on the dependency injection (ADR-0018).");
    }

    private static string LibraryProject() =>
        Path.Combine(Repository.LibraryDirectory, "Nordmann.LUI.csproj");

    private static string HostingProject() =>
        Path.Combine(Repository.HostingDirectory, "Nordmann.LUI.Hosting.csproj");

    private static bool IsPackable(string projectFile)
    {
        var isPackable = Repository
            .Elements(Repository.LoadXml(projectFile), "IsPackable")
            .Select(element => element.Value)
            .FirstOrDefault();

        return !string.Equals(isPackable, "false", StringComparison.OrdinalIgnoreCase);
    }
}
