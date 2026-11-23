using Nordmann.LUI.Indexes;
using Nordmann.LUI.Schema;

namespace Nordmann.LUI;

/// <summary>
/// One call of <see cref="LuiCatalog.Initialize"/> (ADR-0076): the steps in LUI's order, <b>step by step across the selected indexes</b>, each only
/// where the state of the index needs it, read first. Every step goes through the operations by name of the catalog (<c>Maintain</c>) — the runtime of the
/// stored definition with the guard of every operation (ADR-0075) — never through a parallel path, so a dropped or recreated index is the proper exception
/// here too.
/// <list type="number">
/// <item>the triggers of every linked index whose triggers are missing or stale (idempotent, ADR-0077) — <b>first</b>, so that a row written before the
/// sync is noted;</item>
/// <item>the first sync of every linked index that still needs one, one after the other, through the lease of #271: a lease that another process holds is
/// <see cref="LuiInitializationNeeds.InitialSyncRunningElsewhere"/> — skipped, not waited for, not an error;</item>
/// <item>the outbox of every linked index that has rows in it, once;</item>
/// <item>the typo dictionaries, <b>last</b>, so that they know the words of the syncs and the outbox, and <b>one after the other</b>, so that the peak memory
/// is that of the largest dictionary and not the sum — and only for an index that has no file step left.</item>
/// </list>
/// An index that fails records the failing step and gets no later one; the others go on. A cancelled call keeps what it finished. A catalog opened read-only
/// builds the dictionaries and leaves the file steps to the writing process.
/// </summary>
internal sealed class CatalogInitialization(LuiCatalog catalog, LuiInitializeOptions options, CancellationToken cancellationToken)
{
    private readonly bool _readOnly = catalog.IsReadOnly;
    private readonly List<IndexRun> _runs = [];

    /// <summary>Does the work.</summary>
    /// <returns>What was done.</returns>
    /// <exception cref="LuiInitializeException">An index failed — after every other index was initialized.</exception>
    internal LuiInitializeReport Run()
    {
        var started = catalog.TimeProvider.GetTimestamp();
        cancellationToken.ThrowIfCancellationRequested();

        // a definition of an index that does not exist stops the call before any step
        _runs.AddRange(SelectNames().Select(name => new IndexRun(name)));

        // 1 the triggers, 2 the first syncs, 3 the outbox — the file steps, which a catalog that may not write leaves to the writing process
        if (!_readOnly)
        {
            if (options.InstallTriggers)
            {
                EachIndex(InstallTriggers);
            }

            if (options.InitialSync)
            {
                EachIndex(RunInitialSync);
            }

            if (options.ApplyPending)
            {
                EachIndex(ApplyPending);
            }
        }

        // 4 the dictionaries, last and one after the other — what the file steps did is read again: the state of an index is not the one of the start
        if (options.WarmUpTypoDictionaries)
        {
            foreach (var run in _runs)
            {
                run.State = null;
            }

            EachIndex(BuildTypoDictionary);
        }

        var lines = _runs.Select(Finish).ToArray();
        var report = new LuiInitializeReport { Indexes = lines, Elapsed = catalog.TimeProvider.GetElapsedTime(started) };
        var errors = _runs.Where(run => run.Error is not null).ToDictionary(run => run.Name, run => run.Error!, StringComparer.Ordinal);
        return errors.Count == 0 ? report : throw new LuiInitializeException(report, errors);
    }

    /// <summary>The indexes of the run, in the order of the names — the ones the definitions name, or every index of the catalog.</summary>
    private List<string> SelectNames()
    {
        if (options.Indexes is null)
        {
            _ = catalog.StoredIndexes.ReloadIfChanged();
            return [.. catalog.StoredIndexes.Current.Entries.Keys.Order(StringComparer.Ordinal)];
        }

        var names = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var definition in options.Indexes)
        {
            ArgumentNullException.ThrowIfNull(definition, nameof(options));
            _ = names.Add(Identifier.ValidateIndexName(definition.Name, nameof(options)));
        }

        foreach (var name in names)
        {
            // only the names are used: the stored definition is the index's, and the call does not compare it with the declared one
            _ = catalog.StoredIndexes.Find(name, out _) ?? throw new LuiIndexNotFoundException(name);
        }

