using System.Text.Json.Serialization;

namespace Umbraco.Community.SearchManager.Core.Rebuilds;

/// <summary>What started a rebuild recorded in the history.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<RebuildTrigger>))]
public enum RebuildTrigger
{
    /// <summary>A backoffice user, through Search Manager; the entry names the user.</summary>
    User,

    /// <summary>
    /// Something outside Search Manager (Umbraco Search's own UI, the core Examine dashboard, a
    /// language change, another package); the entry names no user.
    /// </summary>
    Other,

    /// <summary>
    /// Umbraco's own rebuild of empty (or, on a cold boot, all) core Examine indexes shortly after
    /// the site started; the entry names no user.
    /// </summary>
    Startup,
}

/// <summary>One finished rebuild of one index, as the history keeps it.</summary>
/// <param name="IndexName">The index.</param>
/// <param name="Engine">The engine that owns it.</param>
/// <param name="Server">The machine the rebuild ran on.</param>
/// <param name="Trigger">What started it.</param>
/// <param name="TriggeredBy">The user's name, for <see cref="RebuildTrigger.User"/>.</param>
/// <param name="StartedAt">When it started.</param>
/// <param name="EndedAt">When it ended.</param>
/// <param name="Outcome"><see cref="RebuildStatus.Succeeded"/> or <see cref="RebuildStatus.Failed"/>.</param>
/// <param name="DocumentsBefore">Documents in the index before, when known.</param>
/// <param name="DocumentsAfter">Documents in the index after, when known.</param>
/// <param name="Error">Why a failed rebuild failed.</param>
/// <param name="IsMemberIndex">Whether the index holds member data (only shown with Members access).</param>
public sealed record RebuildHistoryEntry(
    string IndexName,
    string Engine,
    string Server,
    RebuildTrigger Trigger,
    string? TriggeredBy,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    RebuildStatus Outcome,
    long? DocumentsBefore,
    long? DocumentsAfter,
    string? Error,
    bool IsMemberIndex
);

/// <summary>
/// The persisted rebuild history: the last <see cref="RunsKeptPerIndex"/> finished rebuilds of each
/// index, kept across restarts.
/// </summary>
public interface IRebuildHistory
{
    /// <summary>How many runs are kept per index; older ones are pruned as new ones are added.</summary>
    const int RunsKeptPerIndex = 20;

    /// <summary>Records a finished rebuild and prunes the index's runs beyond <see cref="RunsKeptPerIndex"/>.</summary>
    /// <param name="entry">The rebuild.</param>
    void Add(RebuildHistoryEntry entry);

    /// <summary>Lists an index's recorded rebuilds, newest first.</summary>
    /// <param name="indexName">The index, as <c>IndexSummary.Name</c> gives it.</param>
    /// <returns>At most <see cref="RunsKeptPerIndex"/> entries.</returns>
    IReadOnlyList<RebuildHistoryEntry> GetRecent(string indexName);

    /// <summary>Lists the newest recorded rebuild of every index that has one.</summary>
    /// <param name="server">
    /// Only consider rebuilds that ran on this machine (as <see cref="RebuildHistoryEntry.Server"/>
    /// records it); null considers every server.
    /// </param>
    /// <returns>One entry per index.</returns>
    IReadOnlyList<RebuildHistoryEntry> GetLatestPerIndex(string? server = null);
}
