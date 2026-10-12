using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Models;
using Umbraco.Community.SearchManager.Core.Rebuilds;
using IndexOptions = Umbraco.Cms.Search.Core.Configuration.IndexOptions;

namespace Umbraco.Community.SearchManager.UmbracoSearch.Indexes;

/// <summary>
/// Adds Umbraco Search rebuilds that Search Manager did not start (Umbraco Search's own UI, a
/// content type change, another server's share of a distributed rebuild) to the rebuild history,
/// with the trigger <see cref="RebuildTrigger.Other"/>.
/// </summary>
/// <remarks>
/// Umbraco Search announces a start and an end on the server that runs the rebuild, never a
/// failure, so every recorded run is a success; a rebuild already going when the site started is
/// not recorded, because its start was missed.
/// </remarks>
/// <param name="history">The persisted history.</param>
/// <param name="indexOptions">Umbraco Search's index registrations, to tell member indexes apart.</param>
/// <param name="timeProvider">Supplies the start and end times.</param>
/// <param name="logger">Logs a history that cannot be written.</param>
internal sealed class UmbracoSearchExternalRebuildRecorder(
    IRebuildHistory history,
    IOptions<IndexOptions> indexOptions,
    TimeProvider timeProvider,
    ILogger<UmbracoSearchExternalRebuildRecorder> logger
)
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _started = new(
        StringComparer.OrdinalIgnoreCase
    );

    /// <summary>Notes when an unwatched rebuild started.</summary>
    /// <param name="indexAlias">The alias.</param>
    public void OnStarting(string indexAlias) => _started[indexAlias] = timeProvider.GetUtcNow();

    /// <summary>Records an unwatched rebuild that has finished, when its start was seen.</summary>
    /// <param name="indexAlias">The alias.</param>
    public void OnCompleted(string indexAlias)
    {
        if (!_started.TryRemove(indexAlias, out DateTimeOffset startedAt))
        {
            return;
        }

        bool isMemberIndex = indexOptions
            .Value.GetContentIndexRegistrations()
            .Any(registration =>
                string.Equals(
                    registration.IndexAlias,
                    indexAlias,
                    StringComparison.OrdinalIgnoreCase
                ) && registration.ContainedObjectTypes.Contains(UmbracoObjectTypes.Member)
            );

        try
        {
            history.Add(
                new RebuildHistoryEntry(
                    indexAlias,
                    UmbracoSearchIndexSource.EngineId,
                    Environment.MachineName,
                    RebuildTrigger.Other,
                    TriggeredBy: null,
                    startedAt,
                    timeProvider.GetUtcNow(),
                    RebuildStatus.Succeeded,
                    DocumentsBefore: null,
                    DocumentsAfter: null,
                    Error: null,
                    isMemberIndex
                )
            );
        }
#pragma warning disable CA1031 // Runs inside Umbraco Search's notification; a history failure is only logged.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            logger.HistoryNotRecorded(exception, indexAlias);
        }
    }
}
