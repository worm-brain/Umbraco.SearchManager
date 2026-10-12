using Microsoft.Extensions.Logging;

namespace Umbraco.Community.SearchManager.Features.Indexes;

/// <summary>Log messages for listing indexes (source-generated, as the analyzers require).</summary>
internal static partial class IndexLog
{
    /// <summary>Reading an index's diagnostics threw, so it is listed as corrupted.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="exception">What was thrown.</param>
    /// <param name="indexName">The index.</param>
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Could not read diagnostics for index {IndexName}; listing it as corrupted"
    )]
    public static partial void DiagnosticsFailed(
        this ILogger logger,
        Exception exception,
        string indexName
    );

    /// <summary>A Lucene statistic could not be read, so it is shown as unknown.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="exception">What was thrown.</param>
    /// <param name="statistic">Which statistic.</param>
    /// <param name="indexName">The index.</param>
    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Could not read {Statistic} for index {IndexName}"
    )]
    public static partial void StatisticFailed(
        this ILogger logger,
        Exception exception,
        string statistic,
        string indexName
    );
}
