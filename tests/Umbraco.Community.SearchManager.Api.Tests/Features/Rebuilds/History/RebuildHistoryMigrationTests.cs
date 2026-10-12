using Umbraco.Community.SearchManager.Features.Rebuilds.History;

namespace Umbraco.Community.SearchManager.Api.Tests.Features.Rebuilds.History;

public sealed class RebuildHistoryMigrationTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task RunAsync_EmptyDatabase_CreatesTheHistoryTable()
    {
        // Act
        await new AddRebuildHistoryTable(_database.MigrationContext()).RunAsync();

        // Assert
        Assert.Contains(RebuildHistoryDto.TableName, TableNames());
    }

    [Fact]
    public async Task RunAsync_TableAlreadyThere_LeavesItAlone()
    {
        // Arrange
        await new AddRebuildHistoryTable(_database.MigrationContext()).RunAsync();

        // Act
        Task again = new AddRebuildHistoryTable(_database.MigrationContext()).RunAsync();
        await again;

        // Assert
        Assert.True(again.IsCompletedSuccessfully);
    }

    [Fact]
    public void Plan_EndsWithTheHistoryTable()
    {
        // Act
        var plan = new SearchManagerMigrationPlan();

        // Assert
        Assert.Equal(SearchManagerMigrationPlan.RebuildHistoryState, plan.FinalState);
    }

    private IEnumerable<string> TableNames() =>
        _database.SqlContext.SqlSyntax.GetTablesInSchema(_database.Database);
}