        return [.. names];
    }

    // ---- the steps --------------------------------------------------------------------------------------------------

    private void InstallTriggers(IndexRun run) =>
        Attempt(
            run,
            LuiInitializeStep.Triggers,
            () =>
            {
                if (Read(run) is not { File: { } file } || file.Triggers == LuiTriggerState.Current)
                {
                    return;
                }

                Announce(run, LuiInitializeStep.Triggers);
                _ = catalog.Maintain(run.Name, runtime =>
                {
                    ((LinkedIndex)runtime.Index).CreateTriggers();
                    return true;
                });
                run.TriggersInstalled = true;
            });

    private void RunInitialSync(IndexRun run) =>
        Attempt(
            run,
            LuiInitializeStep.InitialSync,
            () =>
            {
                if (Read(run) is not { File: { NeedsInitialSync: true } file })
                {
                    return;
                }

                // a lease that is held: another sync completes it, and this call neither waits nor runs one of its own — and the steps after it for this index stay undone
                if (file.SyncRunning)
                {
                    run.RunningElsewhere = true;
                    return;
                }

                Announce(run, LuiInitializeStep.InitialSync);
                try
                {
                    run.Sync = catalog.Maintain(run.Name, runtime => ((LinkedIndex)runtime.Index).SyncWhenNeeded(SyncOptionsOf(run), cancellationToken));
                }
                catch (LuiSyncInProgressException exception) when (exception.Owner is not null)
                {
                    // the lease was taken between the read and the take, or taken over from this run: the sync of someone else is running
                    run.RunningElsewhere = true;
                }
            });

    private void ApplyPending(IndexRun run) =>
        Attempt(
            run,
            LuiInitializeStep.Outbox,
            () =>
            {
                if (Read(run) is not { File: not null })
                {
                    return;
                }

                if (catalog.Maintain(run.Name, runtime => ((LinkedIndex)runtime.Index).Pending()) == 0)
                {
                    return;
                }

                Announce(run, LuiInitializeStep.Outbox);
                run.PendingApplied += catalog.Maintain(run.Name, runtime => ((LinkedIndex)runtime.Index).ApplyPending(cancellationToken));
            });

    private void BuildTypoDictionary(IndexRun run) =>
        Attempt(
            run,
            LuiInitializeStep.TypoDictionary,
            () =>
            {
                if (Read(run) is not { } state || state.Distance == LuiTypoDistance.None || state.DictionaryState == LuiTypoDictionaryState.Current)
                {
                    return;
                }

                // an index that has a file step left gets no dictionary yet: it would miss the words of a sync still to come. The reason stays, the next call builds it.
                // A catalog that may not write has the file steps left by definition, and builds what it can
                if (!_readOnly && state.File is { Triggers: not LuiTriggerState.Current } or { NeedsInitialSync: true })
                {
                    return;
                }

                Announce(run, LuiInitializeStep.TypoDictionary);
                _ = catalog.Maintain(run.Name, runtime =>
                {
                    runtime.Index.WarmUpTypoDictionary(cancellationToken);
                    return true;
                });
                run.TypoDictionaryBuilt = true;
            });

    /// <summary>One step of the run for each index, one after the other, in the order of the names; an index that failed or waits for a sync elsewhere gets none.</summary>
    private void EachIndex(Action<IndexRun> step)
    {
        foreach (var run in _runs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (run.Error is null && !run.RunningElsewhere)
            {
                step(run);
            }
        }
    }

    /// <summary>
    /// Runs one step of an index: an error ends the steps of that index, and is the error of the call at its end — a cancellation of the call is not an error of the index and
    /// ends the call. The time of the step is the time of the index.
    /// </summary>
    private void Attempt(IndexRun run, LuiInitializeStep step, Action work)
    {
        var began = catalog.TimeProvider.GetTimestamp();
        try
        {
            work();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // an index that fails does not stop the others: its error is kept and thrown, with the report, at the end of the call
        catch (Exception exception)
#pragma warning restore CA1031
        {
            run.Fail(step, exception);
        }
        finally
        {
            run.Elapsed += catalog.TimeProvider.GetElapsedTime(began);
        }
    }

    // ---- the state --------------------------------------------------------------------------------------------------

    /// <summary>
    /// What the index says now: read from the file with the guard of every operation, and kept until a step has changed it. A failure to read it is the failure of the
    /// index, at <see cref="LuiInitializeStep.ReadState"/>.
    /// </summary>
    private IndexState? Read(IndexRun run)
    {
        if (run.State is { } known)
        {
            return known;
        }

        try
        {
            run.State = catalog.Maintain(run.Name, runtime =>
            {
                var linked = runtime.Index as LinkedIndex;
                return new IndexState(runtime.Index.TypoDistance, runtime.Index.TypoDictionaryState, linked?.ReadInitializationState());
            });
            return run.State;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // see Attempt
        catch (Exception exception)
#pragma warning restore CA1031
        {
            run.Fail(LuiInitializeStep.ReadState, exception);
            return null;
        }
    }

    private void Announce(IndexRun run, LuiInitializeStep step)
    {
        // the step changes the state that was read
        run.State = null;
        options.Progress?.Report(new LuiInitializeProgress { IndexName = run.Name, Step = step });
    }

    /// <summary>The options of the first sync of an index: the options of the call, and its progress told to <see cref="LuiInitializeOptions.Progress"/> as well.</summary>
    private LuiSyncOptions SyncOptionsOf(IndexRun run)
    {
        var syncOptions = options.Sync ?? new LuiSyncOptions();
        return syncOptions with
        {
            Progress = new ForwardingProgress(
                syncOptions.Progress,
                report => options.Progress?.Report(new LuiInitializeProgress { IndexName = run.Name, Step = LuiInitializeStep.InitialSync, Sync = report })),
        };
    }

    /// <summary>The line of an index in the report: what ran, and — read again from the state — what is left.</summary>
    private LuiInitializeIndexReport Finish(IndexRun run)
    {
        var began = catalog.TimeProvider.GetTimestamp();
        var left = LuiInitializationNeeds.None;
        if (run.State is null && Peek(run) is { } state)
        {
            run.State = state;
        }

        if (run.State is { } read)
        {
            left = read.Needs;
        }

        return new LuiInitializeIndexReport
        {
            Name = run.Name,
            TriggersInstalled = run.TriggersInstalled,
            Sync = run.Sync,
            PendingApplied = run.PendingApplied,
            TypoDictionaryBuilt = run.TypoDictionaryBuilt,
            Skipped = left,
            Failed = run.Error is not null,
            FailedStep = run.FailedStep,
            Elapsed = run.Elapsed + catalog.TimeProvider.GetElapsedTime(began),
        };
    }

    /// <summary>The state at the end of the call. It cannot make a failed index worse, and it is the failure of an index that was well until now.</summary>
    private IndexState? Peek(IndexRun run)
    {
        if (run.Error is null)
        {
            return Read(run);
        }

        try
        {
            return catalog.Maintain(run.Name, runtime =>
                new IndexState(runtime.Index.TypoDistance, runtime.Index.TypoDictionaryState, (runtime.Index as LinkedIndex)?.ReadInitializationState()));
        }
#pragma warning disable CA1031 // the index failed already; what is left of it is not known now
        catch (Exception exception) when (exception is not OperationCanceledException)
#pragma warning restore CA1031
        {
            return null;
        }
    }

    /// <summary>What the call reads of an index: how it forgives typos, where its dictionary stands in this process, and — a linked index — the state of the file.</summary>
    private sealed record IndexState(LuiTypoDistance Distance, LuiTypoDictionaryState DictionaryState, LinkedInitialization? File)
    {
        /// <summary>What the index still needs: the dictionary of this process, and the triggers and the first sync of a linked index.</summary>
        internal LuiInitializationNeeds Needs =>
            (Distance != LuiTypoDistance.None && DictionaryState != LuiTypoDictionaryState.Current ? LuiInitializationNeeds.TypoDictionary : LuiInitializationNeeds.None)
            | (File?.Needs ?? LuiInitializationNeeds.None);
    }

    /// <summary>What happened to one index in the call.</summary>
    private sealed class IndexRun(string name)
    {
        internal string Name { get; } = name;

        internal IndexState? State { get; set; }

        internal bool TriggersInstalled { get; set; }

        internal LuiLinkedSyncReport? Sync { get; set; }

        internal int PendingApplied { get; set; }

        internal bool TypoDictionaryBuilt { get; set; }

        internal bool RunningElsewhere { get; set; }

        internal TimeSpan Elapsed { get; set; }

        internal Exception? Error { get; private set; }

        internal LuiInitializeStep? FailedStep { get; private set; }

        /// <summary>Keeps the first failure of the index: later steps do not run, so it is the only one.</summary>
        internal void Fail(LuiInitializeStep step, Exception exception)
        {
            if (Error is not null)
            {
                return;
            }

            Error = exception;
            FailedStep = step;
        }
    }

    /// <summary>Tells the progress of the sync to the options of the call and to the report of <see cref="LuiCatalog.Initialize"/>.</summary>
    private sealed class ForwardingProgress(IProgress<LuiSyncReport>? first, Action<LuiSyncReport> second) : IProgress<LuiSyncReport>
    {
        public void Report(LuiSyncReport value)
        {
            first?.Report(value);
            second(value);
        }
    }
}
