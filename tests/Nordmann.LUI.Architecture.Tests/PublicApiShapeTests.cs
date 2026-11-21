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
/// that misleads its consumers (ADR-0016).
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
        // a lie about latency (ADR-0016).
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

        // LuiHit and LuiResult have two generic forms each (ADR-0073: without and with a payload): the guard counts all forms of a name
        const int genericForms = 4;

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
        LibraryAssembly.GetExportedTypes().Count(type => named.Contains(TypeNames.Of(type), StringComparer.Ordinal)).ShouldBe(named.Length + genericForms);
        LibraryAssembly.GetExportedTypes().Where(type => type.IsGenericTypeDefinition && TypeNames.Of(type) is "LuiHit" or "LuiResult")
            .Select(TypeNames.Of).Order(StringComparer.Ordinal).ShouldBe(["LuiHit", "LuiHit", "LuiResult", "LuiResult"]);
    }

    [Fact]
    public void The_statements_of_the_read_path_are_prepared()
    {
        // ADR-0067: the statements of a query (the prefix path, FTS5 and the hits) are prepared — an explicit Prepare() costs nothing (measured) and
        // states the intent. The guard reads the sources: a command that is built and not prepared fails here.
        foreach (var file in new[] { "Querying/CandidateReader.cs", "Querying/HitReader.cs" })
        {
            var source = File.ReadAllText(Path.Combine(Repository.RootPath, "src", "Nordmann.LUI", file));

            source.Contains(".Prepare();", StringComparison.Ordinal).ShouldBeTrue($"{file} prepares its statement");
        }
    }

    [Fact]
    public void The_catalogue_has_no_second_read_only_notion_and_no_connection_string()
    {
        // ADR-0068: the way a catalogue was opened is its only read-only notion; SQLite refuses a write and LUI translates
        // the refusal. A bool and a guard beside it would be a second answer to the same question.
        const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        var database = LibraryAssembly.GetType("Nordmann.LUI.Storage.Database").ShouldNotBeNull();

        database.GetProperty("ReadOnly", all).ShouldBeNull("Database.ReadOnly is the second read-only notion");
        database.GetMethod("ThrowIfReadOnly", all).ShouldBeNull("the refusal comes from SQLite, not from a guard of LUI");
        typeof(LuiCatalog).GetProperty("ConnectionString", all).ShouldBeNull("the application never sees a connection string");
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
                "LuiCatalogNotFoundException", "LuiConfigurationException", "LuiDefinitionMismatchException", "LuiEnvironmentException",
                "LuiException", "LuiFormatException",
                "LuiIndexNotFoundException", "LuiKeyException", "LuiLinkedTableException", "LuiPayloadException", "LuiReadOnlyException",
            ]);
    }

    [Fact]
    public void The_key_types_are_long_int_string_and_guid_and_the_old_kind_stays_until_268()
    {
        // ADR-0073, issue #270: `LuiKeyType` is the key type a definition stores, exactly; `LuiKeyKind` (numbers or texts) stays until #268
        // removes it with the old calls. A renumbered value is a break (AGENTS.md), so the numbers are part of the guard.
        Enum.GetNames<LuiKeyType>().ShouldBe(["Long", "Int", "String", "Guid"]);
        Enum.GetValues<LuiKeyType>().Select(value => (int)value).ShouldBe([0, 1, 2, 3]);
        Enum.GetNames<LuiKeyKind>().ShouldBe(["Number", "Text"], "LuiKeyKind stays until #268");

        const BindingFlags declared = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        static string[] Surface(Type type) =>
            [.. type.GetProperties(declared).Select(property => $"{TypeText(property.PropertyType)} {property.Name} {{ get; }}")
                .Concat(type.GetConstructors(declared).Select(constructor => $".ctor({Parameters(constructor)})"))
                .Concat(type.GetMethods(declared).Where(method => !method.IsSpecialName).Select(method => $"{TypeText(method.ReturnType)} {method.Name}({Parameters(method)})"))
                .Order(StringComparer.Ordinal)];

        // the new exceptions: sealed, derived from LuiException, and they carry what the message names
        typeof(LuiKeyException).IsSealed.ShouldBeTrue();
        typeof(LuiKeyException).BaseType.ShouldBe(typeof(LuiException));
        Surface(typeof(LuiKeyException)).ShouldBe(
            ["LuiKeyType KeyType { get; }", "string IndexName { get; }", "string StoredKey { get; }"],
            "the stored key and the index it is in");
        typeof(LuiFormatException).IsSealed.ShouldBeTrue();
        typeof(LuiFormatException).BaseType.ShouldBe(typeof(LuiException));
        Surface(typeof(LuiFormatException)).ShouldBe(
            ["int FoundFormat { get; }", "int SupportedFormat { get; }", "string Path { get; }"],
            "the file and the two formats");
        typeof(LuiFormatException).GetConstructors().ShouldBeEmpty("only the library throws it");
        typeof(LuiKeyException).GetConstructors().ShouldBeEmpty("only the library throws it");
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

        // the typed query of an index definition has the same settings (ADR-0073)
        var typedSettings = typeof(LuiQuery<>).GetMethods(declared)
            .Where(method => method.ReturnType.IsGenericType && method.ReturnType.GetGenericTypeDefinition() == typeof(LuiQuery<>))
            .Select(method => method.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        typedSettings.ShouldBe(options, "LuiQuery<TKey> has the settings of LuiQuery, and no other.");

        // and so has the query of an index with a payload (issue #269)
        var payloadSettings = typeof(LuiQuery<,>).GetMethods(declared)
            .Where(method => method.ReturnType.IsGenericType && method.ReturnType.GetGenericTypeDefinition() == typeof(LuiQuery<,>))
            .Select(method => method.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        payloadSettings.ShouldBe(options, "LuiQuery<TKey, TPayload> has the settings of LuiQuery, and no other.");
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
        // argument, and the compiler infers it from the lambda. ADR-0062: the route is named in the call — `Create`
        // creates an embedded index, `CreateLinked` a linked one; the compiler does not choose it.
        const BindingFlags declared = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        MethodInfo[] Named(string name) =>
            [.. typeof(LuiCatalog).GetMethods(declared).Where(method => string.Equals(method.Name, name, StringComparison.Ordinal))];

        // ADR-0073: a definition is the single declaration. `Create` and `Index` take one — an embedded definition gives an
        // embedded handle, a linked definition a linked handle — and `Create(params …)` creates several in one transaction.
        // The old calls stay, a thin layer over the same path, until their removal (issue #268).
        var created = Named("Create");
        created.Length.ShouldBe(6, "the old `Create(name, …)`, one per definition type (four: with and without payload), and the many");
        var createdEmbedded = created.Single(method => method.GetParameters().Length == 2);
        createdEmbedded.IsGenericMethodDefinition.ShouldBeFalse();
        createdEmbedded.ReturnType.ShouldBe(typeof(ILuiEmbeddedIndex));
        createdEmbedded.GetParameters()[0].ParameterType.ShouldBe(typeof(string));

        var definitions = created.Where(method => method.GetParameters().Length == 1).ToArray();
        definitions.Length.ShouldBe(5);
        var many = definitions.Single(method => method.ReturnType == typeof(void));
        many.IsGenericMethodDefinition.ShouldBeFalse();
        many.GetParameters().Single().ParameterType.ShouldBe(typeof(LuiIndexDefinitionBase[]));
        many.GetParameters().Single().IsDefined(typeof(ParamArrayAttribute), inherit: false).ShouldBeTrue("`Create(a, b, c)`");
        Handle(definitions, typeof(LuiIndexDefinition<>), typeof(LuiEmbeddedIndex<>));
        Handle(definitions, typeof(LuiLinkedIndexDefinition<>), typeof(LuiLinkedIndex<>));
        HandleWithPayload(definitions, typeof(LuiIndexDefinition<,>), typeof(LuiEmbeddedIndex<,>));
        HandleWithPayload(definitions, typeof(LuiLinkedIndexDefinition<,>), typeof(LuiLinkedIndex<,>));

        var createdLinked = Named("CreateLinked");
        createdLinked.Length.ShouldBe(2, "`CreateLinked` creates a linked index — and has no definition form: `Create` takes all four; the second takes the typed builder (issue #269)");
        createdLinked.ShouldAllBe(method => method.ReturnType == typeof(ILuiLinkedIndex));
        createdLinked.Count(method => !method.IsGenericMethodDefinition).ShouldBe(1);

        // the bridge for the old calls with `WithPayload` (issue #269, until #268): the typed builder is the parameter, the payload type the one type argument
        var bridge = createdLinked.Single(method => method.IsGenericMethodDefinition);
        bridge.GetGenericArguments().Length.ShouldBe(1);
        bridge.GetParameters().Select(parameter => parameter.ParameterType).ShouldBe(
            [typeof(string), typeof(Func<,>).MakeGenericType(typeof(LuiIndexKindBuilder), typeof(LuiLinkedIndexBuilder<>).MakeGenericType(bridge.GetGenericArguments()[0]))]);
        HasNotNullConstraint(bridge.GetGenericArguments()[0]).ShouldBeTrue("where TPayload : notnull");

        // the factory of the catalog stays: `Open` opens the catalog, `Index` opens the index (ADR-0033)
        typeof(LuiCatalog).GetMethod(
            "Open",
            BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly,
            [typeof(string), typeof(LuiCatalogOptions)]).ShouldNotBeNull();
    }

    [Fact]
    public void The_catalog_opens_an_index_by_its_name_or_by_its_definition()
    {
        // ADR-0033 and ADR-0073: `Index` is the read path, one name for both sources. A definition gives the handle of its
        // source; there is no `LinkedIndex(definition)`. The old calls stay until their removal (issue #268).
        const BindingFlags declared = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        MethodInfo[] Named(string name) =>
            [.. typeof(LuiCatalog).GetMethods(declared).Where(method => string.Equals(method.Name, name, StringComparison.Ordinal))];

        var opened = Named("Index");
        opened.Length.ShouldBe(5, "an index is opened by its name, or by its definition (four of them: with and without payload) — one name for both sources");
        var embedded = opened.Single(method => method.GetParameters()[0].ParameterType == typeof(string));
        embedded.IsGenericMethodDefinition.ShouldBeFalse();
        embedded.ReturnType.ShouldBe(typeof(ILuiEmbeddedIndex));
        embedded.GetParameters().Select(parameter => parameter.ParameterType).ShouldBe([typeof(string)]);
        var openedByDefinition = opened.Where(method => method != embedded).ToArray();
        Handle(openedByDefinition, typeof(LuiIndexDefinition<>), typeof(LuiEmbeddedIndex<>));
        Handle(openedByDefinition, typeof(LuiLinkedIndexDefinition<>), typeof(LuiLinkedIndex<>));
        HandleWithPayload(openedByDefinition, typeof(LuiIndexDefinition<,>), typeof(LuiEmbeddedIndex<,>));
        HandleWithPayload(openedByDefinition, typeof(LuiLinkedIndexDefinition<,>), typeof(LuiLinkedIndex<,>));

        var linked = Named("LinkedIndex");
        linked.Length.ShouldBe(2, "a linked index without payload columns, and one with them and the map of the application");
        linked.ShouldAllBe(method => method.ReturnType == typeof(ILuiLinkedIndex));
        linked.ShouldContain(method => !method.IsGenericMethodDefinition && method.GetParameters().Length == 1);
        linked.ShouldContain(method => method.IsGenericMethodDefinition
            && method.GetGenericArguments().Length == 1
            && method.GetParameters().Last().ParameterType.GetGenericTypeDefinition() == typeof(Func<,>));

    }

    /// <summary>The type of the key is the one type argument of the method, and the compiler infers it from the definition.</summary>
    private static void Handle(MethodInfo[] methods, Type definition, Type handle)
    {
        var method = methods.Single(candidate => candidate.IsGenericMethodDefinition
            && candidate.GetParameters().Single().ParameterType.GetGenericTypeDefinition() == definition);
        method.GetGenericArguments().Length.ShouldBe(1);
        method.ReturnType.GetGenericTypeDefinition().ShouldBe(handle);
        method.ReturnType.GetGenericArguments().Single().ShouldBe(method.GetGenericArguments().Single());
    }

    /// <summary>The types of the key and of the payload are the two type arguments of the method, and the compiler infers them from the definition.</summary>
    private static void HandleWithPayload(MethodInfo[] methods, Type definition, Type handle)
    {
        var method = methods.Single(candidate => candidate.IsGenericMethodDefinition
            && candidate.GetParameters().Single().ParameterType.IsGenericType
            && candidate.GetParameters().Single().ParameterType.GetGenericTypeDefinition() == definition);
        var arguments = method.GetGenericArguments();
        arguments.Length.ShouldBe(2);
        method.ReturnType.GetGenericTypeDefinition().ShouldBe(handle);
        method.ReturnType.GetGenericArguments().ShouldBe(arguments);
        arguments.ShouldAllBe(argument => HasNotNullConstraint(argument), "where TKey : notnull where TPayload : notnull");
    }

    [Fact]
    public void No_builder_holds_a_catalog()
    {
        // ADR-0073: a builder describes, the catalog creates — a builder that holds the catalog (or a name) is a builder
        // that creates. The guard reads every field and every property of every builder, the private ones too.
        const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        var builders = LibraryAssembly.GetTypes()
            .Where(type => type.Name.Contains("Builder", StringComparison.Ordinal) && !type.Name.StartsWith('<'))
            .ToArray();
        builders.Length.ShouldBeGreaterThanOrEqualTo(3, "the guard sees the three builders");

        var offenders = builders
            .SelectMany(type => type.GetFields(all).Select(field => (Member: $"{type.Name}.{field.Name}", Type: field.FieldType))
                .Concat(type.GetProperties(all).Select(property => (Member: $"{type.Name}.{property.Name}", Type: property.PropertyType)))
                .Concat(type.GetConstructors(all).SelectMany(constructor => constructor.GetParameters()
                    .Select(parameter => (Member: $"{type.Name}..ctor({parameter.Name})", Type: parameter.ParameterType)))))
            .Where(member => member.Type == typeof(LuiCatalog))
            .Select(member => member.Member)
            .ToArray();

        offenders.ShouldBeEmpty("No builder holds a LuiCatalog: the catalog creates from the description.");
    }

    [Fact]
    public void No_type_outside_LUI_can_derive_from_the_definition_base()
    {
        // ADR-0073: the two kinds of definition are the whole family — the overloads of Create and Index rely on it
        const BindingFlags all = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        var @base = typeof(LuiIndexDefinitionBase);

        @base.IsAbstract.ShouldBeTrue();
        @base.GetConstructors(all).ShouldNotBeEmpty();
        @base.GetConstructors(all).ShouldAllBe(
            constructor => !(constructor.IsPublic || constructor.IsFamily || constructor.IsFamilyOrAssembly),
            "a constructor that another assembly can call is a way to derive");

        LibraryAssembly.GetTypes()
            .Where(type => type.IsSubclassOf(@base))
            .Select(type => type.Name)
            .Order(StringComparer.Ordinal)
            .ShouldBe(["LuiIndexDefinition`1", "LuiIndexDefinition`2", "LuiLinkedIndexDefinition`1", "LuiLinkedIndexDefinition`2"]);
        LibraryAssembly.GetTypes().Where(type => type.IsSubclassOf(@base)).ShouldAllBe(type => type.IsSealed, "a definition type does not derive from another");

        // the handles are closed in the same way: no constructor of the abstract read side can be called from outside
        typeof(LuiIndex<>).IsAbstract.ShouldBeTrue();
        typeof(LuiIndex<>).GetConstructors(all).ShouldAllBe(
            constructor => !(constructor.IsPublic || constructor.IsFamily || constructor.IsFamilyOrAssembly));
        typeof(LuiIndex<,>).IsAbstract.ShouldBeTrue();
        typeof(LuiIndex<,>).GetConstructors(all).ShouldAllBe(
            constructor => !(constructor.IsPublic || constructor.IsFamily || constructor.IsFamilyOrAssembly));
    }

    [Fact]
    public void The_typed_handles_have_exactly_the_members_of_the_release()
    {
        // ADR-0073 and issue #261: the members of the typed handles, as the tables of the issue name them — sorted, one
        // string each. The target that every later pull request extends (payload variants, CheckPayloads, NeedsInitialSync, …).
        // A change of the surface is a change of this list, in the same commit.
        var expected = ExpectedMembers();

        var actual = LibraryAssembly.GetExportedTypes()
            .Where(type => type.IsGenericTypeDefinition || string.Equals(type.Name, nameof(LuiIndexDefinitionBase), StringComparison.Ordinal))
            .Where(type => expected.ContainsKey(TypeText(type)))
            .ToDictionary(TypeText, type => MemberSignatures(type).Order(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);

        actual.Keys.Order(StringComparer.Ordinal).ShouldBe(expected.Keys.Order(StringComparer.Ordinal));
        foreach (var (name, members) in expected)
        {
            actual[name].ShouldBe(members.Order(StringComparer.Ordinal).ToArray(), name);
        }

        // modifiers and equality (the tables of the issue)
        typeof(LuiIndex<>).IsAbstract.ShouldBeTrue();
        typeof(LuiEmbeddedIndex<>).BaseType!.GetGenericTypeDefinition().ShouldBe(typeof(LuiIndex<>));
        typeof(LuiLinkedIndex<>).BaseType!.GetGenericTypeDefinition().ShouldBe(typeof(LuiIndex<>));
        new[] { typeof(LuiEmbeddedIndex<>), typeof(LuiLinkedIndex<>), typeof(LuiQuery<>), typeof(LuiResult<>), typeof(LuiHit<>), typeof(LuiEntry<>) }
            .ShouldAllBe(type => type.IsSealed && type.IsClass);
        new[] { typeof(LuiEntry<>), typeof(LuiHit<>) }.ShouldAllBe(type => type.GetMethod("<Clone>$") != null, "records: value equality");
        new[] { typeof(LuiIndex<>), typeof(LuiEmbeddedIndex<>), typeof(LuiLinkedIndex<>), typeof(LuiQuery<>), typeof(LuiResult<>) }
            .ShouldAllBe(type => IsReferenceEqual(type), "reference equality");
        new[] { typeof(LuiIndex<>), typeof(LuiEmbeddedIndex<>), typeof(LuiLinkedIndex<>), typeof(LuiEntry<>), typeof(LuiQuery<>), typeof(LuiResult<>), typeof(LuiHit<>), typeof(LuiIndexDefinition<>), typeof(LuiLinkedIndexDefinition<>) }
            .ShouldAllBe(type => HasNotNullConstraint(type.GetGenericArguments().Single()), "where TKey : notnull");

        // the payload variants (ADR-0073, issue #269): the same shape, with a payload
        typeof(LuiIndex<,>).IsAbstract.ShouldBeTrue();
        typeof(LuiEmbeddedIndex<,>).BaseType!.GetGenericTypeDefinition().ShouldBe(typeof(LuiIndex<,>));
        typeof(LuiLinkedIndex<,>).BaseType!.GetGenericTypeDefinition().ShouldBe(typeof(LuiIndex<,>));
        Type[] payloadTypes = [typeof(LuiEmbeddedIndex<,>), typeof(LuiLinkedIndex<,>), typeof(LuiQuery<,>), typeof(LuiResult<,>), typeof(LuiHit<,>), typeof(LuiEntry<,>), typeof(LuiLinkedIndexBuilder<>)];
        payloadTypes.ShouldAllBe(type => type.IsSealed && type.IsClass);
        new[] { typeof(LuiEntry<,>), typeof(LuiHit<,>) }.ShouldAllBe(type => type.GetMethod("<Clone>$") != null, "records: value equality");
        new[] { typeof(LuiIndex<,>), typeof(LuiEmbeddedIndex<,>), typeof(LuiLinkedIndex<,>), typeof(LuiQuery<,>), typeof(LuiResult<,>) }
            .ShouldAllBe(type => IsReferenceEqual(type), "reference equality");
        new[] { typeof(LuiIndex<,>), typeof(LuiEmbeddedIndex<,>), typeof(LuiLinkedIndex<,>), typeof(LuiEntry<,>), typeof(LuiQuery<,>), typeof(LuiResult<,>), typeof(LuiHit<,>), typeof(LuiIndexDefinition<,>), typeof(LuiLinkedIndexDefinition<,>) }
            .ShouldAllBe(
                type => HasNotNullConstraint(type.GetGenericArguments()[0]) && HasNotNullConstraint(type.GetGenericArguments()[1]),
                "where TKey : notnull where TPayload : notnull");
        HasNotNullConstraint(typeof(LuiLinkedIndexBuilder<>).GetGenericArguments().Single()).ShouldBeTrue("where TPayload : notnull");

        // the type relations: the variants with a payload are other types, not subtypes of the ones without (a shared base can come later without a break)
        IsSubclassOfGeneric(typeof(LuiIndex<,>), typeof(LuiIndex<>)).ShouldBeFalse();
        IsSubclassOfGeneric(typeof(LuiHit<,>), typeof(LuiHit<>)).ShouldBeFalse();
        IsSubclassOfGeneric(typeof(LuiEntry<,>), typeof(LuiEntry<>)).ShouldBeFalse();
        typeof(LuiLinkedIndexBuilder<>).BaseType.ShouldBe(typeof(object), "the typed builder does not derive from LuiLinkedIndexBuilder");
        typeof(LuiIndexDefinition<,>).BaseType.ShouldBe(typeof(LuiIndexDefinitionBase));
        typeof(LuiLinkedIndexDefinition<,>).BaseType.ShouldBe(typeof(LuiIndexDefinitionBase));
    }

    [Fact]
    public void A_handle_with_a_payload_has_no_write_without_a_payload()
    {
        // issue #269: no entry without a payload can reach an index with a payload — no `Upsert(key, text, weight)`, no
        // `LuiEntry<TKey>` and no payload-less `Sync` on the handle. With `TPayload = int`, `Upsert(1, "x", 5)` means the payload.
        const BindingFlags declared = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        var embedded = typeof(LuiEmbeddedIndex<,>);
        var payload = embedded.GetGenericArguments()[1];

        embedded.GetMethods(declared).ShouldAllBe(method => !method.GetParameters().Any(parameter => Mentions(parameter.ParameterType, typeof(LuiEntry<>))),
            "no write takes an entry without a payload");
        embedded.GetMethods(declared).Where(method => string.Equals(method.Name, "Upsert", StringComparison.Ordinal)).ShouldAllBe(
            method => method.GetParameters().Any(parameter => parameter.ParameterType == payload)
                      || method.GetParameters().Any(parameter => Mentions(parameter.ParameterType, typeof(LuiEntry<,>))),
            "every Upsert takes the payload or an entry that has one");
        var upsert = embedded.GetMethods(declared).Single(method => string.Equals(method.Name, "Upsert", StringComparison.Ordinal) && method.GetParameters().Length == 6);
        upsert.GetParameters().Select(parameter => parameter.Name).Take(3).ShouldBe(["key", "text", "payload"], "the payload comes right after the text, in the order of the entry");
        typeof(LuiLinkedIndex<,>).GetMethods(declared).ShouldAllBe(method => !method.GetParameters().Any(parameter => Mentions(parameter.ParameterType, typeof(LuiEntry<>)) || Mentions(parameter.ParameterType, typeof(LuiEntry<,>))),
            "a linked index writes nothing: the application's table is the content");
    }

    [Fact]
    public void The_typed_linked_builder_has_the_option_methods_of_the_untyped_one()
    {
        // issue #269: `WithPayload` returns the typed builder, which does not derive from the untyped one — so the options must
        // be on both, or the order of `WithPayload` and the options would not be free
        const BindingFlags declared = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        string[] Options(Type type) =>
            [.. type.GetMethods(declared).Where(method => !method.IsSpecialName && method.ReturnType.Name.StartsWith("LuiLinkedIndexBuilder", StringComparison.Ordinal))
                .Select(method => $"{method.Name}({string.Join(", ", method.GetParameters().Select(parameter => parameter.ParameterType.Name))})")
                .Order(StringComparer.Ordinal)];

        var untyped = Options(typeof(LuiLinkedIndexBuilder)).Where(option => !option.StartsWith("WithPayload", StringComparison.Ordinal)).ToArray();
        untyped.ShouldNotBeEmpty();
        Options(typeof(LuiLinkedIndexBuilder<>)).ShouldBe(untyped, "the typed builder offers the options of the untyped one, and nothing else");

        var withPayload = typeof(LuiLinkedIndexBuilder).GetMethods(declared).Single(method => string.Equals(method.Name, "WithPayload", StringComparison.Ordinal));
        withPayload.IsGenericMethodDefinition.ShouldBeTrue();
        withPayload.ReturnType.GetGenericTypeDefinition().ShouldBe(typeof(LuiLinkedIndexBuilder<>));
        withPayload.ReturnType.GetGenericArguments().ShouldBe(withPayload.GetGenericArguments());
        HasNotNullConstraint(withPayload.GetGenericArguments().Single()).ShouldBeTrue("where TPayload : notnull");
        typeof(LuiLinkedIndexBuilder<>).GetMethods(declared).ShouldNotContain(method => string.Equals(method.Name, "WithPayload", StringComparison.Ordinal), "the payload is declared once");
    }

    private static bool IsSubclassOfGeneric(Type type, Type generic)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition() == generic)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether a parameter type is, or is a collection of, the generic type — <c>LuiEntry&lt;TKey&gt;</c>, <c>IEnumerable&lt;LuiEntry&lt;TKey&gt;&gt;</c>.</summary>
    private static bool Mentions(Type type, Type generic) =>
        type.IsGenericType && (type.GetGenericTypeDefinition() == generic || type.GetGenericArguments().Any(argument => Mentions(argument, generic)));

    /// <summary>The members of the typed handles of this release (ADR-0073): sorted by the test, listed here as the tables of the issue name them.</summary>
    private static Dictionary<string, string[]> ExpectedMembers()
    {
        return new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["LuiIndexDefinitionBase"] =
            [
                "LuiIndexSource Source { get; }",
                "string Name { get; }",
                "string ToString()",
                "Type KeyType { get; }",
                "Type PayloadType { get; }",
            ],
            ["LuiIndexDefinition<TKey>"] =
            [
                ".ctor(string name, Func<LuiIndexKindBuilder, LuiEmbeddedIndexBuilder> index)",
            ],
            ["LuiLinkedIndexDefinition<TKey>"] =
            [
                ".ctor(string name, Func<LuiIndexKindBuilder, LuiLinkedIndexBuilder> index)",
            ],
            ["LuiEmbeddedIndex<TKey>"] =
            [
                "LuiIntegrityReport Check(IEnumerable<LuiEntry<TKey>> entries, CancellationToken cancellationToken = default)",
                "LuiSyncReport Sync(IAsyncEnumerable<LuiEntry<TKey>> entries, LuiSyncOptions options = default, CancellationToken cancellationToken = default)",
                "LuiSyncReport Sync(IEnumerable<LuiEntry<TKey>> entries, LuiSyncOptions options = default, CancellationToken cancellationToken = default)",
                "bool Remove(TKey key, SqliteTransaction transaction = default)",
                "bool UpdatePriority(TKey key, LuiPriority priority, SqliteTransaction transaction = default)",
                "bool UpdateWeight(TKey key, int weight, SqliteTransaction transaction = default)",
                "int UpsertMany(IEnumerable<LuiEntry<TKey>> entries, SqliteTransaction transaction = default, CancellationToken cancellationToken = default)",
                "void Rebuild()",
                "void Upsert(LuiEntry<TKey> entry, SqliteTransaction transaction = default)",
                "void Upsert(TKey key, string text, int weight = default, LuiPriority priority = default, SqliteTransaction transaction = default)",
            ],
            ["LuiLinkedIndex<TKey>"] =
            [
                "LuiLinkedSyncReport Sync(LuiSyncOptions options = default, CancellationToken cancellationToken = default)",
                "LuiTriggerState Triggers()",
                "bool Refresh(TKey key, SqliteTransaction transaction = default)",
                "int ApplyPending(CancellationToken cancellationToken = default)",
                "int RefreshMany(IEnumerable<TKey> keys, SqliteTransaction transaction = default, CancellationToken cancellationToken = default)",
                "long Pending()",
                "string TriggerScript()",
                "void CreateTriggers()",
                "void DropTriggers()",
            ],
        }
        .Concat(ExpectedPayloadVariants())
        .Concat(ExpectedValues())
        .Concat(ExpectedPayloadValues())
        .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    }

    /// <summary>The entry, the query, the result and the hit of the typed handles (ADR-0073).</summary>
    private static Dictionary<string, string[]> ExpectedValues()
    {
        return new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["LuiIndex<TKey>"] =
            [
                "LuiIndexKind Kind { get; }",
                "LuiIntegrityReport Check()",
                "LuiQuery<TKey> Query(string text)",
                "LuiResult<TKey> Search(string text, LuiQueryOptions options = default, CancellationToken cancellationToken = default)",
                "LuiTypoDictionaryState TypoDictionaryState { get; }",
                "LuiTypoDistance TypoDistance { get; }",
                "long Count()",
                "string Name { get; }",
                "void Optimize()",
                "void RebuildTypoDictionary(CancellationToken cancellationToken = default)",
                "void WarmUpTypoDictionary(CancellationToken cancellationToken = default)",
            ],
            ["LuiEntry<TKey>"] =
            [
                ".ctor(TKey key, string text)",
                "LuiPriority Priority { get; init; }",
                "TKey Key { get; }",
                "int Weight { get; init; }",
                "string Text { get; }",
            ],
            ["LuiQuery<TKey>"] =
            [
                "LuiQuery<TKey> Candidates(int candidates)",
                "LuiQuery<TKey> PriorityMode(LuiPriorityMode mode)",
                "LuiQuery<TKey> Rank(LuiRankingOrder order)",
                "LuiQuery<TKey> Similarity(ILuiSimilarity similarity)",
                "LuiQuery<TKey> Take(int maxResults)",
                "LuiQuery<TKey> TrackTimings(bool track = default)",
                "LuiQuery<TKey> TypoTolerance(bool enabled = default)",
                "LuiResult<TKey> Execute(CancellationToken cancellationToken = default)",
            ],
            ["LuiResult<TKey>"] =
            [
                ".ctor()",
                "IReadOnlyList<LuiHit<TKey>> Hits { get; init; }",
                "LuiTimings Timings { get; init; }",
                "bool CorrectionSkipped { get; init; }",
                "bool Truncated { get; init; }",
                "int CandidatesConsidered { get; init; }",
                "string CorrectedQuery { get; init; }",
                "string Query { get; init; }",
            ],
            ["LuiHit<TKey>"] =
            [
                ".ctor()",
                "LuiPriority Priority { get; init; }",
                "TKey Key { get; init; }",
                "bool AtWordStart { get; init; }",
                "double Similarity { get; init; }",
                "int Weight { get; init; }",
                "string Text { get; init; }",
            ],
        };
    }

    /// <summary>The definitions, the typed builder and the handles with a payload (ADR-0073, issue #269): the members of the tables of the issue.</summary>
    private static Dictionary<string, string[]> ExpectedPayloadVariants()
    {
        return new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["LuiIndexDefinition<TKey, TPayload>"] =
            [
                ".ctor(string name, Func<LuiIndexKindBuilder, LuiEmbeddedIndexBuilder> index)",
            ],
            ["LuiLinkedIndexDefinition<TKey, TPayload>"] =
            [
                ".ctor(string name, Func<LuiIndexKindBuilder, LuiLinkedIndexBuilder<TPayload>> index)",
            ],
            ["LuiLinkedIndexBuilder<TPayload>"] =
            [
                "LuiLinkedIndexBuilder<TPayload> GermanTransliteration(bool enabled)",
                "LuiLinkedIndexBuilder<TPayload> Similarity(ILuiSimilarity similarity)",
                "LuiLinkedIndexBuilder<TPayload> TypoTolerance(LuiTypoDistance distance = default)",
            ],
            ["LuiEmbeddedIndex<TKey, TPayload>"] =
            [
                "LuiIntegrityReport Check(IEnumerable<LuiEntry<TKey, TPayload>> entries, CancellationToken cancellationToken = default)",
                "LuiSyncReport Sync(IAsyncEnumerable<LuiEntry<TKey, TPayload>> entries, LuiSyncOptions options = default, CancellationToken cancellationToken = default)",
                "LuiSyncReport Sync(IEnumerable<LuiEntry<TKey, TPayload>> entries, LuiSyncOptions options = default, CancellationToken cancellationToken = default)",
                "bool Remove(TKey key, SqliteTransaction transaction = default)",
                "bool UpdatePriority(TKey key, LuiPriority priority, SqliteTransaction transaction = default)",
                "bool UpdateWeight(TKey key, int weight, SqliteTransaction transaction = default)",
                "int UpsertMany(IEnumerable<LuiEntry<TKey, TPayload>> entries, SqliteTransaction transaction = default, CancellationToken cancellationToken = default)",
                "void Rebuild()",
                "void Upsert(LuiEntry<TKey, TPayload> entry, SqliteTransaction transaction = default)",
                "void Upsert(TKey key, string text, TPayload payload, int weight = default, LuiPriority priority = default, SqliteTransaction transaction = default)",
            ],
            ["LuiLinkedIndex<TKey, TPayload>"] =
            [
                "LuiLinkedSyncReport Sync(LuiSyncOptions options = default, CancellationToken cancellationToken = default)",
                "LuiTriggerState Triggers()",
                "bool Refresh(TKey key, SqliteTransaction transaction = default)",
                "int ApplyPending(CancellationToken cancellationToken = default)",
                "int RefreshMany(IEnumerable<TKey> keys, SqliteTransaction transaction = default, CancellationToken cancellationToken = default)",
                "long Pending()",
                "string TriggerScript()",
                "void CreateTriggers()",
                "void DropTriggers()",
            ],
        };
    }

    /// <summary>The entry, the query, the result and the hit of the handles with a payload (ADR-0073, issue #269). <c>Payload</c> of the hit arrives with #262.</summary>
    private static Dictionary<string, string[]> ExpectedPayloadValues()
    {
        return new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["LuiIndex<TKey, TPayload>"] =
            [
                "LuiIndexKind Kind { get; }",
                "LuiIntegrityReport Check()",
                "LuiQuery<TKey, TPayload> Query(string text)",
                "LuiResult<TKey, TPayload> Search(string text, LuiQueryOptions options = default, CancellationToken cancellationToken = default)",
                "LuiTypoDictionaryState TypoDictionaryState { get; }",
                "LuiTypoDistance TypoDistance { get; }",
                "long Count()",
                "string Name { get; }",
                "void Optimize()",
                "void RebuildTypoDictionary(CancellationToken cancellationToken = default)",
                "void WarmUpTypoDictionary(CancellationToken cancellationToken = default)",
            ],
            ["LuiEntry<TKey, TPayload>"] =
            [
                ".ctor(TKey key, string text, TPayload payload)",
                "LuiPriority Priority { get; init; }",
                "TKey Key { get; }",
                "TPayload Payload { get; }",
                "int Weight { get; init; }",
                "string Text { get; }",
            ],
            ["LuiQuery<TKey, TPayload>"] =
            [
                "LuiQuery<TKey, TPayload> Candidates(int candidates)",
                "LuiQuery<TKey, TPayload> PriorityMode(LuiPriorityMode mode)",
                "LuiQuery<TKey, TPayload> Rank(LuiRankingOrder order)",
                "LuiQuery<TKey, TPayload> Similarity(ILuiSimilarity similarity)",
                "LuiQuery<TKey, TPayload> Take(int maxResults)",
                "LuiQuery<TKey, TPayload> TrackTimings(bool track = default)",
                "LuiQuery<TKey, TPayload> TypoTolerance(bool enabled = default)",
                "LuiResult<TKey, TPayload> Execute(CancellationToken cancellationToken = default)",
            ],
            ["LuiResult<TKey, TPayload>"] =
            [
                ".ctor()",
                "IReadOnlyList<LuiHit<TKey, TPayload>> Hits { get; init; }",
                "LuiTimings Timings { get; init; }",
                "bool CorrectionSkipped { get; init; }",
                "bool Truncated { get; init; }",
                "int CandidatesConsidered { get; init; }",
                "string CorrectedQuery { get; init; }",
                "string Query { get; init; }",
            ],
            ["LuiHit<TKey, TPayload>"] =
            [
                ".ctor()",
                "LuiPriority Priority { get; init; }",
                "TKey Key { get; init; }",
                "bool AtWordStart { get; init; }",
                "double Similarity { get; init; }",
                "int Weight { get; init; }",
                "string Text { get; init; }",
            ],
        };
    }

    /// <summary>Whether a type is no record and keeps the equality of <see cref="object"/> — two handles are the same only when they are one.</summary>
    private static bool IsReferenceEqual(Type type)
    {
        var equals = type.GetMethod("Equals", [typeof(object)]);
        return type.GetMethod("<Clone>$") is null && equals?.DeclaringType == typeof(object);
    }

    /// <summary>
    /// Whether a type parameter is declared <c>where T : notnull</c>. The compiler writes no attribute for it: the parameter
    /// has no <c>Nullable(2)</c> (that marks an unconstrained one) and its type is in a non-nullable context (<c>NullableContext(1)</c>).
    /// </summary>
    private static bool HasNotNullConstraint(Type parameter)
    {
        static bool Is(CustomAttributeData attribute, string name, byte value) =>
            string.Equals(attribute.AttributeType.Name, name, StringComparison.Ordinal)
            && attribute.ConstructorArguments.Count == 1
            && attribute.ConstructorArguments[0].Value is byte found
            && found == value;

        return !parameter.GetCustomAttributesData().Any(attribute => Is(attribute, "NullableAttribute", 2))
            && parameter.DeclaringType!.GetCustomAttributesData().Any(attribute => Is(attribute, "NullableContextAttribute", 1));
    }

    /// <summary>The public members a type declares, one string each: <c>ReturnType Name(Type name, …)</c> or <c>Type Name { get; init; }</c>.</summary>
    private static IEnumerable<string> MemberSignatures(Type type)
    {
        const BindingFlags declared = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var generated = new HashSet<string>(StringComparer.Ordinal) { "Equals", "GetHashCode", "<Clone>$" };
        var isRecord = type.GetMethod("<Clone>$", declared | BindingFlags.NonPublic) is not null;

        foreach (var constructor in type.GetConstructors(declared))
        {
            yield return $".ctor({Parameters(constructor)})";
        }

        foreach (var method in type.GetMethods(declared).Where(method => !method.IsSpecialName))
        {
            if (generated.Contains(method.Name) || (isRecord && string.Equals(method.Name, "ToString", StringComparison.Ordinal)))
            {
                continue;
            }

            yield return $"{TypeText(method.ReturnType)} {method.Name}({Parameters(method)})";
        }

        foreach (var property in type.GetProperties(declared))
        {
            var setter = property.SetMethod is null
                ? string.Empty
                : property.SetMethod.ReturnParameter.GetRequiredCustomModifiers().Any(modifier => string.Equals(modifier.Name, "IsExternalInit", StringComparison.Ordinal))
                    ? " init;"
                    : " set;";
            yield return $"{TypeText(property.PropertyType)} {property.Name} {{ get;{setter} }}";
        }
    }

    private static string Parameters(MethodBase method) =>
        string.Join(", ", method.GetParameters().Select(parameter =>
            $"{TypeText(parameter.ParameterType)} {parameter.Name}{(parameter.IsOptional ? " = default" : string.Empty)}"));

    /// <summary>A type as C# writes it: keywords for the simple types, the name without the arity, the arguments in angle brackets.</summary>
    private static string TypeText(Type type)
    {
        if (type == typeof(void))
        {
            return "void";
        }

        if (type == typeof(long))
        {
            return "long";
        }

        if (type == typeof(int))
        {
            return "int";
        }

        if (type == typeof(string))
        {
            return "string";
        }

        if (type == typeof(bool))
        {
            return "bool";
        }

        if (type == typeof(double))
        {
            return "double";
        }

        if (type.IsGenericParameter)
        {
            return type.Name;
        }

        var name = TypeNames.Of(type);
        return type.IsGenericType
            ? $"{name}<{string.Join(", ", type.GetGenericArguments().Select(TypeText))}>"
            : name;
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
        registrations.Length.ShouldBe(2, "AddLui and AddLuiWarmup — the hosting takes a factory (ADR-0066)");
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
