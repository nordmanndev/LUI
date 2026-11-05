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
    /// <summary>The configuration key of the SQLite connection string.</summary>
    public const string ConnectionStringKey = "Sample:ConnectionString";

    /// <summary>Builds the application, and fills the empty indexes.</summary>
    /// <param name="args">The command line; <c>--Sample:ConnectionString=…</c> and <c>--urls=…</c> are the ones that matter.</param>
    /// <returns>The application, not started.</returns>
    public static WebApplication Build(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        var connectionString = builder.Configuration[ConnectionStringKey] ?? "Data Source=lui-sample.db;Default Timeout=10";

        // In code and not in an appsettings.json: a project that references the sample (the integration tests) would copy the
        // file into its output, and every host started there would read a log level that is not its own.
        builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);

        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
        });

        // The application's own table exists before LUI is asked to link an index to it (ADR-0015, ADR-0017).
        SampleDatabase.Initialize(connectionString);
        AddLui(builder.Services, connectionString);

        var app = builder.Build();
        SampleSeed.Apply(app.Services.GetRequiredService<SampleIndexes>());
        UseClient(app);

        SuggestEndpoint.Map(app);
        IndexEndpoints.Map(app);
        app.MapGet("/api/health", (RequestStats stats) => Results.Ok(new { status = "ok", suggest = new { served = stats.Served, aborted = stats.Aborted } }));
        app.MapFallbackToFile("index.html");
        return app;
    }

    private static void AddLui(IServiceCollection services, string connectionString)
    {
        services
            .AddLui(connectionString)
            // Embedded, several words, a JSON payload that LUI stores next to each entry.
            .AddIndex("recipes", index => index.MultiWord().WithPayload(SampleJsonContext.Default.RecipePayload).TypoTolerance())
            // Embedded, several words, no payload: the names, where the typo tolerance matters most.
            .AddIndex("names", index => index.MultiWord().TypoTolerance())
            // Linked to the application's table: the text, the weight, the priority and the payload are its columns, and the
            // hosted service applies the outbox that the application's triggers fill, every two seconds.
            .AddLinkedIndex(
                "tags",
                index => index.SingleWord()
                    .LinkTo(SampleDatabase.TagTable, key: "id", text: "name", weight: "usage_count", priority: "rank")
                    .WithPayload(["kind", "color"], row => new TagPayload(row.GetString("kind"), row.GetString("color")))
                    .TypoTolerance(),
                applyPendingEvery: TimeSpan.FromSeconds(2))
            // The bulk indexes (ADR-0029): they hold what a source produces for the number the user asks for. They are empty
            // until the button of the index panel fills them — a fill of a million entries takes minutes, and nobody pays for it
            // who does not want it.
            .AddIndex("words", index => index.SingleWord())
            .AddIndex("people", index => index.MultiWord().TypoTolerance())
            .AddIndex("hashtags", index => index.SingleWord())
            .WarmUpTypoDictionariesOnStart();

        services.AddSingleton<RequestStats>();
        services.AddSingleton<FillJobs>();
        services.AddSingleton(provider => new SampleIndexes(Facades(provider, connectionString)));
    }

    /// <summary>The indexes behind the page, each with the description the index panel shows.</summary>
    private static List<ISampleIndex> Facades(IServiceProvider provider, string connectionString) =>
    [
        new EmbeddedSampleIndex<RecipePayload>(
            provider.GetRequiredKeyedService<ILuiEmbeddedIndex<long, RecipePayload>>("recipes"),
            SampleSeed.Recipes,
            SampleJsonContext.Default.RecipePayload,
            payloadDescription: "JSON {category, minutes, emoji}, stored by LUI",
            description: "Cookbook. Several words, so 'kuchen' finds 'Gedeckter Apfelkuchen' by its second word."),
        new EmbeddedSampleIndex<LuiNoPayload>(
            provider.GetRequiredKeyedService<ILuiEmbeddedIndex<long, LuiNoPayload>>("names"),
            SampleSeed.Names,
            payloadType: null,
            payloadDescription: "none",
            description: "People. Typo tolerance with a dictionary of its own: 'megn' becomes 'megan' here, and in 'tags' it finds nothing."),
        new LinkedSampleIndex<TagPayload>(
            provider.GetRequiredKeyedService<ILuiLinkedIndex<TagPayload>>("tags"),
            connectionString,
            payloadDescription: "columns kind, color — read from the table",
            description: "Tags in the application's own table. 'vegan' has the priority System and comes first for every input it matches."),
        new BulkSampleIndex(
            provider.GetRequiredKeyedService<ILuiEmbeddedIndex<long, LuiNoPayload>>("words"),
            new WordSource(GermanWordList.Find())),
        new BulkSampleIndex(
            provider.GetRequiredKeyedService<ILuiEmbeddedIndex<long, LuiNoPayload>>("people"),
            new PeopleSource()),
        new BulkSampleIndex(
            provider.GetRequiredKeyedService<ILuiEmbeddedIndex<long, LuiNoPayload>>("hashtags"),
            new HashtagSource()),
    ];

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
