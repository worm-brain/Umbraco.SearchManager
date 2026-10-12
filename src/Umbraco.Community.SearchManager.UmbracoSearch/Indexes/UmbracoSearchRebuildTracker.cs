using System.Collections.Concurrent;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Search.Core.Notifications;

namespace Umbraco.Community.SearchManager.UmbracoSearch.Indexes;

/// <summary>
/// Turns Umbraco Search's rebuild notifications on this server into tasks a rebuild can await.
/// Umbraco Search runs a rebuild on a background queue and only announces it through
/// <see cref="IndexRebuildStartingNotification"/> and <see cref="IndexRebuildCompletedNotification"/>.
/// </summary>
internal sealed class UmbracoSearchRebuildTracker
{
    private readonly ConcurrentDictionary<string, RebuildWatch> _watches = new(
        StringComparer.OrdinalIgnoreCase
    );

    /// <summary>
    /// Starts watching <paramref name="indexAlias"/>. Call before asking Umbraco Search to rebuild,
    /// so neither notification is missed; dispose the watch when done.
    /// </summary>
    /// <param name="indexAlias">The Umbraco Search alias.</param>
    /// <returns>The watch.</returns>
    public RebuildWatch Watch(string indexAlias)
    {
        var watch = new RebuildWatch(indexAlias, this);
        _watches[indexAlias] = watch;
        return watch;
    }

    /// <summary>Records that Umbraco Search started rebuilding an index on this server.</summary>
    /// <param name="indexAlias">The alias.</param>
    public void OnStarting(string indexAlias)
    {
        if (_watches.TryGetValue(indexAlias, out RebuildWatch? watch))
        {
            watch.StartedSource.TrySetResult();
        }
    }

    /// <summary>Records that Umbraco Search finished rebuilding an index on this server.</summary>
    /// <param name="indexAlias">The alias.</param>
    public void OnCompleted(string indexAlias)
    {
        if (_watches.TryGetValue(indexAlias, out RebuildWatch? watch))
        {
            watch.StartedSource.TrySetResult();
            watch.CompletedSource.TrySetResult();
        }
    }

    /// <summary>Stops watching, unless a newer watch replaced this one.</summary>
    /// <param name="watch">The watch.</param>
    private void Remove(RebuildWatch watch) =>
        _watches.TryRemove(new KeyValuePair<string, RebuildWatch>(watch.IndexAlias, watch));

    /// <summary>One rebuild being watched.</summary>
    /// <param name="indexAlias">The alias.</param>
    /// <param name="tracker">The tracker it belongs to.</param>
    internal sealed class RebuildWatch(string indexAlias, UmbracoSearchRebuildTracker tracker)
        : IDisposable
    {
        /// <summary>The alias watched.</summary>
        public string IndexAlias { get; } = indexAlias;

        /// <summary>Completed by the starting notification.</summary>
        internal TaskCompletionSource StartedSource { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Completed by the completed notification.</summary>
        internal TaskCompletionSource CompletedSource { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Completes when the rebuild has started on this server.</summary>
        public Task Started => StartedSource.Task;

        /// <summary>Completes when the rebuild has finished on this server.</summary>
        public Task Completed => CompletedSource.Task;

        /// <summary>Stops watching.</summary>
        public void Dispose() => tracker.Remove(this);
    }
}

/// <summary>Forwards Umbraco Search's rebuild notifications to <see cref="UmbracoSearchRebuildTracker"/>.</summary>
/// <param name="tracker">The tracker.</param>
internal sealed class UmbracoSearchRebuildNotificationHandler(UmbracoSearchRebuildTracker tracker)
    : INotificationHandler<IndexRebuildStartingNotification>,
        INotificationHandler<IndexRebuildCompletedNotification>
{
    /// <inheritdoc />
    public void Handle(IndexRebuildStartingNotification notification) =>
        tracker.OnStarting(notification.IndexAlias);

    /// <inheritdoc />
    public void Handle(IndexRebuildCompletedNotification notification) =>
        tracker.OnCompleted(notification.IndexAlias);
}
