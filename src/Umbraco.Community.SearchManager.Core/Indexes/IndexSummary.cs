using System.Text.Json.Serialization;

namespace Umbraco.Community.SearchManager.Core.Indexes;

/// <summary>
/// The health of an index as the Indexes view shows it. The vocabulary is Umbraco Search's
/// (<c>Healthy</c>, <c>Rebuilding</c>, <c>Corrupted</c>, <c>Empty</c>, <c>Unknown</c>) plus core
/// Examine's <c>Unhealthy</c> (the index opens but reports a problem) and the package's own
/// <c>Incomplete</c> (the last rebuild failed part-way).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<IndexHealth>))]
public enum IndexHealth
{
    /// <summary>The index can be read and holds documents.</summary>
    Healthy,

    /// <summary>A rebuild is running.</summary>
    Rebuilding,

    /// <summary>The index opens but its diagnostics report a problem.</summary>
    Unhealthy,

    /// <summary>Reading the index threw: it is probably corrupt or locked.</summary>
    Corrupted,

    /// <summary>The index can be read but holds no documents.</summary>
    Empty,

    /// <summary>The last rebuild failed part-way, so the index holds only some documents.</summary>
    Incomplete,

    /// <summary>The source cannot tell.</summary>
    Unknown,
}

/// <summary>What a source can do with one of its indexes. The UI hides or explains what is missing.</summary>
/// <param name="Rebuild">The index can be rebuilt from Search Manager.</param>
/// <param name="Search">The index can be searched with the portable query.</param>
/// <param name="NativeQuery">The index accepts raw Lucene queries.</param>
/// <param name="TermStatistics">Per-field term statistics can be read (it is a Lucene index).</param>
public sealed record IndexCapabilities(
    bool Rebuild,
    bool Search,
    bool NativeQuery,
    bool TermStatistics
);

/// <summary>One index as the Indexes view lists it.</summary>
/// <param name="Name">The index name (Examine) or alias (Umbraco Search); unique across sources.</param>
/// <param name="Engine">The source that owns it, for example <c>examine</c> or <c>umbraco-search</c>.</param>
/// <param name="Description">What the index holds, when the source knows; otherwise null.</param>
/// <param name="Health">The index's health.</param>
/// <param name="HealthMessage">The source's explanation for an unhealthy, corrupted or unknown index.</param>
/// <param name="DocumentCount">Documents in the index, or null when the source cannot count them.</param>
/// <param name="DeletedDocumentCount">Deleted documents still in the Lucene segments, or null when unknown.</param>
/// <param name="FieldCount">Distinct field names, or null when unknown.</param>
/// <param name="SizeInBytes">Size of the index files on disk, or null when unknown.</param>
/// <param name="Capabilities">What Search Manager can do with the index.</param>
/// <param name="RebuildUnavailableReason">Why <see cref="IndexCapabilities.Rebuild"/> is missing, when it is.</param>
/// <param name="ZeroDowntime">Whether a rebuild builds a fresh copy and swaps it in, so search keeps working.</param>
/// <param name="IsMemberIndex">Whether the index holds member data, which needs Members section access.</param>
/// <param name="Properties">The provider's own settings (analyzer, directory, ...), shown in the details panel.</param>
/// <param name="FailedRebuild">
/// The failed rebuild that left the index <see cref="IndexHealth.Incomplete"/>; null for any other health.
/// </param>
public sealed record IndexSummary(
    string Name,
    string Engine,
    string? Description,
    IndexHealth Health,
    string? HealthMessage,
    long? DocumentCount,
    long? DeletedDocumentCount,
    int? FieldCount,
    long? SizeInBytes,
    IndexCapabilities Capabilities,
    string? RebuildUnavailableReason,
    bool ZeroDowntime,
    bool IsMemberIndex,
    IReadOnlyDictionary<string, string> Properties,
    FailedRebuildSummary? FailedRebuild = null
);

/// <summary>The rebuild that failed part-way and left an index incomplete.</summary>
/// <param name="EndedAt">When it failed.</param>
/// <param name="Error">Why it failed, when known.</param>
/// <param name="DocumentsWritten">Documents in the index when it failed, when known.</param>
public sealed record FailedRebuildSummary(
    DateTimeOffset EndedAt,
    string? Error,
    long? DocumentsWritten
);
