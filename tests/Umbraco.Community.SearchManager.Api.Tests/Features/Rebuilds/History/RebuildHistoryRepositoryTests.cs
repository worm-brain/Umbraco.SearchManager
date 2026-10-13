using Umbraco.Community.SearchManager.Core.Rebuilds;
using Umbraco.Community.SearchManager.Features.Rebuilds.History;

namespace Umbraco.Community.SearchManager.Api.Tests.Features.Rebuilds.History;

public sealed class RebuildHistoryRepositoryTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Start = new(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);

    private readonly SqliteTestDatabase _database = new();
    private RebuildHistoryRepository _repository = null!;

    public async ValueTask InitializeAsync()
    {
        await new AddRebuildHistoryTable(_database.MigrationContext()).RunAsync();
        _repository = new RebuildHistoryRepository(_database.ScopeProvider());
    }

    public ValueTask DisposeAsync()
    {
        _database.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public void Add_FailedRun_IsReadBackWithEveryField()
    {
        // Arrange
        RebuildHistoryEntry entry = Entry("ProductIndex", 0) with
        {
            Outcome = RebuildStatus.Failed,
            Error = "Lock obtain timed out",
            DocumentsAfter = null,
            IsMemberIndex = true,
        };

        // Act
        _repository.Add(entry);

        // Assert
        Assert.Equal(entry, Assert.Single(_repository.GetRecent("ProductIndex")));
    }

    [Fact]
    public void GetRecent_SeveralRuns_ListsThatIndexNewestFirst()
    {
        // Arrange
        _repository.Add(Entry("ProductIndex", 0));
        _repository.Add(Entry("ExternalIndex", 1));
        _repository.Add(Entry("ProductIndex", 2));

        // Act
        IReadOnlyList<RebuildHistoryEntry> recent = _repository.GetRecent("ProductIndex");

        // Assert
        Assert.Equal([Start.AddHours(2), Start], recent.Select(entry => entry.StartedAt));
    }

    [Fact]
    public void Add_MoreThanTheKeptRuns_PrunesTheOldest()
    {
        // Arrange
        for (int hour = 0; hour < IRebuildHistory.RunsKeptPerIndex; hour++)
        {
            _repository.Add(Entry("ProductIndex", hour));
        }

        // Act
        _repository.Add(Entry("ProductIndex", IRebuildHistory.RunsKeptPerIndex));

        // Assert
        IReadOnlyList<RebuildHistoryEntry> recent = _repository.GetRecent("ProductIndex");
        Assert.Equal(
            (IRebuildHistory.RunsKeptPerIndex, Start.AddHours(1)),
            (recent.Count, recent[^1].StartedAt)
        );
    }

    [Fact]
    public void Add_PruningOneIndex_KeepsTheOtherIndexesRuns()
    {
        // Arrange
        _repository.Add(Entry("ExternalIndex", 0));

        // Act
        for (int hour = 0; hour <= IRebuildHistory.RunsKeptPerIndex; hour++)
        {
            _repository.Add(Entry("ProductIndex", hour));
        }

        // Assert
        Assert.Single(_repository.GetRecent("ExternalIndex"));
    }

    [Fact]
    public void GetLatestPerIndex_SeveralRuns_ReturnsTheNewestOfEachIndex()
    {
        // Arrange
        _repository.Add(Entry("ProductIndex", 0));
        _repository.Add(Entry("ExternalIndex", 1));
        _repository.Add(Entry("ProductIndex", 2));

        // Act
        IReadOnlyList<RebuildHistoryEntry> latest = _repository.GetLatestPerIndex();

        // Assert
        Assert.Equal(
            [("ExternalIndex", Start.AddHours(1)), ("ProductIndex", Start.AddHours(2))],
            latest
                .Select(entry => (entry.IndexName, entry.StartedAt))
                .OrderBy(pair => pair.IndexName)
        );
    }

    [Fact]
    public void GetLatestPerIndex_EmptyTable_ReturnsNothing()
    {
        // Act
        IReadOnlyList<RebuildHistoryEntry> latest = _repository.GetLatestPerIndex();

        // Assert
        Assert.Empty(latest);
    }

    [Fact]
    public void Add_StartupRun_IsReadBackWithTheStartupTrigger()
    {
        // Arrange
        RebuildHistoryEntry entry = Entry("ExternalIndex", 0) with
        {
            Trigger = RebuildTrigger.Startup,
            TriggeredBy = null,
        };

        // Act
        _repository.Add(entry);

        // Assert
        Assert.Equal(
            RebuildTrigger.Startup,
            Assert.Single(_repository.GetRecent("ExternalIndex")).Trigger
        );
    }

    private static RebuildHistoryEntry Entry(string indexName, int hour) =>
        new(
            indexName,
            "examine",
            "WEB01",
            RebuildTrigger.User,
            "Jack",
            Start.AddHours(hour),
            Start.AddHours(hour).AddSeconds(42),
            RebuildStatus.Succeeded,
            100,
            120,
            null,
            false
        );
}
