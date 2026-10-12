using Microsoft.Extensions.Logging;

namespace Umbraco.Community.SearchManager.UmbracoSearch.Indexes;

/// <summary>Log messages for listing Umbraco Search indexes (source-generated, as the analyzers require).</summary>
internal static partial class UmbracoSearchIndexLog
{
    /// <summary>Umbraco Search's metadata for an index threw, so it is listed as corrupted.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="exception">What was thrown.</param>
    /// <param name="indexAlias">The Umbraco Search alias.</param>
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Could not read Umbraco Search metadata for index {IndexAlias}; listing it as corrupted"
    )]
    public static partial void MetadataFailed(
        this ILogger logger,
        Exception exception,
        string indexAlias
    );

    /// <summary>The physical index behind an Umbraco Search index could not be read.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="exception">What was thrown.</param>
    /// <param name="physicalIndex">The physical Examine index name.</param>
    /// <param name="indexAlias">The Umbraco Search alias.</param>
    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Could not read physical index {PhysicalIndex} behind Umbraco Search index {IndexAlias}"
    )]
    public static partial void PhysicalIndexFailed(
        this ILogger logger,
        Exception exception,
        string physicalIndex,
        string indexAlias
    );

    /// <summary>A rebuild started outside Search Manager could not be added to the history.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="exception">Why.</param>
    /// <param name="indexAlias">The Umbraco Search alias.</param>
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Search Manager could not record the rebuild of Umbraco Search index {IndexAlias} in its history"
    )]
    public static partial void HistoryNotRecorded(
        this ILogger logger,
        Exception exception,
        string indexAlias
    );
}
