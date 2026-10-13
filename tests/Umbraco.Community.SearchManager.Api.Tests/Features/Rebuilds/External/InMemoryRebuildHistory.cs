using Umbraco.Community.SearchManager.Core.Rebuilds;

namespace Umbraco.Community.SearchManager.Api.Tests.Features.Rebuilds.External;

/// <summary>A rebuild history in memory, newest last; set <see cref="AddThrows"/> to fail writes.</summary>
internal sealed class InMemoryRebuildHistory : IRebuildHistory
{
    private readonly List<RebuildHistoryEntry> _entries = [];

    /// <summary>Thrown by <see cref="Add"/> when set.</summary>
    public Exception? AddThrows { get; set; }

    /// <summary>Every entry added, oldest first.</summary>
    public IReadOnlyList<RebuildHistoryEntry> Entries
    {
        get
        {
            lock (_entries)
            {
                return [.. _entries];
            }
        }
    }

    public void Add(RebuildHistoryEntry entry)
    {
        if (AddThrows is not null)
        {
            throw AddThrows;
        }

        lock (_entries)
        {
            _entries.Add(entry);
        }
    }

    public IReadOnlyList<RebuildHistoryEntry> GetRecent(string indexName) =>
        [.. Entries.Where(entry => entry.IndexName == indexName).Reverse()];

    public IReadOnlyList<RebuildHistoryEntry> GetLatestPerIndex() =>
        [.. Entries.GroupBy(entry => entry.IndexName).Select(group => group.Last())];
}
