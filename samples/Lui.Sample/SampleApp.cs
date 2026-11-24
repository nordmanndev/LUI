using System.Text.Json;
using System.Text.Json.Serialization;
using Nordmann.LUI.Sample.Api;
using Nordmann.LUI.Sample.Data;
using Nordmann.LUI.Sample.Indexes;

namespace Nordmann.LUI.Sample;

/// <summary>
/// The composition of the sample: LUI through its hosting package, three indexes that show the three ways an
/// application uses it, the routes in front of them, and the static client. <c>Program</c> runs it; the integration
/// tests start it on a port of their own.
/// </summary>
public static class SampleApp
{
    /// <summary>The configuration key of the path of the SQLite file (ADR-0066).</summary>
    public const string PathKey = "Sample:Path";

    /// <summary>The configuration key of the German word list — where the sample looks for it before it downloads it.</summary>
    public const string WordListKey = "Sample:WordList";

    /// <summary>Builds the application, and fills the empty indexes.</summary>
    /// <param name="args">The command line; <c>--Sample:Path=…</c> and <c>--urls=…</c> are the ones that matter.</param>
    /// <returns>The application, not started.</returns>
    public static WebApplication Build(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        var path = builder.Configuration[PathKey] ?? "lui-sample.db";

        // In code and not in an appsettings.json: a project that references the sample (the integration tests) would copy the
        // file into its output, and every host started there would read a log level that is not its own.
        builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);

        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
        });

        // The application's own table exists before LUI is asked to link an index to it (ADR-0015, ADR-0017).
        SampleDatabase.Initialize(path);
        AddServices(builder.Services, path, builder.Configuration[WordListKey]);

        var app = builder.Build();
        SampleSeed.Apply(app.Services.GetRequiredService<SampleIndexes>());
        UseClient(app);

        SuggestEndpoint.Map(app);
        IndexEndpoints.Map(app);
        app.MapGet("/api/health", (RequestStats stats) => Results.Ok(new { status = "ok", suggest = new { served = stats.Served, aborted = stats.Aborted } }));
        app.MapFallbackToFile("index.html");
        return app;
    }

    private static void AddServices(IServiceCollection services, string path, string? wordList)
    {
        services
            // The catalog is configured the way the measurements of ADR-0020 measure it: a page cache of
            // 512 MB. A stock of a million entries is a bulk load — the settings are what make
            // it a bulk load and not a million small writes.
            // The container holds the catalog and nothing else (ADR-0033): the indexes of the sample are its own.
            // The factory opens the catalog (ADR-0066): the way it is opened is the caller's, and the container
            // holds what it gets.
            .AddLui(_ => LuiCatalog.OpenOrCreate(path, new LuiCatalogOptions
            {
                Pragmas = new LuiPragmas { CacheSizeMegabytes = 512 },

                // the serializers of the payload types of the embedded indexes: one registry (ADR-0072)
                Payloads = new LuiPayloadRegistry().Use(SampleJsonContext.Default),
            }));

        services.AddSingleton<RequestStats>();
        services.AddSingleton<FillJobs>();
        services.AddSingleton(provider => new SampleIndexes(Facades(provider, path, wordList)));

        // The outbox of the linked index: the application keeps its index current (ADR-0015), the container does not
        // any more (ADR-0033).
        services.AddHostedService<OutboxService>();
    }

    /// <summary>The indexes behind the page, each with the description the index panel shows.</summary>
    private static List<ISampleIndex> Facades(IServiceProvider provider, string path, string? wordList)
    {
        var catalog = provider.GetRequiredService<LuiCatalog>();

        // The application creates its own indexes (ADR-0033): the catalog is the entry point, and the container knows
        // only the catalog.
        var recipes = catalog.Create(new LuiIndexDefinition<long, RecipePayload>("recipes", index => index.MultiWord().TypoTolerance()));
        var names = catalog.Create(new LuiIndexDefinition<long>("names", index => index.MultiWord().TypoTolerance()));
        var tags = catalog.Create(new LuiLinkedIndexDefinition<long, TagPayload>("tags",
            index => index.SingleWord()
                .LinkTo(SampleDatabase.TagTable, key: "id", text: "name", weight: "usage_count", priority: "rank")
                .WithPayload(["kind", "color"], row => new TagPayload(row.GetString("kind"), row.GetString("color")))
                .TypoTolerance()));

        return
        [
            new EmbeddedPayloadSampleIndex(
                recipes,
                SampleSeed.Recipes,
                payloadDescription: "JSON {category, minutes, emoji}, stored by LUI",
                description: "Cookbook. Several words, so 'kuchen' finds 'Gedeckter Apfelkuchen' by its second word."),
            new EmbeddedSampleIndex(
                names,
                SampleSeed.Names,
                description: "People. Typo tolerance with a dictionary of its own: 'megn' becomes 'megan' here, and in 'tags' it finds nothing."),
            new LinkedSampleIndex(
                tags,
                path,
                payloadDescription: "columns kind, color — read from the table",
                description: "Tags in the application's own table. 'vegan' has the priority System and comes first for every input it matches."),
            // The bulk indexes (ADR-0029): they hold what a source produces for the number the user asks for. They are not
            // registered — an index of a million entries is minutes of work, and nobody pays for it who does not want it. The
            // index of LUI is created when the panel asks for it, and loaded when the button of the panel is pressed.
            new BulkSampleIndex(catalog, new WordSource(wordList)),
            new BulkSampleIndex(catalog, new PeopleSource()),
            new BulkSampleIndex(catalog, new HashtagSource()),
        ];
    }

    /// <summary>The static client, behind a strict policy: no inline script, no inline style, so the escaping in the client is not the only thing between a hostile entry and the DOM.</summary>
    private static void UseClient(WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            context.Response.Headers.ContentSecurityPolicy =
                "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; base-uri 'none'; frame-ancestors 'none'";
            context.Response.Headers.XContentTypeOptions = "nosniff";
            await next(context).ConfigureAwait(false);
        });

        app.UseDefaultFiles();
        app.UseStaticFiles(new StaticFileOptions
        {
            // a sample that is edited and reloaded: no stale module
            OnPrepareResponse = context => context.Context.Response.Headers.CacheControl = "no-cache",
        });
    }
}
