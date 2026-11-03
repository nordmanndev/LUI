using Microsoft.Extensions.DependencyInjection;
using Nordmann.LUI.Hosting;
using Nordmann.LUI.Schema;

namespace Nordmann.LUI;

/// <summary>
/// Adds the indexes of the catalog that <c>AddLui</c> registered (LUI-Spezifikation §12.7). Every index is a keyed
/// singleton under its name — as its read interface, which a suggest endpoint needs, and as its write interface:
/// <code>
/// public sealed class SuggestEndpoint([FromKeyedServices("recipes")] ILuiIndex&lt;long, RecipeChip&gt; recipes) { … }
/// </code>
/// The description is the one of <c>lui.Index(name)</c>; it runs when the index is first needed — on start, unless
/// <see cref="EnsureOnStart"/> is switched off.
/// </summary>
public sealed class LuiServicesBuilder
{
    private readonly LuiRegistrations _registrations;

    internal LuiServicesBuilder(IServiceCollection services, LuiRegistrations registrations)
    {
        Services = services;
        _registrations = registrations;
    }

    /// <summary>The services of the application.</summary>
    public IServiceCollection Services { get; }

    /// <summary>
    /// Adds an index of LUI's own (<c>Owned</c>): <c>AddIndex("recipes", index =&gt; index.MultiWord().WithPayload(…))</c>.
    /// Registered under <paramref name="name"/> as <see cref="ILuiIndex{TKey, TPayload}"/> and
    /// <see cref="ILuiOwnedIndex{TKey, TPayload}"/>.
    /// </summary>
    /// <typeparam name="TKey">The key of the application.</typeparam>
    /// <typeparam name="TPayload">The payload of a hit.</typeparam>
    /// <param name="name">The name of the index — and the key of its services.</param>
    /// <param name="index">Describes the index, from its kind on.</param>
    /// <returns>The builder.</returns>
    /// <exception cref="ArgumentException">The name is not a valid index name, or it is registered already.</exception>
    public LuiServicesBuilder AddIndex<TKey, TPayload>(string name, Func<LuiIndexKindBuilder, LuiIndexBuilder<TKey, TPayload>> index)
    {
        Identifier.ValidateIndexName(name, nameof(name));
        ArgumentNullException.ThrowIfNull(index);
        var typos = new TypoWish();

        _registrations.Add(new IndexRegistration(
            name,
            Ensure: provider => provider.GetRequiredKeyedService<ILuiOwnedIndex<TKey, TPayload>>(name),
            WarmUp: (provider, token) =>
            {
                // only an index that asked for typo tolerance: the dictionary of another one would cost memory for nothing
                if (provider.GetRequiredKeyedService<ILuiOwnedIndex<TKey, TPayload>>(name) is var built && typos.Wanted)
                {
                    built.WarmUpTypoDictionary(token);
                }
            },
            ApplyPending: null,
            ApplyPendingEvery: null));

        Services.AddKeyedSingleton<ILuiOwnedIndex<TKey, TPayload>>(name, (provider, _) =>
        {
            var described = index(provider.GetRequiredService<LuiCatalog>().Index(name));
            typos.Wanted = described.TypoToleranceSetting;
            return described.Ensure();
        });
        Services.AddKeyedSingleton<ILuiIndex<TKey, TPayload>>(
            name, (provider, _) => provider.GetRequiredKeyedService<ILuiOwnedIndex<TKey, TPayload>>(name));
        return this;
    }

    /// <summary>
    /// Adds an index over a table of the application (<c>Linked</c>):
    /// <c>AddIndex("tags", index =&gt; index.SingleWord().LinkTo("tag", "id", "name"), applyPendingEvery: TimeSpan.FromSeconds(5))</c>.
    /// Registered under <paramref name="name"/> as <see cref="ILuiIndex{TKey, TPayload}"/> (with a <see cref="long"/> key)
    /// and <see cref="ILuiLinkedIndex{TPayload}"/>.
    /// </summary>
    /// <typeparam name="TPayload">The payload of a hit, read from columns of the table.</typeparam>
    /// <param name="name">The name of the index — and the key of its services.</param>
    /// <param name="index">Describes the index, from its kind on.</param>
    /// <param name="applyPendingEvery">
    /// For an index with triggers: how often a hosted service brings the rows they noted into the index
    /// (<c>ApplyPending()</c>). <see langword="null"/>, the default: never — the application calls it. The triggers are
    /// not created here; that stays the application's call (ADR-0015).
    /// </param>
    /// <returns>The builder.</returns>
    /// <exception cref="ArgumentException">The name is not a valid index name, or it is registered already.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="applyPendingEvery"/> is not positive.</exception>
    public LuiServicesBuilder AddIndex<TPayload>(
        string name,
        Func<LuiIndexKindBuilder, LuiLinkedIndexBuilder<TPayload>> index,
        TimeSpan? applyPendingEvery = null)
    {
        Identifier.ValidateIndexName(name, nameof(name));
        ArgumentNullException.ThrowIfNull(index);
        if (applyPendingEvery is { } every && every <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(applyPendingEvery), every, "The interval of ApplyPending is positive.");
        }

        var typos = new TypoWish();
        _registrations.Add(new IndexRegistration(
            name,
            Ensure: provider => provider.GetRequiredKeyedService<ILuiLinkedIndex<TPayload>>(name),
            WarmUp: (provider, token) =>
            {
                // only an index that asked for typo tolerance: the dictionary of another one would cost memory for nothing
                if (provider.GetRequiredKeyedService<ILuiLinkedIndex<TPayload>>(name) is var built && typos.Wanted)
                {
                    built.WarmUpTypoDictionary(token);
                }
            },
            ApplyPending: (provider, token) => provider.GetRequiredKeyedService<ILuiLinkedIndex<TPayload>>(name).ApplyPending(token),
            ApplyPendingEvery: applyPendingEvery));

        Services.AddKeyedSingleton<ILuiLinkedIndex<TPayload>>(name, (provider, _) =>
        {
            var described = index(provider.GetRequiredService<LuiCatalog>().Index(name));
            typos.Wanted = described.TypoToleranceSetting;
            return described.Ensure();
        });
        Services.AddKeyedSingleton<ILuiIndex<long, TPayload>>(
            name, (provider, _) => provider.GetRequiredKeyedService<ILuiLinkedIndex<TPayload>>(name));
        return this;
    }

    /// <summary>
    /// Whether every index is ensured when the host starts (default: on). Off, an index is ensured when it is first
    /// resolved — a definition that does not match then fails that request, not the start.
    /// </summary>
    /// <param name="enabled">Whether the indexes are ensured on start.</param>
    /// <returns>The builder.</returns>
    public LuiServicesBuilder EnsureOnStart(bool enabled = true)
    {
        _registrations.EnsureOnStart = enabled;
        return this;
    }

    /// <summary>
    /// Builds the typo dictionary of every index with typo tolerance in the background once the host has started
    /// (default: off) — about 5 seconds and 140 MB for 685,000 words each, paid on start instead of by the first typo.
    /// Queries do not wait for it; an index without typo tolerance gets no dictionary.
    /// </summary>
    /// <param name="enabled">Whether the dictionaries are built on start.</param>
    /// <returns>The builder.</returns>
    public LuiServicesBuilder WarmUpTypoDictionariesOnStart(bool enabled = true)
    {
        _registrations.WarmUpTypoDictionariesOnStart = enabled;
        return this;
    }

    /// <summary>Whether the description of an index asked for typo tolerance — known once it was described.</summary>
    private sealed class TypoWish
    {
        internal bool Wanted { get; set; }
    }
}
