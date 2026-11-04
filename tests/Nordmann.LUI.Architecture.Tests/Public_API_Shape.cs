using System.Reflection;
using NetArchTest.Rules;
using Shouldly;

using Nordmann.LUI.Testing;

namespace Nordmann.LUI.Architecture.Tests;

/// <summary>
/// The shape of the public API is a promise: a library that talks to the web
/// stack or hands out <c>Task</c>s the caller never had to await is a library
/// that misleads its consumers (LUI-Spezifikation §13).
/// </summary>
public sealed class Public_API_Shape
{
    private static Assembly LibraryAssembly => Assembly.Load("Nordmann.LUI");

    /// <summary>Both packages: the core and the hosting (ADR-0018) — they share the rules of the public API.</summary>
    private static Assembly[] PackageAssemblies => [LibraryAssembly, Assembly.Load("Nordmann.LUI.Hosting")];

    [Fact]
    public void The_library_does_not_depend_on_the_web_stack()
    {
        var result = Types
            .InAssemblies(PackageAssemblies)
            .That()
            .ArePublic()
            .ShouldNot()
            .HaveDependencyOn("Microsoft.AspNetCore.App")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            $"LUI is a library, not a web app. Failing types: {Describe(result)}");
    }

    [Fact]
    public void The_public_api_is_synchronous()
    {
        // Microsoft.Data.Sqlite has no async I/O; a Task-returning API would be
        // a lie about latency (LUI-Spezifikation §13).
        var offenders = PackageAssemblies
            .SelectMany(assembly => assembly.GetExportedTypes())
            .SelectMany(type => type.GetMethods(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(method => typeof(Task).IsAssignableFrom(method.ReturnType))
            .Select(method => $"{method.DeclaringType?.Name}.{method.Name}")
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        offenders.ShouldBeEmpty("The public API of LUI is synchronous.");
    }

    [Fact]
    public void The_library_exposes_no_web_or_hosting_types()
    {
        var offenders = PackageAssemblies
            .SelectMany(assembly => assembly.GetExportedTypes())
            .Where(type => type.Namespace?.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal) == true
                        || type.Namespace?.StartsWith("Microsoft.Extensions.Hosting", StringComparison.Ordinal) == true)
            .Select(type => type.FullName!)
            .ToArray();

        offenders.ShouldBeEmpty("LUI does not leak hosting types into its public surface.");
    }

    [Fact]
    public void A_consumer_needs_one_using_and_every_public_type_says_it_is_lui()
    {
        // One namespace for the consumer (`using Nordmann.LUI;`), and a prefix that does not collide with the
        // types of an application — `Priority` would, in almost every one of them (ADR-0013).
        var offenders = PackageAssemblies
            .SelectMany(assembly => assembly.GetExportedTypes())
            .Where(type => !string.Equals(type.Namespace, "Nordmann.LUI", StringComparison.Ordinal)
                        || !(type.Name.StartsWith("Lui", StringComparison.Ordinal) || type.Name.StartsWith("ILui", StringComparison.Ordinal)))
            .Select(type => type.FullName!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        offenders.ShouldBeEmpty("Every public type lives in Nordmann.LUI and starts with Lui or ILui.");
    }

    [Fact]
    public void The_results_and_options_have_named_properties_not_positions()
    {
        // A positional record breaks every caller when a field is added — and two fields of the same type
        // that swap places do not even fail to compile (ADR-0013).
        string[] named =
        [
            "LuiHit`2", "LuiResult`2", "LuiTimings", "LuiQueryOptions", "LuiCatalogOptions",
            "LuiLinkedSyncReport", "LuiSyncOptions", "LuiSyncReport", "LuiIntegrityReport",
        ];

        var offenders = LibraryAssembly
            .GetExportedTypes()
            .Where(type => named.Contains(type.Name, StringComparer.Ordinal))
            .Where(type => type.GetConstructors().Any(constructor =>
                constructor.GetParameters().Length > 0
                && !(constructor.GetParameters().Length == 1 && constructor.GetParameters()[0].ParameterType == type)))
            .Select(type => type.Name)
            .ToArray();

        offenders.ShouldBeEmpty("The results and options are built with named properties.");
        LibraryAssembly.GetExportedTypes().Count(type => named.Contains(type.Name, StringComparer.Ordinal)).ShouldBe(named.Length);
    }

    [Fact]
    public void Every_exception_the_library_exposes_is_a_LuiException()
    {
        // One type for "something LUI says about an index or its environment", still an InvalidOperationException
        // (review of 2026-11-04, A.8). A fourth exception that forgets the base fails here.
        typeof(LuiException).IsAbstract.ShouldBeTrue("the library throws the three, never the base itself");
        typeof(InvalidOperationException).IsAssignableFrom(typeof(LuiException)).ShouldBeTrue();

        var exceptions = PackageAssemblies
            .SelectMany(assembly => assembly.GetExportedTypes())
            .Where(type => typeof(Exception).IsAssignableFrom(type))
            .ToArray();

        exceptions.Where(type => !typeof(LuiException).IsAssignableFrom(type)).Select(type => type.Name)
            .ShouldBeEmpty("Every public exception derives from LuiException.");
        exceptions.Select(type => type.Name).Order(StringComparer.Ordinal)
            .ShouldBe(["LuiDefinitionMismatchException", "LuiEnvironmentException", "LuiException", "LuiLinkedTableException"]);
    }

    [Fact]
    public void Every_option_of_a_query_can_be_set_on_the_query_and_every_setting_of_the_query_is_an_option()
    {
        // The settings of a query exist twice, as the properties of LuiQueryOptions and as the methods of LuiQuery. Nothing
        // but this test says that the two sets are the same (review of 2026-11-04, B.2). The one name that differs is Take.
        const BindingFlags declared = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        var renamed = new Dictionary<string, string>(StringComparer.Ordinal) { ["MaxResults"] = "Take" };

        var options = typeof(LuiQueryOptions).GetProperties(declared)
            .Select(property => renamed.GetValueOrDefault(property.Name, property.Name))
            .Order(StringComparer.Ordinal)
            .ToArray();
        var settings = typeof(LuiQuery<,>).GetMethods(declared)
            .Where(method => method.ReturnType.IsGenericType && method.ReturnType.GetGenericTypeDefinition() == typeof(LuiQuery<,>))
            .Select(method => method.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        options.ShouldNotBeEmpty();
        settings.ShouldBe(options, "LuiQuery has a method for every property of LuiQueryOptions, and no method without one.");
    }

    [Fact]
    public void The_read_interface_only_reads_and_the_write_interfaces_carry_the_maintenance()
    {
        // What keeps an index in order is not part of the interface that a suggest endpoint gets injected (review of
        // 2026-11-04, B.3). TypoTolerance and TypoDictionaryState stay: the hosting package sees the public API only.
        const BindingFlags declared = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        static string[] Members(Type type) =>
        [
            .. type.GetProperties(declared).Select(property => property.Name),
            .. type.GetMethods(declared).Where(method => !method.IsSpecialName).Select(method => method.Name),
        ];

        Members(typeof(ILuiIndex<,>)).Order(StringComparer.Ordinal)
            .ShouldBe(["Count", "Kind", "Name", "Query", "Search", "TypoDictionaryState", "TypoTolerance"]);

        foreach (var write in new[] { typeof(ILuiEmbeddedIndex<,>), typeof(ILuiLinkedIndex<>) })
        {
            foreach (var maintenance in new[] { "Check", "WarmUpTypoDictionary", "RebuildTypoDictionary" })
            {
                Members(write).ShouldContain(name => string.Equals(name, maintenance, StringComparison.Ordinal), $"{write.Name}.{maintenance}");
            }
        }
    }

    [Fact]
    public void The_content_sources_are_called_Embedded_and_Linked_and_the_old_word_is_gone_from_the_public_surface()
    {
        // In .NET "owned" is the vocabulary of lifetime (Autofac's Owned<T>, an owned IDisposable); the index of LUI's own
        // table is a singleton that is never disposed. The content source is called Embedded (ADR-0023, issue #41).
        var types = PackageAssemblies.SelectMany(assembly => assembly.GetExportedTypes()).ToArray();

        var offenders = types
            .SelectMany(SurfaceNames)
            .Where(name => name.Contains("Owned", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        offenders.ShouldBeEmpty("No public type, member or parameter says 'Owned' — the content source is 'Embedded'.");
        types.ShouldContain(type => string.Equals(type.Name, "ILuiEmbeddedIndex`2", StringComparison.Ordinal), "the guard sees the renamed type");
    }

    [Fact]
    public void The_guard_of_the_vocabulary_reads_the_public_surface_and_only_that()
    {
        // Review of pull request 46: the first version missed the parameters of a public constructor and tripped over a
        // private property. A guard that reads the wrong surface is worse than none — it feigns a promise.
        var names = new[] { typeof(PublicSurfaceProbe), typeof(PrivateSurfaceProbe) }.SelectMany(SurfaceNames).ToArray();

        // public: the type, a constructor parameter, a method and its parameter, a property, a protected member, a field
        names.ShouldContain(name => name.EndsWith("PublicSurfaceProbe..ctor(owned)", StringComparison.Ordinal), "a public constructor parameter");
        names.ShouldContain(name => name.EndsWith("PublicSurfaceProbe.Ask(owned)", StringComparison.Ordinal), "a public method parameter");
        names.ShouldContain(name => name.EndsWith("PublicSurfaceProbe.OwnedProperty", StringComparison.Ordinal), "a public property");
        names.ShouldContain(name => name.EndsWith("PublicSurfaceProbe.OwnedProtected", StringComparison.Ordinal), "a protected property of an open class");
        names.ShouldContain(name => name.EndsWith("PublicSurfaceProbe.OwnedField", StringComparison.Ordinal), "a public field");

        // not public: nothing of the private members, however they are named
        names.ShouldNotContain(name => name.Contains("OwnedCache", StringComparison.Ordinal), "a private property");
        names.ShouldNotContain(name => name.Contains("OwnedSecret", StringComparison.Ordinal), "a private method");
        names.ShouldNotContain(name => name.Contains("hidden", StringComparison.Ordinal), "the parameter of a private constructor");
        names.ShouldNotContain(name => name.Contains("OwnedInternal", StringComparison.Ordinal), "an internal member");
    }

    /// <summary>
    /// The names a consumer can see on a public type: the type, its public and protected members, and the parameters of its
    /// public and protected methods and constructors. A protected member of a sealed class is not visible, but the C#
    /// compiler does not let one be declared there either.
    /// </summary>
    private static IEnumerable<string> SurfaceNames(Type type)
    {
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        yield return type.FullName!;

        foreach (var method in type.GetMethods(all).Cast<MethodBase>().Concat(type.GetConstructors(all)).Where(Visible))
        {
            yield return $"{type.FullName}.{method.Name}";
            foreach (var parameter in method.GetParameters())
            {
                yield return $"{type.FullName}.{method.Name}({parameter.Name})";
            }
        }

        foreach (var property in type.GetProperties(all).Where(property => Visible(property.GetMethod) || Visible(property.SetMethod)))
        {
            yield return $"{type.FullName}.{property.Name}";
        }

        foreach (var field in type.GetFields(all).Where(field => field.IsPublic || field.IsFamily || field.IsFamilyOrAssembly))
        {
            yield return $"{type.FullName}.{field.Name}";
        }

        foreach (var @event in type.GetEvents(all).Where(@event => Visible(@event.AddMethod) || Visible(@event.RemoveMethod)))
        {
            yield return $"{type.FullName}.{@event.Name}";
        }
    }

    /// <summary>Whether a method (or an accessor) can be reached from outside the assembly: public, protected, or both.</summary>
    private static bool Visible(MethodBase? method) => method is { IsPublic: true } or { IsFamily: true } or { IsFamilyOrAssembly: true };

#pragma warning disable CA1051, CA1822, CA1823, CA1812, CS0169, CS0414, IDE0051, IDE0052, IDE0060, S1144, S4487, S1450
    /// <summary>A type with a retired word in every place a consumer can see — for the guard of the guard.</summary>
    public class PublicSurfaceProbe
    {
        public int OwnedField;

        public PublicSurfaceProbe(string owned)
        {
        }

        public string OwnedProperty { get; set; } = string.Empty;

        protected string OwnedProtected { get; set; } = string.Empty;

        internal string OwnedInternal { get; set; } = string.Empty;

        public void Ask(string owned)
        {
        }
    }

    /// <summary>A type whose retired words are all out of sight.</summary>
    public sealed class PrivateSurfaceProbe
    {
        private PrivateSurfaceProbe(int hidden)
        {
        }

        private string OwnedCache { get; set; } = string.Empty;

        private void OwnedSecret(string hidden)
        {
        }
    }
#pragma warning restore CA1051, CA1822, CA1823, CA1812, CS0169, CS0414, IDE0051, IDE0052, IDE0060, S1144, S4487, S1450

    private static string Describe(TestResult result) =>
        result.FailingTypeNames is { Count: > 0 } names
            ? string.Join(", ", names)
            : "none";
}
