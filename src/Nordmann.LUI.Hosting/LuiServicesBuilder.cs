using Microsoft.Extensions.DependencyInjection;
using Nordmann.LUI.Hosting;

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
    /// Adds an index of LUI's own (<c>Embedded</c>): <c>AddIndex("recipes", index =&gt; index.MultiWord().WithPayload(…))</c>.
    /// Registered under <paramref name="name"/> as <see cref="ILuiIndex{TKey, TPayload}"/> and
    /// <see cref="ILuiEmbeddedIndex{TKey, TPayload}"/>.
    /// </summary>
    /// <typeparam name="TKey">The key of the application.</typeparam>
    /// <typeparam name="TPayload">The payload of a hit.</typeparam>
    /// <param name="name">The name of the index — and the key of its services.</param>
    /// <param name="index">Describes the index, from its kind on.</param>
    /// <returns>The builder.</returns>
    /// <exception cref="ArgumentException">The name is empty, or it is registered already. A name that is not a valid index
    /// name (<c>^[a-z][a-z0-9_]{0,40}$</c>) fails when the index is ensured — on start.</exception>
    public LuiServicesBuilder AddIndex<TKey, TPayload>(string name, Func<LuiIndexKindBuilder, LuiEmbeddedIndexBuilder<TKey, TPayload>> index)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(index);

        _registrations.Add(new IndexRegistration(
            name,
            Ensure: provider => provider.GetRequiredKeyedService<ILuiEmbeddedIndex<TKey, TPayload>>(name),
            WarmUp: (provider, token) =>
            {
                var index = provider.GetRequiredKeyedService<ILuiEmbeddedIndex<TKey, TPayload>>(name);
                return WarmUp(index.TypoTolerance, index.WarmUpTypoDictionary, token);
            },
            RebuildIfStale: (provider, token) =>
            {
                var index = provider.GetRequiredKeyedService<ILuiEmbeddedIndex<TKey, TPayload>>(name);
                return RebuildIfStale(index.TypoDictionaryState, index.RebuildTypoDictionary, token);
            }));

        Services.AddKeyedSingleton<ILuiEmbeddedIndex<TKey, TPayload>>(
            name, (provider, _) => index(provider.GetRequiredService<LuiCatalog>().Index(name)).Ensure());
        Services.AddKeyedSingleton<ILuiIndex<TKey, TPayload>>(
            name, (provider, _) => provider.GetRequiredKeyedService<ILuiEmbeddedIndex<TKey, TPayload>>(name));
        return this;
    }

    /// <summary>
    /// Adds an index over a table of the application (<c>Linked</c>):
    /// <c>AddLinkedIndex("tags", index =&gt; index.SingleWord().LinkTo("tag", "id", "name"), applyPendingEvery: TimeSpan.FromSeconds(5))</c>.
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
    /// <exception cref="ArgumentException">The name is empty, or it is registered already. A name that is not a valid index
    /// name (<c>^[a-z][a-z0-9_]{0,40}$</c>) fails when the index is ensured — on start.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="applyPendingEvery"/> is not positive.</exception>
    public LuiServicesBuilder AddLinkedIndex<TPayload>(
        string name,
        Func<LuiIndexKindBuilder, LuiLinkedIndexBuilder<TPayload>> index,
        TimeSpan? applyPendingEvery = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(index);
        if (applyPendingEvery is { } every && every <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(applyPendingEvery), every, "The interval of ApplyPending is positive.");
        }

        _registrations.Add(new LinkedIndexRegistration(
            name,
            Ensure: provider => provider.GetRequiredKeyedService<ILuiLinkedIndex<TPayload>>(name),
            WarmUp: (provider, token) =>
            {
                var index = provider.GetRequiredKeyedService<ILuiLinkedIndex<TPayload>>(name);
                return WarmUp(index.TypoTolerance, index.WarmUpTypoDictionary, token);
            },
            RebuildIfStale: (provider, token) =>
            {
                var index = provider.GetRequiredKeyedService<ILuiLinkedIndex<TPayload>>(name);
                return RebuildIfStale(index.TypoDictionaryState, index.RebuildTypoDictionary, token);
            },
            ApplyPending: (provider, token) => provider.GetRequiredKeyedService<ILuiLinkedIndex<TPayload>>(name).ApplyPending(token),
            ApplyPendingEvery: applyPendingEvery));

        Services.AddKeyedSingleton<ILuiLinkedIndex<TPayload>>(
            name, (provider, _) => index(provider.GetRequiredService<LuiCatalog>().Index(name)).Ensure());
        Services.AddKeyedSingleton<ILuiIndex<long, TPayload>>(
            name, (provider, _) => provider.GetRequiredKeyedService<ILuiLinkedIndex<TPayload>>(name));
        return this;
    }

    /// <summary>
    /// Whether every index is ensured when the host starts (default: on). Off, an index is ensured when it is first
    /// resolved — a definition that does not match then fails that request, not the start.
    /// <para>
    /// Hosted services start in the order of their registration, and <c>AddLui</c> registers this one: a hosted service
    /// the application adds later — a migration that creates the table of a linked index — starts after it. Register the
    /// migration before <c>AddLui</c>, run it before <c>host.Run()</c>, or switch this off.
    /// </para>
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
    /// <para>
    /// Queries without a correction do not wait for it. A query that needs a correction waits until the dictionary is
    /// built, as it would for the lazy build of the first typo; its cancellation token does not interrupt that wait.
    /// </para>
    /// <para>
    /// An index whose instance has no typo tolerance (<see cref="ILuiIndex{TKey, TPayload}.TypoTolerance"/>) is not
    /// warmed: when a query asks for a correction (<c>Query(text).TypoTolerance(true)</c>), it builds its dictionary
    /// then.
    /// </para>
    /// </summary>
    /// <param name="enabled">Whether the dictionaries are built on start.</param>
    /// <returns>The builder.</returns>
    public LuiServicesBuilder WarmUpTypoDictionariesOnStart(bool enabled = true)
    {
        _registrations.WarmUpTypoDictionariesOnStart = enabled;
        return this;
    }

    /// <summary>
    /// How often the typo dictionaries a sync left stale are rebuilt in the background (default: every minute;
    /// <see langword="null"/> switches it off). A sync of an embedded index that changed or removed entries marks a built
    /// dictionary stale instead of building it anew — the build of three million entries takes half a minute, and the
    /// sync should not pay it (ADR-0021). A stale dictionary answers meanwhile: the new words are in it, the words that
    /// went linger and find nothing. Without a host, <c>RebuildTypoDictionary()</c> does the same.
    /// </summary>
    /// <param name="every">The interval, or <see langword="null"/> for never.</param>
    /// <returns>The builder.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="every"/> is not positive.</exception>
    public LuiServicesBuilder RebuildStaleTypoDictionariesEvery(TimeSpan? every)
    {
        if (every is { } interval && interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(every), interval, "The interval of the rebuild is positive.");
        }

        _registrations.RebuildStaleTypoDictionariesEvery = every;
        return this;
    }

    /// <summary>
    /// Rebuilds the typo dictionary of an index if it is stale. The state is the read side of the index
    /// (<see cref="ILuiIndex{TKey, TPayload}.TypoDictionaryState"/>), the rebuild the write side — so both come as arguments.
    /// </summary>
    /// <returns>Whether it was rebuilt.</returns>
    private static bool RebuildIfStale(LuiTypoDictionaryState state, Action<CancellationToken> rebuild, CancellationToken cancellationToken)
    {
        if (state != LuiTypoDictionaryState.Stale)
        {
            return false;
        }

        rebuild(cancellationToken);
        return true;
    }

    /// <summary>
    /// Builds the typo dictionary of an index that asked for typo tolerance; the dictionary of another one would cost
    /// memory for nothing.
    /// </summary>
    /// <returns>Whether it was built.</returns>
    private static bool WarmUp(bool typoTolerance, Action<CancellationToken> warmUp, CancellationToken cancellationToken)
    {
        if (!typoTolerance)
        {
            return false;
        }

        warmUp(cancellationToken);
        return true;
    }
}
