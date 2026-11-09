using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Nordmann.LUI;
using Shouldly;

namespace Nordmann.LUI.Sample.Tests;

/// <summary>
/// The bulk indexes of the sample (ADR-0029): the button that creates an index and loads it, the progress of the load, and
/// what the index holds afterwards — exactly the number of entries that were asked for, and nothing else.
/// </summary>
public sealed class SampleBulkTests(SampleHost host) : IClassFixture<SampleHost>
{
    private HttpClient Client => host.Client;

    private async Task<JsonNode> Get(string path)
    {
        var response = await Client.GetAsync(path).ConfigureAwait(false);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync().ConfigureAwait(false));
        return (await response.Content.ReadFromJsonAsync<JsonNode>().ConfigureAwait(false))!;
    }

    private async Task<JsonNode> Suggest(string index, string query) =>
        await Get($"/api/suggest/{index}?q={Uri.EscapeDataString(query)}").ConfigureAwait(false);

    private static string[] Texts(JsonNode result) => [.. result["results"]!.AsArray().Select(hit => hit!["text"]!.GetValue<string>())];

    [Fact]
    public async Task The_three_bulk_indexes_are_listed_with_their_source_and_their_ceiling()
    {
        var indexes = (await Get("/api/indexes")).AsArray()
            .ToDictionary(index => index!["name"]!.GetValue<string>(), index => index!, StringComparer.Ordinal);

        foreach (var name in new[] { "words", "people", "hashtags" })
        {
            indexes[name].ShouldNotBeNull(name);
            indexes[name]!["source"]!.GetValue<string>().ShouldNotBeNullOrEmpty(name);
            indexes[name]!["storage"]!.GetValue<string>().ShouldBe("Embedded");
            indexes[name]!["payload"]!.GetValue<string>().ShouldBe("none");
        }

        indexes["words"]!["capacity"]!.GetValue<long>().ShouldBeGreaterThan(0, "the word list holds words");
        indexes["people"]!["capacity"].ShouldBeNull("Bogus has no ceiling");
        indexes["people"]!["typoTolerance"]!.GetValue<bool>().ShouldBeTrue("a name is what a user misspells");
        indexes["hashtags"]!["typoTolerance"]!.GetValue<bool>().ShouldBeFalse("a dictionary of a million tags costs 140 MB");
    }

    [Fact]
    public async Task A_fill_loads_exactly_the_number_of_entries_that_were_asked_for()
    {
        var status = await Fill("people", 120);

        status["read"]!.GetValue<long>().ShouldBe(120);
        // what was already there stays — the entries are the same ones, so a fill adds only what is missing
        (status["added"]!.GetValue<long>() + status["unchanged"]!.GetValue<long>()).ShouldBe(120);
        (await Get("/api/indexes/people"))["count"]!.GetValue<long>().ShouldBe(120);
    }

    [Fact]
    public async Task A_fill_with_fewer_entries_removes_the_rest()
    {
        await Fill("people", 120);
        var status = await Fill("people", 40);

        status["removed"]!.GetValue<long>().ShouldBe(80);
        (await Get("/api/indexes/people"))["count"]!.GetValue<long>().ShouldBe(40);
    }

    [Fact]
    public async Task A_filled_index_answers_the_query_with_its_entries()
    {
        await Fill("hashtags", 50);

        var texts = Texts(await Suggest("hashtags", "Gehrkeburg"));

        texts.ShouldContain(text => text.StartsWith("#Gehrkeburg", StringComparison.Ordinal), $"[{string.Join(" | ", texts)}]");
    }

    [Fact]
    public async Task The_names_of_the_locale_are_the_entries_of_the_people_index()
    {
        await Fill("people", 50);

        var texts = Texts(await Suggest("people", "Gehrke"));

        texts.ShouldContain(text => text.Contains("Gehrke", StringComparison.Ordinal), $"[{string.Join(" | ", texts)}]");
    }

    [Fact]
    public async Task The_word_list_fills_the_words_index()
    {
        await Fill("words", 100);

        (await Get("/api/indexes/words"))["count"]!.GetValue<long>().ShouldBe(100);
        var texts = Texts(await Suggest("words", "aa"));
        texts.ShouldContain(text => text.StartsWith("aa", StringComparison.OrdinalIgnoreCase), $"[{string.Join(" | ", texts)}]");
    }

    [Fact]
    public async Task A_number_the_index_cannot_hold_is_a_400_that_says_why()
    {
        var none = await Client.PostAsJsonAsync("/api/indexes/people/fill", new { count = 0 });
        none.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await none.Content.ReadAsStringAsync()).ShouldContain("at least one");

        var tooMany = await Client.PostAsJsonAsync("/api/indexes/words/fill", new { count = 3_000_001 });
        tooMany.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await tooMany.Content.ReadAsStringAsync()).ShouldContain("largest fill");

        var beyondTheList = await Client.PostAsJsonAsync("/api/indexes/words/fill", new { count = 1_000_000 });
        beyondTheList.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await beyondTheList.Content.ReadAsStringAsync()).ShouldContain("holds");
    }

    [Fact]
    public void The_bulk_indexes_are_not_registered_at_startup()
    {
        // They are created when they are asked for — a stock of a million entries is minutes of work, and nobody pays for
        // it who does not want it (ADR-0029).
        host.Services.GetKeyedService<ILuiEmbeddedIndex>("words").ShouldBeNull();
        host.Services.GetKeyedService<ILuiEmbeddedIndex>("people").ShouldBeNull();
        host.Services.GetKeyedService<ILuiEmbeddedIndex>("hashtags").ShouldBeNull();
    }

    [Fact]
    public async Task The_curated_indexes_are_not_filled_by_the_button()
    {
        var response = await Client.PostAsJsonAsync("/api/indexes/recipes/fill", new { count = 10 });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).ShouldContain("not a bulk index");
    }

    [Fact]
    public async Task The_cancel_route_answers_for_a_bulk_index()
    {
        var response = await Client.PostAsync("/api/indexes/hashtags/fill/cancel", null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<JsonNode>())!["cancelled"]!.GetValue<bool>().ShouldBeOneOf(true, false);
    }

    /// <summary>Starts a fill and waits for it: the fill runs in the background of the sample (ADR-0029).</summary>
    private async Task<JsonNode> Fill(string index, int count)
    {
        var response = await Client.PostAsJsonAsync($"/api/indexes/{index}/fill", new { count }).ConfigureAwait(false);
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync().ConfigureAwait(false));

        var started = (await response.Content.ReadFromJsonAsync<JsonNode>().ConfigureAwait(false))!;
        started["state"]!.GetValue<string>().ShouldBeOneOf("running", "done");

        for (var attempt = 0; attempt < 600; attempt++)
        {
            var status = await Get($"/api/indexes/{index}/fill").ConfigureAwait(false);
            var state = status["state"]!.GetValue<string>();

            if (string.Equals(state, "done", StringComparison.Ordinal))
            {
                return status;
            }

            if (state is "failed" or "cancelled")
            {
                throw new InvalidOperationException($"The fill of '{index}' ended: {status}");
            }

            await Task.Delay(100).ConfigureAwait(false);
        }

        throw new InvalidOperationException($"The fill of '{index}' did not finish within a minute.");
    }
}
