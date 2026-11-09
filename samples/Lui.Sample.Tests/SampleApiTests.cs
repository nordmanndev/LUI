using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Nordmann.LUI.Sample.Api;
using Shouldly;

namespace Nordmann.LUI.Sample.Tests;

/// <summary>
/// The sample's API against the real library, over a real Kestrel (PR-011, ADR-0027): every setting of a query is a
/// parameter and reaches <c>LuiQueryOptions</c>; the three indexes show the three ways an application uses LUI (embedded
/// with a JSON payload, linked to the application's table with column payload and a priority, embedded without payload);
/// every index has a typo dictionary of its own; the write side and the maintenance are reachable; and a page that
/// abandons a request stops the server's work.
/// </summary>
public sealed class SampleApiTests(SampleHost host) : IClassFixture<SampleHost>
{
    private HttpClient Client => host.Client;

    private async Task<JsonNode> Get(string path)
    {
        var response = await Client.GetAsync(path).ConfigureAwait(false);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync().ConfigureAwait(false));
        return (await response.Content.ReadFromJsonAsync<JsonNode>().ConfigureAwait(false))!;
    }

    private async Task<JsonNode> Post(string path, object? body = null)
    {
        var response = await Client.PostAsJsonAsync(path, body ?? new { }).ConfigureAwait(false);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync().ConfigureAwait(false));
        return (await response.Content.ReadFromJsonAsync<JsonNode>().ConfigureAwait(false))!;
    }

    private async Task<JsonNode> Suggest(string index, string query) =>
        await Get($"/api/suggest/{index}?q={Uri.EscapeDataString(query)}").ConfigureAwait(false);

    private static void ShouldFind(string[] texts, string expected) =>
        texts.ShouldContain(text => string.Equals(text, expected, StringComparison.Ordinal), $"'{expected}' in [{string.Join(" | ", texts)}]");

    private static string[] Texts(JsonNode result) => [.. result["results"]!.AsArray().Select(hit => hit!["text"]!.GetValue<string>())];

    [Fact]
    public async Task The_health_route_answers_and_counts_what_the_suggest_route_saw()
    {
        var health = await Get("/api/health");

        health["status"]!.GetValue<string>().ShouldBe("ok");
        health["suggest"]!["served"]!.GetValue<long>().ShouldBeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task The_indexes_show_the_three_ways_an_application_uses_LUI_and_the_bulk_indexes_beside_them()
    {
        var indexes = (await Get("/api/indexes")).AsArray().ToDictionary(index => index!["name"]!.GetValue<string>(), index => index!, StringComparer.Ordinal);

        indexes.Keys.OrderBy(name => name, StringComparer.Ordinal).ShouldBe(["hashtags", "names", "people", "recipes", "tags", "words"]);
        indexes["recipes"]["storage"]!.GetValue<string>().ShouldBe("Embedded");
        indexes["recipes"]["kind"]!.GetValue<string>().ShouldBe("MultiWord");
        indexes["names"]["payload"]!.GetValue<string>().ShouldBe("none");
        indexes["tags"]["storage"]!.GetValue<string>().ShouldBe("Linked");
        indexes["tags"]["kind"]!.GetValue<string>().ShouldBe("SingleWord");
        indexes["tags"]["triggers"]!.GetValue<string>().ShouldBe("Current", "the application created them, on purpose");

        // the curated indexes are filled by the seed; the bulk ones are empty until the button of the panel fills them (ADR-0029)
        foreach (var name in new[] { "recipes", "names", "tags" })
        {
            indexes[name]!["count"]!.GetValue<long>().ShouldBeGreaterThan(0, name);
            indexes[name]!["typoTolerance"]!.GetValue<bool>().ShouldBeTrue(name);
            indexes[name]!["source"].ShouldBeNull(name);
        }

        foreach (var name in new[] { "words", "people", "hashtags" })
        {
            indexes[name]!["count"]!.GetValue<long>().ShouldBe(0, name);
        }
    }

    [Fact]
    public async Task A_hit_of_the_embedded_index_carries_its_json_payload()
    {
        var result = await Suggest("recipes", "apfelkuch");

        var first = result["results"]![0]!;
        first["text"]!.GetValue<string>().ShouldBe("Gedeckter Apfelkuchen");
        first["priority"]!.GetValue<string>().ShouldBe("High");
        first["payload"]!["category"]!.GetValue<string>().ShouldBe("Kuchen");
        first["payload"]!["minutes"]!.GetValue<int>().ShouldBe(90);
        result["index"]!.GetValue<string>().ShouldBe("recipes");
        result["serverMs"]!.GetValue<double>().ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task A_hit_of_the_linked_index_carries_the_columns_of_the_application_s_table_and_the_system_priority_leads()
    {
        var result = await Suggest("tags", "ve");

        var first = result["results"]![0]!;
        first["text"]!.GetValue<string>().ShouldBe("vegan");
        first["priority"]!.GetValue<string>().ShouldBe("System", "a row with rank 0 is a System entry (E10)");
        first["payload"]!["kind"]!.GetValue<string>().ShouldBe("Ernährung");
        first["payload"]!["color"]!.GetValue<string>().ShouldStartWith("#");
        ShouldFind(Texts(result), "vegetarisch");
    }

    [Fact]
    public async Task A_hit_of_an_index_without_a_payload_has_none()
    {
        var first = (await Suggest("names", "schneider"))["results"]![0]!;

        first["text"]!.GetValue<string>().ShouldBe("Schneider Lena");
        first["payload"].ShouldBeNull();
    }

    [Fact]
    public async Task A_typo_is_corrected_and_the_envelope_says_what_was_searched()
    {
        var result = await Suggest("recipes", "kartofel");

        result["correctedQuery"]!.GetValue<string>().ShouldBe("kartoffel");
        result["correctionSkipped"]!.GetValue<bool>().ShouldBeFalse();
        ShouldFind(Texts(result), "Kartoffelsuppe");
    }

    [Fact]
    public async Task The_transliteration_finds_the_umlaut_by_its_two_letters()
    {
        ShouldFind(Texts(await Suggest("recipes", "kaesekuchen")), "Käsekuchen");
        ShouldFind(Texts(await Suggest("names", "mueller")), "Megan Müller");
    }

    [Fact]
    public async Task Every_index_has_a_typo_dictionary_of_its_own_so_a_search_among_tags_is_never_corrected_towards_names()
    {
        // owner's requirement (#48, ADR-0025), seen from the page
        (await Suggest("names", "megn"))["correctedQuery"]!.GetValue<string>().ShouldBe("mega");
        ShouldFind(Texts(await Suggest("names", "megn")), "Megan Müller");

        var tags = await Suggest("tags", "megn");
        tags["correctedQuery"].ShouldBeNull();
        Texts(tags).ShouldBeEmpty();

        (await Suggest("tags", "vegn"))["correctedQuery"]!.GetValue<string>().ShouldBe("vege");
        var names = await Suggest("names", "vegn");
        names["correctedQuery"].ShouldBeNull();
        Texts(names).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_settings_of_a_query_are_parameters_and_reach_the_library()
    {
        (await Get("/api/suggest/recipes?q=kuchen&take=2"))["results"]!.AsArray().Count.ShouldBe(2);

        var distinct = Texts(await Get("/api/suggest/names?q=m&take=100&distinct=true"));
        distinct.ShouldBe([.. distinct.Distinct(StringComparer.Ordinal)]);

        var byWeight = (await Get("/api/suggest/recipes?q=kartoffel&rank=WeightOnly&take=50"))["results"]!.AsArray()
            .Select(hit => hit!["weight"]!.GetValue<int>()).ToArray();
        byWeight.ShouldBe([.. byWeight.OrderByDescending(weight => weight)], "WeightOnly: the order of the weights");

        var noTypo = await Get("/api/suggest/recipes?q=kartofel&typo=false");
        noTypo["correctedQuery"].ShouldBeNull();
        Texts(noTypo).ShouldBeEmpty();

        var timed = await Get("/api/suggest/recipes?q=kuchen&timings=true");
        timed["timings"]!["totalMs"]!.GetValue<double>().ShouldBeGreaterThan(0);
        (await Suggest("recipes", "kuchen"))["timings"].ShouldBeNull("TrackTimings is off unless it is asked for");

        foreach (var similarity in new[] { "jaro-winkler", "ngram-dice", "damerau-levenshtein", "levenshtein" })
        {
            Texts(await Get($"/api/suggest/recipes?q=apfelkuchen&similarity={similarity}")).ShouldNotBeEmpty(similarity);
        }

        (await Get("/api/suggest/recipes?q=apfelkuchen&candidates=3"))["candidatesConsidered"]!.GetValue<int>().ShouldBeLessThanOrEqualTo(3);
        await Get("/api/suggest/recipes?q=ve&priorityMode=CandidatesOnly&rank=SimilarityOnly");
    }

    [Theory]
    [InlineData("/api/suggest/recipes?q=a&rank=Bogus", "Bogus")]
    [InlineData("/api/suggest/recipes?q=a&priorityMode=x", "priorityMode")]
    [InlineData("/api/suggest/recipes?q=a&take=0", "MaxResults")]
    [InlineData("/api/suggest/recipes?q=a&take=1001", "MaxResults")]
    [InlineData("/api/suggest/recipes?q=a&take=abc", "")]
    [InlineData("/api/suggest/recipes?q=a&similarity=soundex", "soundex")]
    [InlineData("/api/suggest/recipes?q=a&typo=maybe", "")]
    [InlineData("/api/suggest/recipes?q=a&delayMs=99999", "delayMs")]
    public async Task A_setting_the_library_does_not_accept_is_a_400_with_the_reason(string path, string mention)
    {
        var response = await Client.GetAsync(path);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = (await response.Content.ReadFromJsonAsync<JsonNode>())!;
        problem["status"]!.GetValue<int>().ShouldBe(400);
        problem["detail"]!.GetValue<string>().ShouldContain(mention);
    }

    [Fact]
    public async Task An_unknown_index_is_a_404()
    {
        (await Client.GetAsync("/api/suggest/nope?q=a")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Client.GetAsync("/api/indexes/nope")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_page_can_tell_the_server_the_limits_and_the_defaults_of_the_library()
    {
        var meta = await Get("/api/meta");

        meta["defaults"]!["maxResults"]!.GetValue<int>().ShouldBe(new LuiQueryOptions().MaxResults);
        meta["defaults"]!["candidates"]!.GetValue<int>().ShouldBe(500);
        meta["limits"]!["take"]!.GetValue<int>().ShouldBe(LuiQueryOptions.MaxResultsLimit);
        meta["rank"]!.AsArray().Select(name => name!.GetValue<string>()).ShouldBe(Enum.GetNames<LuiRankingOrder>());
        meta["priorityMode"]!.AsArray().Count.ShouldBe(Enum.GetNames<LuiPriorityMode>().Length);
        meta["similarity"]!.AsArray().Count.ShouldBe(4);
    }

    [Fact]
    public async Task An_embedded_entry_is_written_with_its_payload_found_changed_and_removed()
    {
        // the payload arrives as the page speaks it, camelCase — and must not turn into an empty record
        var key = (await Post("/api/indexes/recipes/entries", new { key = 700001, text = "Zimtschnecke Testrezept", weight = 5, payload = new { category = "Gebäck", minutes = 25, emoji = "🥐" } }))["key"]!.GetValue<long>();
        key.ShouldBe(700001);

        var found = (await Suggest("recipes", "zimtschnecke"))["results"]![0]!;
        found["text"]!.GetValue<string>().ShouldBe("Zimtschnecke Testrezept");
        found["payload"]!["category"]!.GetValue<string>().ShouldBe("Gebäck");
        found["payload"]!["minutes"]!.GetValue<int>().ShouldBe(25);

        (await Client.PatchAsJsonAsync("/api/indexes/recipes/entries/700001", new { weight = 7777, priority = "Elevated" })).StatusCode.ShouldBe(HttpStatusCode.OK);
        var changed = (await Suggest("recipes", "zimtschnecke"))["results"]![0]!;
        changed["weight"]!.GetValue<int>().ShouldBe(7777);
        changed["priority"]!.GetValue<string>().ShouldBe("Elevated");

        (await Client.DeleteAsync("/api/indexes/recipes/entries/700001")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        Texts(await Suggest("recipes", "zimtschnecke")).ShouldBeEmpty();
        (await Client.DeleteAsync("/api/indexes/recipes/entries/700001")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_payload_for_an_index_that_stores_none_is_a_400()
    {
        var response = await Client.PostAsJsonAsync("/api/indexes/names/entries", new { text = "Name mit Payload", payload = new { x = 1 } });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain("stores no payload");
    }

    [Fact]
    public async Task Sync_against_the_seed_removes_what_is_not_in_it_and_check_against_the_seed_sees_the_difference_first()
    {
        await Post("/api/indexes/names/entries", new { key = 700002, text = "Nur Im Index" });

        var check = await Get("/api/indexes/names/check?source=seed");
        check["report"]!["extra"]!.GetValue<long>().ShouldBe(1);
        check["report"]!["isHealthy"]!.GetValue<bool>().ShouldBeFalse();

        var sync = await Post("/api/indexes/names/sync");
        sync["report"]!["removed"]!.GetValue<long>().ShouldBe(1);
        Texts(await Suggest("names", "nur im index")).ShouldBeEmpty();
        (await Get("/api/indexes/names/check"))["report"]!["isHealthy"]!.GetValue<bool>().ShouldBeTrue();
    }

    [Fact]
    public async Task The_maintenance_surface_of_an_embedded_index_answers()
    {
        foreach (var path in new[] { "/api/indexes/recipes/rebuild", "/api/indexes/recipes/optimize", "/api/indexes/recipes/typos/warm-up", "/api/indexes/recipes/typos/rebuild" })
        {
            var result = await Post(path);
            result["operation"]!.GetValue<string>().ShouldNotBeNullOrEmpty(path);
            result["elapsedMs"]!.GetValue<double>().ShouldBeGreaterThanOrEqualTo(0);
        }

        (await Get("/api/indexes/recipes/check"))["report"]!["isHealthy"]!.GetValue<bool>().ShouldBeTrue();
        (await Get("/api/indexes/recipes"))["typoDictionaryState"]!.GetValue<string>().ShouldBe("Current");
    }

    [Fact]
    public async Task A_row_written_to_the_application_s_table_reaches_the_linked_index_through_the_triggers_and_the_outbox()
    {
        var id = (await Post("/api/indexes/tags/rows", new { name = "Outboxtest", kind = "Test", color = "#123456", usageCount = 9 }))["id"]!.GetValue<long>();

        // the triggers noted the key; applying the outbox brings the row into the index (the host does it every two seconds)
        var applied = await Post("/api/indexes/tags/apply-pending");
        applied["report"]!.GetValue<int>().ShouldBeGreaterThanOrEqualTo(1);
        var hit = (await Suggest("tags", "outboxt"))["results"]![0]!;
        hit["text"]!.GetValue<string>().ShouldBe("Outboxtest");
        hit["payload"]!["color"]!.GetValue<string>().ShouldBe("#123456");

        (await Client.DeleteAsync($"/api/indexes/tags/rows/{id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await Post("/api/indexes/tags/apply-pending");
        Texts(await Suggest("tags", "outboxt")).ShouldBeEmpty();
    }

    [Fact]
    public async Task Without_triggers_only_Refresh_tells_the_linked_index_and_the_application_decides_about_the_triggers()
    {
        await Post("/api/indexes/tags/triggers", new { create = false });
        (await Get("/api/indexes/tags"))["triggers"]!.GetValue<string>().ShouldBe("None");

        var id = (await Post("/api/indexes/tags/rows", new { name = "Ohnetrigger", kind = "Test" }))["id"]!.GetValue<long>();
        (await Post("/api/indexes/tags/apply-pending"))["report"]!.GetValue<int>().ShouldBe(0, "nothing was noted");
        Texts(await Suggest("tags", "ohnetrig")).ShouldBeEmpty("the index does not know the row yet");

        (await Post("/api/indexes/tags/refresh", new { keys = new[] { id } }))["report"]!.GetValue<int>().ShouldBe(1);
        Texts(await Suggest("tags", "ohnetrig")).ShouldBe(["Ohnetrigger"]);

        await Post("/api/indexes/tags/triggers", new { create = true });
        (await Get("/api/indexes/tags"))["triggers"]!.GetValue<string>().ShouldBe("Current");
        await Client.DeleteAsync($"/api/indexes/tags/rows/{id}");
        await Post("/api/indexes/tags/apply-pending");
    }

    [Fact]
    public async Task The_sync_of_a_linked_index_rebuilds_it_from_the_table()
    {
        var sync = await Post("/api/indexes/tags/sync");

        sync["report"]!["indexed"]!.GetValue<long>().ShouldBeGreaterThanOrEqualTo(25);
        (await Get("/api/indexes/tags/check"))["report"]!["isHealthy"]!.GetValue<bool>().ShouldBeTrue();
    }

    [Fact]
    public async Task The_write_routes_of_the_wrong_kind_are_a_409_that_says_where_to_write()
    {
        var entries = await Client.PostAsJsonAsync("/api/indexes/tags/entries", new { text = "x" });
        entries.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await entries.Content.ReadAsStringAsync()).ShouldContain("/rows");

        var rows = await Client.PostAsJsonAsync("/api/indexes/recipes/rows", new { name = "x" });
        rows.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await rows.Content.ReadAsStringAsync()).ShouldContain("/entries");
    }

    [Theory]
    [InlineData("""{"name":""}""")]
    [InlineData("""{"name":"ok","color":"red"}""")]
    [InlineData("""{"name":"ok","rank":9}""")]
    public async Task A_row_the_application_would_not_write_is_a_400(string body)
    {
        var response = await Client.PostAsync("/api/indexes/tags/rows", new StringContent(body, System.Text.Encoding.UTF8, "application/json"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    [Trait("Category", "Timing")]
    public async Task A_request_the_page_abandons_stops_the_work_of_the_server()
    {
        // issue #94: the test cancelled after a fixed 150 ms. On a loaded machine the request had not reached the route
        // by then, the server never saw an abort, and the window ran out — a red test with no fault. Now the page
        // abandons the request once the server has it; the deadlines only stop a hang.
        var stats = host.Services.GetRequiredService<RequestStats>();
        var started = stats.Started;
        var aborted = stats.Aborted;

        using var cancellation = new CancellationTokenSource();
        var request = Client.GetAsync("/api/suggest/recipes?q=apfel&delayMs=1500", cancellation.Token);

        await WaitUntil(() => stats.Started > started, "the server has the request");
        await cancellation.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(() => request);

        // the server notices the closed connection and ends the request; its counter says so
        await WaitUntil(() => stats.Aborted > aborted, "the server ended the request it no longer has to answer");
    }

    [Fact]
    public async Task The_page_is_served_with_a_policy_that_has_no_room_for_inline_script_or_style()
    {
        var response = await Client.GetAsync("/");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var policy = response.Headers.GetValues("Content-Security-Policy").Single();
        policy.ShouldContain("script-src 'self'");
        policy.ShouldContain("style-src 'self'");
        policy.ShouldNotContain("unsafe-inline");
        policy.ShouldNotContain("unsafe-eval");
        response.Headers.GetValues("X-Content-Type-Options").Single().ShouldBe("nosniff");
        (await response.Content.ReadAsStringAsync()).ShouldContain("/js/main.js");
    }

    [Theory]
    [InlineData("/js/main.js", "text/javascript")]
    [InlineData("/js/vendor/flexdatalist.es.js", "text/javascript")]
    [InlineData("/css/flexdatalist.css", "text/css")]
    public async Task The_static_client_is_served(string path, string mediaType)
    {
        var response = await Client.GetAsync(path);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe(mediaType);
    }

    /// <summary>
    /// Waits until a claim of the server holds. The deadline of thirty seconds only stops a hang: a claim that holds
    /// returns at once, and a loaded runner gets its time (issue #78).
    /// </summary>
    private static async Task WaitUntil(Func<bool> claim, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!claim())
        {
            DateTime.UtcNow.ShouldBeLessThan(deadline, $"{what} — within thirty seconds");
            await Task.Delay(20, TestContext.Current.CancellationToken).ConfigureAwait(false);
        }
    }
}
