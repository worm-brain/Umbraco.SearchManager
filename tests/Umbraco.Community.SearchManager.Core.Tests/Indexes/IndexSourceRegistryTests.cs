using Umbraco.Community.SearchManager.Core.Indexes;

namespace Umbraco.Community.SearchManager.Core.Tests.Indexes;

public class IndexSourceRegistryTests
{
    [Fact]
    public async Task GetIndexesAsync_IndexClaimedByAnotherSource_IsListedOnlyByItsOwner()
    {
        // Arrange
        var examine = new StubSource(
            "examine",
            claims: [],
            names: ["ExternalIndex", "Umb_Content_a"]
        );
        var search = new StubSource(
            "umbraco-search",
            claims: ["Umb_Content_a"],
            names: ["Umb_Content"]
        );
        var registry = new IndexSourceRegistry([examine, search]);

        // Act
        IReadOnlyList<IndexSummary> indexes = await registry.GetIndexesAsync(
            TestContext.Current.CancellationToken
        );

        // Assert
        Assert.Equal(["ExternalIndex", "Umb_Content"], indexes.Select(index => index.Name));
    }

    [Fact]
    public void GetSource_KnownEngine_ReturnsThatSource()
    {
        // Arrange
        var examine = new StubSource("examine", claims: [], names: []);
        var search = new StubSource("umbraco-search", claims: [], names: []);
        var registry = new IndexSourceRegistry([examine, search]);

        // Act
        IIndexSource? source = registry.GetSource("umbraco-search");

        // Assert
        Assert.Same(search, source);
    }

    [Fact]
    public void GetSource_UnknownEngine_ReturnsNull()
    {
        // Arrange
        var registry = new IndexSourceRegistry([new StubSource("examine", [], [])]);

        // Act
        IIndexSource? source = registry.GetSource("elastic");

        // Assert
        Assert.Null(source);
    }

    [Fact]
    public async Task GetIndexesAsync_NoSources_ReturnsEmptyList()
    {
        // Arrange
        var registry = new IndexSourceRegistry([]);

        // Act
        IReadOnlyList<IndexSummary> indexes = await registry.GetIndexesAsync(
            TestContext.Current.CancellationToken
        );

        // Assert
        Assert.Empty(indexes);
    }

    /// <summary>A source that lists fixed names, minus whatever other sources claim.</summary>
    private sealed class StubSource(string engine, string[] claims, string[] names) : IIndexSource
    {
        public string Engine => engine;

        public IReadOnlySet<string> ClaimedExamineIndexNames { get; } = new HashSet<string>(claims);

        public Task<IReadOnlyList<IndexSummary>> GetIndexesAsync(
            IReadOnlySet<string> claimedElsewhere,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult<IReadOnlyList<IndexSummary>>([
                .. names
                    .Where(name => !claimedElsewhere.Contains(name))
                    .Select(name => IndexSummaries.Healthy(name, engine)),
            ]);

        public Task RebuildAsync(
            string indexName,
            IProgress<long> documentsWritten,
            CancellationToken cancellationToken
        ) => Task.CompletedTask;
    }
}

/// <summary>Builds index summaries for tests, stating only what a test cares about.</summary>
internal static class IndexSummaries
{
    public static IndexSummary Healthy(
        string name,
        string engine = "examine",
        bool member = false
    ) =>
        new(
            Name: name,
            Engine: engine,
            Description: null,
            Health: IndexHealth.Healthy,
            HealthMessage: null,
            DocumentCount: 10,
            DeletedDocumentCount: 0,
            FieldCount: 5,
            SizeInBytes: 1024,
            Capabilities: new IndexCapabilities(true, true, true, true),
            RebuildUnavailableReason: null,
            ZeroDowntime: false,
            IsMemberIndex: member,
            Properties: new Dictionary<string, string>()
        );
}
