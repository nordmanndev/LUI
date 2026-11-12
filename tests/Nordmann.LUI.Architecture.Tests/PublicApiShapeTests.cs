using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using NetArchTest.Rules;
using TestResult = NetArchTest.Rules.TestResult;
using Shouldly;

using Nordmann.LUI.Testing;

namespace Nordmann.LUI.Architecture.Tests;

/// <summary>
/// The shape of the public API is a promise: a library that talks to the web
/// stack or hands out <c>Task</c>s the caller never had to await is a library
/// that misleads its consumers (LUI-Spezifikation §13).
/// </summary>
public sealed class PublicApiShapeTests
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
            "LuiHit", "LuiResult", "LuiTimings", "LuiQueryOptions", "LuiCatalogOptions",
            "LuiLinkedSyncReport", "LuiSyncOptions", "LuiSyncReport", "LuiIntegrityReport",
            "LuiTypedHit", "LuiTypedResult",
        ];

        // the name without the arity: the runtime appends it to a generic type, the list names the type
        var offenders = LibraryAssembly
            .GetExportedTypes()
            .Where(type => named.Contains(TypeNames.Of(type), StringComparer.Ordinal))
            .Where(type => type.GetConstructors().Any(constructor =>
                constructor.GetParameters().Length > 0
                && !(constructor.GetParameters().Length == 1 && constructor.GetParameters()[0].ParameterType == type)))
            .Select(type => type.Name)
            .ToArray();

        offenders.ShouldBeEmpty("The results and options are built with named properties.");
        LibraryAssembly.GetExportedTypes().Count(type => named.Contains(TypeNames.Of(type), StringComparer.Ordinal)).ShouldBe(named.Length);
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
            .ShouldBe(
            [
                "LuiDefinitionMismatchException", "LuiEnvironmentException", "LuiException",
                "LuiIndexNotFoundException", "LuiLinkedTableException", "LuiPayloadException", "LuiReadOnlyException",
            ]);
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
        var settings = typeof(LuiQuery).GetMethods(declared)
            .Where(method => method.ReturnType == typeof(LuiQuery))
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
        // 2026-11-04, B.3). TypoDistance and TypoDictionaryState stay: the hosting package sees the public API only.
        const BindingFlags declared = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        static string[] Members(Type type) =>
        [
            .. type.GetProperties(declared).Select(property => property.Name),
            .. type.GetMethods(declared).Where(method => !method.IsSpecialName).Select(method => method.Name),
            .. type.GetEvents(declared).Select(@event => @event.Name),
        ];

        Members(typeof(ILuiIndex)).Order(StringComparer.Ordinal)
            .ShouldBe(["Count", "KeyKind", "Kind", "Name", "Query", "Search", "TypoDictionaryState", "TypoDistance"]);

        foreach (var write in new[] { typeof(ILuiEmbeddedIndex), typeof(ILuiLinkedIndex) })
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
        types.ShouldContain(type => string.Equals(type.Name, "ILuiEmbeddedIndex", StringComparison.Ordinal), "the guard sees the renamed type");
    }

    [Fact]
    public void The_catalog_creates_an_index_and_opens_one_that_is_there()
    {
        // ADR-0033: `Create` is the write path — it was called `Ensure`, and that name is gone (ADR-0026 allows a change of
        // a name before 0.1.0). `Index` and `LinkedIndex` are the read path: they open an index that is there and write
        // nothing. ADR-0035: none of them names a type of the application — the map of a linked index is the one type
        // argument, and the compiler infers it from the lambda.
        const BindingFlags declared = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        MethodInfo[] Named(string name) =>
            [.. typeof(LuiCatalog).GetMethods(declared).Where(method => string.Equals(method.Name, name, StringComparison.Ordinal))];

        var created = Named("Create");
        created.Length.ShouldBe(2, "one Create per content source");
        created.ShouldAllBe(method => !method.IsGenericMethodDefinition);
        created.Select(method => method.ReturnType).ShouldBe([typeof(ILuiEmbeddedIndex), typeof(ILuiLinkedIndex)], ignoreOrder: true);

        var embedded = Named("Index").ShouldHaveSingleItem("an embedded index is opened by its name, and by nothing else");
        embedded.IsGenericMethodDefinition.ShouldBeFalse();
        embedded.ReturnType.ShouldBe(typeof(ILuiEmbeddedIndex));
        embedded.GetParameters().Select(parameter => parameter.ParameterType).ShouldBe([typeof(string)]);

        var linked = Named("LinkedIndex");
        linked.Length.ShouldBe(2, "a linked index without payload columns, and one with them and the map of the application");
        linked.ShouldAllBe(method => method.ReturnType == typeof(ILuiLinkedIndex));
        linked.ShouldContain(method => !method.IsGenericMethodDefinition && method.GetParameters().Length == 1);
        linked.ShouldContain(method => method.IsGenericMethodDefinition
            && method.GetGenericArguments().Length == 1
            && method.GetParameters().Last().ParameterType.GetGenericTypeDefinition() == typeof(Func<,>));

        // the factory of the catalog stays: `Open` opens the catalog, `Index` opens the index (ADR-0033)
        typeof(LuiCatalog).GetMethod(
            "Open",
            BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly,
            [typeof(string), typeof(LuiCatalogOptions)]).ShouldNotBeNull();
    }

    [Fact]
    public void The_index_knows_no_types()
    {
        // ADR-0035: the file stores a key as text and a payload as bytes, whatever a type parameter would say. The types
        // that a caller holds while working with an index have none — the type of a payload is named where it is read
        // (GetPayload<T>), and the serialiser that reads it is the catalog's.
        Type[] untyped =
        [
            typeof(ILuiIndex), typeof(ILuiEmbeddedIndex), typeof(ILuiLinkedIndex), typeof(LuiEntry), typeof(LuiHit),
            typeof(LuiResult), typeof(LuiQuery), typeof(LuiEmbeddedIndexBuilder), typeof(LuiLinkedIndexBuilder),
            typeof(ILuiPayloadSerializer),
        ];
        untyped.Where(type => type.IsGenericType).Select(type => type.Name).ShouldBeEmpty();

        // the payload is read with the type the caller asks for, on the hit
        typeof(LuiHit).GetMethod(nameof(LuiHit.GetPayload))!.IsGenericMethodDefinition.ShouldBeTrue();
        typeof(LuiHit).GetProperty("Payload").ShouldBeNull("the payload is a method that deserialises, not a property");
        typeof(LuiTypedHit<object>).GetProperty("Payload")
            .ShouldBeNull("the typed hit reads the payload as the hit does: a method that deserialises, not a property (ADR-0044)");

        // one serialiser per catalog
        typeof(LuiCatalogOptions).GetProperty(nameof(LuiCatalogOptions.PayloadSerializer))!.PropertyType
            .ShouldBe(typeof(ILuiPayloadSerializer));
    }

    [Fact]
    public void The_retired_names_are_gone_from_the_public_surface()
    {
        // ADR-0033 and ADR-0026: before 0.1.0 a name may change — `Ensure` became `Create`, `OpenIndex` became `Index`,
        // and the untyped facade (`ILuiDocumentIndex`, `LuiDocumentHit`) is gone: one interface per capability.
        const BindingFlags declared = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        var retired = PackageAssemblies
            .SelectMany(assembly => assembly.GetExportedTypes())
            .SelectMany(type => type.GetMethods(declared))
            .Where(method => method.Name is "Ensure" or "OpenIndex" or "AddIndex" or "AddLinkedIndex" or "AddOpenIndex")
            .Select(method => $"{method.DeclaringType?.Name}.{method.Name}")
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        retired.ShouldBeEmpty("the retired names are gone, not deprecated");

        var facade = PackageAssemblies
            .SelectMany(assembly => assembly.GetExportedTypes())
            .Where(type => type.Name is "ILuiDocumentIndex" or "LuiDocumentHit")
            .Select(type => type.Name)
            .ToArray();
        facade.ShouldBeEmpty("the untyped facade is gone");

        // ADR-0035: the placeholder of "no payload" and the typed serialiser are gone with the type parameters
        PackageAssemblies
            .SelectMany(assembly => assembly.GetExportedTypes())
            .Where(type => type.Name is "LuiNoPayload" or "LuiJsonPayloadSerializer`1" or "ILuiPayloadSerializer`1" or "LuiEmbeddedIndexBuilderExtensions")
            .Select(type => type.Name)
            .ShouldBeEmpty("the placeholder and the typed serialiser are gone");
    }

    [Fact]
    public void The_container_holds_the_catalog_and_the_warm_up_but_no_index()
    {
        // ADR-0033: the container does not describe indexes — the application asks the catalog for what it needs.
        // ADR-0043: the warm-up is a service of the host, and the host asks for it — it is the second registration, and
        // it registers no index either.
        const BindingFlags declared = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        var hosting = Assembly.Load("Nordmann.LUI.Hosting").GetExportedTypes();

        hosting.ShouldNotContain(
            type => string.Equals(type.Name, "LuiServicesBuilder", StringComparison.Ordinal),
            "the builder of the indexes is gone with the indexes");

        var registrations = hosting
            .SelectMany(type => type.GetMethods(declared))
            .Where(method => method.Name.StartsWith("Add", StringComparison.Ordinal))
            .ToArray();
        registrations.Length.ShouldBe(3, "AddLui, AddLui with the connection string from the container, and AddLuiWarmup");
        registrations.ShouldAllBe(method => method.Name == "AddLui" || method.Name == "AddLuiWarmup");
        registrations.ShouldAllBe(method => method.ReturnType == typeof(IServiceCollection));
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
