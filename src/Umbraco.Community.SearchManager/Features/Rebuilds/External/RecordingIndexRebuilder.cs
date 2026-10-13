using System.Collections.Concurrent;
using Examine;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Runtime;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Infrastructure.Examine;
using Umbraco.Cms.Infrastructure.Models;
using Umbraco.Community.SearchManager.Core.Rebuilds;

namespace Umbraco.Community.SearchManager.Features.Rebuilds.External;

/// <summary>
/// Decorates Umbraco's <see cref="IIndexRebuilder"/>, which every core Examine rebuild goes
/// through (the startup rebuild, the core dashboard, language changes, other packages), so the
/// rebuilds Search Manager did not start are added to the history (ADR 0022).
/// </summary>
/// <remarks>
/// <para>
/// Calls pass straight through when Search Manager made them (<see cref="SearchManagerRebuildScope"/>),
/// when Umbraco would refuse them (not the main domain or not running), or when the index is
/// already rebuilding: those either record themselves or never run.
/// </para>
/// <para>
/// Otherwise the decorator runs the rebuild itself: it asks the inner rebuilder for a foreground
/// rebuild between <see cref="CoreRebuildRecorder.Begin"/> and <see cref="CoreRebuildRecorder.End"/>.
/// A background request returns success at once and does that on a thread pool thread, which is
/// what Umbraco does too. The cost: on Umbraco 17.3 and later the inner foreground rebuild cannot be
/// cancelled at shutdown, only its start delay can (accepted in ADR 0022).
/// </para>
/// <para>
/// A rebuild of several indexes with a start delay is the startup rebuild: Umbraco asks for it once,
/// one minute after the site starts, and nothing else in Umbraco 17 or 18 delays a multi-index rebuild.
/// </para>
/// </remarks>
/// <param name="inner">Umbraco's rebuilder.</param>
/// <param name="recorder">Tracks and records the runs.</param>
/// <param name="examineManager">Finds the indexes.</param>
/// <param name="mainDom">With <paramref name="runtimeState"/>, whether Umbraco may rebuild on this server.</param>
/// <param name="runtimeState">The runtime level.</param>
/// <param name="lifetime">Cancels a pending start delay at shutdown.</param>
/// <param name="logger">Logs background failures.</param>
internal sealed class RecordingIndexRebuilder(
    IIndexRebuilder inner,
    CoreRebuildRecorder recorder,
    IExamineManager examineManager,
    IMainDom mainDom,
    IRuntimeState runtimeState,
    IHostApplicationLifetime lifetime,
    ILogger<RecordingIndexRebuilder> logger
) : IIndexRebuilder
{
    private readonly ConcurrentDictionary<Task, byte> _background = new();

    /// <summary>The rebuilder this one decorates.</summary>
    public IIndexRebuilder Inner => inner;

    /// <summary>
    /// Whether the inner rebuilder was built with <see cref="RecordingIndexPopulator"/>s, so a
    /// populator failure in a rebuild of several indexes is recorded.
    /// </summary>
    public bool PopulatorsWrapped { get; set; }

    /// <inheritdoc />
    public bool CanRebuild(string indexName) => inner.CanRebuild(indexName);

    /// <inheritdoc />
    /// <remarks>
    /// Also true while a tracked run is going, which makes Umbraco's startup rebuild visible per
    /// index on Umbraco 17.0 to 17.2, where the inner rebuilder only reports single-index rebuilds.
    /// </remarks>
    public async Task<bool> IsRebuildingAsync(string indexName) =>
        recorder.IsTracking(indexName) || await inner.IsRebuildingAsync(indexName);

#pragma warning disable CS0618, CS0672 // The obsolete members must still be implemented and decorated.
    /// <inheritdoc />
    [Obsolete("Use RebuildIndexAsync() instead.")]
    public void RebuildIndex(
        string indexName,
        TimeSpan? delay = null,
        bool useBackgroundThread = true
    ) => RebuildIndexAsync(indexName, delay, useBackgroundThread).GetAwaiter().GetResult();

    /// <inheritdoc />
    [Obsolete("Use RebuildIndexesAsync() instead.")]
    public void RebuildIndexes(
        bool onlyEmptyIndexes,
        TimeSpan? delay = null,
        bool useBackgroundThread = true
    ) => RebuildIndexesAsync(onlyEmptyIndexes, delay, useBackgroundThread).GetAwaiter().GetResult();
#pragma warning restore CS0618, CS0672

    /// <inheritdoc />
    public async Task<Attempt<IndexRebuildResult>> RebuildIndexAsync(
        string indexName,
        TimeSpan? delay = null,
        bool useBackgroundThread = true
    )
    {
        if (
            SearchManagerRebuildScope.IsActive
            || !CanRun()
            || !examineManager.TryGetIndex(indexName, out IIndex? index)
            || !SafeCanRebuild(indexName)
            || await IsRebuildingAsync(indexName)
        )
        {
            return await inner.RebuildIndexAsync(indexName, delay, useBackgroundThread);
        }

        if (useBackgroundThread)
        {
            RunInBackground(() => RebuildOneAsync(index, delay, recheck: true));
            return Attempt.Succeed(IndexRebuildResult.Success);
        }

        return await RebuildOneAsync(index, delay, recheck: false);
    }

    /// <inheritdoc />
    public async Task<Attempt<IndexRebuildResult>> RebuildIndexesAsync(
        bool onlyEmptyIndexes,
        TimeSpan? delay = null,
        bool useBackgroundThread = true
    )
    {
        if (SearchManagerRebuildScope.IsActive || !CanRun())
        {
            return await inner.RebuildIndexesAsync(onlyEmptyIndexes, delay, useBackgroundThread);
        }

        RebuildTrigger trigger =
            delay > TimeSpan.Zero && recorder.TryClaimStartup()
                ? RebuildTrigger.Startup
                : RebuildTrigger.Other;

        if (useBackgroundThread)
        {
            RunInBackground(() => RebuildManyAsync(onlyEmptyIndexes, delay, trigger));
            return Attempt.Succeed(IndexRebuildResult.Success);
        }

        return await RebuildManyAsync(onlyEmptyIndexes, delay, trigger);
    }

    /// <summary>Waits for every background rebuild started so far. For tests.</summary>
    /// <returns>A task that completes once they have.</returns>
    internal Task WhenBackgroundIdleAsync() => Task.WhenAll(_background.Keys);

    /// <summary>Rebuilds one index in the foreground, tracked.</summary>
    /// <param name="index">The index.</param>
    /// <param name="delay">Waited before starting.</param>
    /// <param name="recheck">
    /// Check again after the delay that the index is not already rebuilding (a background run;
    /// Umbraco skips a rebuild that finds one going).
    /// </param>
    /// <returns>The inner rebuilder's answer.</returns>
    private async Task<Attempt<IndexRebuildResult>> RebuildOneAsync(
        IIndex index,
        TimeSpan? delay,
        bool recheck
    )
    {
        await DelayAsync(delay);
        if (recheck && await IsRebuildingAsync(index.Name))
        {
            return Attempt.Fail(IndexRebuildResult.AlreadyRebuilding);
        }

        if (!recorder.Begin(index, RebuildTrigger.Other))
        {
            return await inner.RebuildIndexAsync(index.Name, TimeSpan.Zero, false);
        }

        Attempt<IndexRebuildResult> attempt;
        try
        {
            attempt = await inner.RebuildIndexAsync(index.Name, TimeSpan.Zero, false);
        }
        catch (Exception exception)
        {
            recorder.End(index.Name, Unwrap(exception));
            throw;
        }

        if (attempt.Success)
        {
            recorder.End(index.Name, null);
        }
        else
        {
            recorder.Discard(index.Name);
        }

        return attempt;
    }

    /// <summary>
    /// Rebuilds every rebuildable index (or only the empty ones) in the foreground, tracking the
    /// indexes Umbraco will rebuild.
    /// </summary>
    /// <param name="onlyEmptyIndexes">Only indexes that are missing or hold no documents.</param>
    /// <param name="delay">Waited before starting.</param>
    /// <param name="trigger">What started it.</param>
    /// <returns>The inner rebuilder's answer.</returns>
    private async Task<Attempt<IndexRebuildResult>> RebuildManyAsync(
        bool onlyEmptyIndexes,
        TimeSpan? delay,
        RebuildTrigger trigger
    )
    {
        await DelayAsync(delay);

        // The set Umbraco will rebuild, chosen as it chooses it, minus the indexes already rebuilding.
        var tracked = new List<IIndex>();
        foreach (
            IIndex index in examineManager.Indexes.Where(index =>
                SafeCanRebuild(index.Name) && (!onlyEmptyIndexes || IsEmpty(index))
            )
        )
        {
            if (!await IsRebuildingAsync(index.Name) && recorder.Begin(index, trigger))
            {
                tracked.Add(index);
            }
        }

        Attempt<IndexRebuildResult> attempt;
        try
        {
            attempt = await inner.RebuildIndexesAsync(onlyEmptyIndexes, TimeSpan.Zero, false);
        }
        catch (Exception exception)
        {
            tracked.ForEach(index => recorder.End(index.Name, Unwrap(exception)));
            throw;
        }

        foreach (IIndex index in tracked)
        {
            if (attempt.Success)
            {
                recorder.End(index.Name, null);
            }
            else
            {
                recorder.Discard(index.Name);
            }
        }

        return attempt;
    }

    /// <summary>
    /// Starts work on the thread pool without the caller's execution context: Umbraco's
    /// long-running operations refuse to run in the foreground under an ambient scope, and the
    /// caller's <see cref="SearchManagerRebuildScope"/> must not follow either. Failures are logged.
    /// </summary>
    /// <param name="work">The work.</param>
    private void RunInBackground(Func<Task> work)
    {
        Task task;
        using (ExecutionContext.SuppressFlow())
        {
            task = Task.Run(async () =>
            {
                try
                {
                    await work();
                }
                catch (OperationCanceledException)
                    when (lifetime.ApplicationStopping.IsCancellationRequested)
                {
                    // Shut down during the start delay: nothing started, nothing to record.
                }
#pragma warning disable CA1031 // A background rebuild has no caller to throw to; it is recorded and logged.
                catch (Exception exception)
#pragma warning restore CA1031
                {
                    logger.ExternalRebuildFailed(exception);
                }
            });
        }

        _background.TryAdd(task, 0);
        _ = task.ContinueWith(
            finished => _background.TryRemove(finished, out _),
            TaskScheduler.Default
        );
    }

    /// <summary>Waits out a start delay; shutdown cancels it.</summary>
    /// <param name="delay">The delay, or null for none.</param>
    /// <returns>A task that completes after the delay.</returns>
    private Task DelayAsync(TimeSpan? delay) =>
        delay > TimeSpan.Zero
            ? Task.Delay(delay.Value, lifetime.ApplicationStopping)
            : Task.CompletedTask;

    /// <summary>Umbraco's own rule for whether rebuilds may run on this server.</summary>
    /// <returns><see langword="true"/> on the main domain of a running site.</returns>
    private bool CanRun() => mainDom.IsMainDom && runtimeState.Level == RuntimeLevel.Run;

    /// <summary>Whether the index has a populator; an index the rebuilder does not know cannot be rebuilt.</summary>
    /// <param name="indexName">The index.</param>
    /// <returns><see langword="true"/> when it can be rebuilt.</returns>
    private bool SafeCanRebuild(string indexName)
    {
        try
        {
            return inner.CanRebuild(indexName);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>
    /// Umbraco's rule for "empty" in a rebuild of empty indexes: missing, or holding no documents.
    /// An index that cannot be read is not rebuilt.
    /// </summary>
    /// <param name="index">The index.</param>
    /// <returns><see langword="true"/> when Umbraco would rebuild it.</returns>
    private static bool IsEmpty(IIndex index)
    {
        try
        {
            return !index.IndexExists()
                || (index is IIndexStats stats && stats.GetDocumentCount() == 0);
        }
#pragma warning disable CA1031 // Umbraco skips an index it cannot read; so does the tracking.
        catch (Exception)
#pragma warning restore CA1031
        {
            return false;
        }
    }

    /// <summary>
    /// Umbraco 17.0 to 17.2 run a foreground rebuild through a long-running operation that wraps
    /// the populator's exception in an <see cref="AggregateException"/>.
    /// </summary>
    /// <param name="exception">What the rebuilder threw.</param>
    /// <returns>The exception to record.</returns>
    private static Exception Unwrap(Exception exception) =>
        exception is AggregateException { InnerException: { } innerException }
            ? innerException
            : exception;
}
