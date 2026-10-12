using Microsoft.Extensions.Logging;
using Umbraco.Community.SearchManager.Core.Rebuilds;

namespace Umbraco.Community.SearchManager.Features.Rebuilds;

/// <summary>Log messages for rebuild runs (source-generated, as the analyzers require).</summary>
internal static partial class RebuildLog
{
    /// <summary>A queued rebuild is starting.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="indexName">The index.</param>
    /// <param name="triggeredBy">Who asked for it.</param>
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Search Manager is rebuilding index {IndexName} (requested by {TriggeredBy})"
    )]
    public static partial void RebuildStarting(
        this ILogger logger,
        string indexName,
        string triggeredBy
    );

    /// <summary>A rebuild failed.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="exception">Why.</param>
    /// <param name="indexName">The index.</param>
    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Search Manager could not rebuild index {IndexName}"
    )]
    public static partial void RebuildFailed(
        this ILogger logger,
        Exception exception,
        string indexName
    );

    /// <summary>A rebuild run ended.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="indexName">The index.</param>
    /// <param name="status">How it ended.</param>
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Search Manager rebuild of index {IndexName} ended: {Status}"
    )]
    public static partial void RebuildFinished(
        this ILogger logger,
        string indexName,
        RebuildStatus status
    );

    /// <summary>A finished rebuild could not be added to the history.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="exception">Why.</param>
    /// <param name="indexName">The index.</param>
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Search Manager could not record the rebuild of index {IndexName} in its history"
    )]
    public static partial void HistoryNotRecorded(
        this ILogger logger,
        Exception exception,
        string indexName
    );

    /// <summary>A rebuild server event could not be sent.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="exception">Why.</param>
    /// <param name="eventType">The event type.</param>
    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Search Manager could not send the {EventType} server event"
    )]
    public static partial void RebuildEventNotSent(
        this ILogger logger,
        Exception exception,
        string eventType
    );
}
