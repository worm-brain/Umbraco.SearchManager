using Examine;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Infrastructure.Examine;
using Umbraco.Cms.Search.Core.Models.Searching;
using Umbraco.Cms.Search.Core.Models.Searching.Filtering;
using Umbraco.Cms.Search.Core.Services;
using Umbraco.Cms.Search.Core.Services.ContentIndexing;
using Umbraco.Cms.Search.Provider.Examine.Services;
using Umbraco.Community.SearchManager.Core.Diagnose;
using Umbraco.Community.SearchManager.Core.Explore;
using Umbraco.Community.SearchManager.UmbracoSearch.Diagnose;
using Umbraco.Community.SearchManager.UmbracoSearch.Explore;
using ExploreFilter = Umbraco.Community.SearchManager.Core.Explore.Filter;
using IndexOptions = Umbraco.Cms.Search.Core.Configuration.IndexOptions;
using SearchFilter = Umbraco.Cms.Search.Core.Models.Searching.Filtering.Filter;
using SearchResult = Umbraco.Cms.Search.Core.Models.Searching.SearchResult;
using UmbracoSearcher = Umbraco.Cms.Search.Core.Services.ISearcher;

namespace Umbraco.Community.SearchManager.UmbracoSearch.Tests.Diagnose;

public class UmbracoSearchNodePresenceSourceTests
{
    private static readonly Guid Key = Guid.Parse("c8addc1f-0a30-4bd9-849d-243ae8ab24a2");

    private static readonly string[] Cultures = ["en-US", "da-DK"];

    private static readonly NodeFacts Guide = new(
        1061,
        Key,
        "Guide",
        "document",
        "contentPage",
        "-1,1061",
        Published: true,
        Trashed: false,
        Protected: false,
        new DateTime(2026, 10, 1),
        new DateTime(2026, 10, 1),
        []
    );

    private readonly IndexOptions _options = new();
    private readonly ISearcherResolver _searcherResolver = Substitute.For<ISearcherResolver>();
    private readonly UmbracoSearcher _searcher = Substitute.For<UmbracoSearcher>();
    private readonly List<IEnumerable<SearchFilter>?> _filtersSent = [];

    public UmbracoSearchNodePresenceSourceTests()
    {
        _options.RegisterContentIndex<IIndexer, UmbracoSearcher, IPublishedContentChangeStrategy>(
            "Umb_PublishedContent",
            UmbracoObjectTypes.Document
        );
        _options.RegisterContentIndex<IIndexer, UmbracoSearcher, IDraftContentChangeStrategy>(
            "Umb_Media",
            UmbracoObjectTypes.Media
        );
        _searcherResolver.GetSearcher(Arg.Any<string>()).Returns(_searcher);
        ReturnTotal(0);
    }

    [Fact]
    public async Task GetPresenceAsync_IndexOfMediaOnly_IsNotApplicable()
    {
        // Act
        IndexPresence presence = await PresenceAsync("Umb_Media", Guide);

        // Assert
        Assert.Equal(PresenceStatus.NotApplicable, presence.Status);
    }

    [Fact]
    public async Task GetPresenceAsync_UnpublishedNodeMissingFromThePublishedIndex_SaysItIsNotPublished()
    {
        // Act
        IndexPresence presence = await PresenceAsync(
            "Umb_PublishedContent",
            Guide with
            {
                Published = false,
            }
        );

        // Assert
        Assert.Contains("not published", presence.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetPresenceAsync_NodeTheProviderFinds_IsPresentWithoutAStaleCheck()
    {
        // Arrange
        ReturnTotal(1);

        // Act
        IndexPresence presence = await PresenceAsync("Umb_PublishedContent", Guide);

        // Assert
        Assert.Equal((PresenceStatus.Present, (bool?)null), (presence.Status, presence.Stale));
    }

    [Fact]
    public async Task MatchAsync_Chip_IsRestrictedToTheNodesKey()
    {
        // Arrange
        ReturnTotal(1);

        // Act
        await Source()
            .MatchAsync(
                Target("Umb_PublishedContent"),
                Guide,
                new NodeMatchRequest([
                    new ExploreFilter(FilterKind.Text, null, FilterOperator.Contains, "guide"),
                ]),
                TestContext.Current.CancellationToken
            );

        // Assert
        Assert.All(
            _filtersSent,
            filters =>
                Assert.Contains(
                    filters!.OfType<KeywordFilter>(),
                    filter =>
                        filter.FieldName == "Umb_Id" && filter.Values.Contains(Key.ToString("D"))
                )
        );
    }

    [Fact]
    public void ReadPhysical_CultureDocuments_ReadsTheirCulturesAndTheUpdateDate()
    {
        // Arrange: the Examine provider's layout, one document per culture.
        using var directory = new Lucene.Net.Store.RAMDirectory();
        var options = new Examine.Lucene.LuceneDirectoryIndexOptions
        {
            DirectoryFactory = new Examine.Lucene.Directories.GenericDirectoryFactory(_ =>
                directory
            ),
            FieldDefinitions = new FieldDefinitionCollection(
                new FieldDefinition("Field_Umb_Id_keywords", FieldDefinitionTypes.Raw),
                new FieldDefinition(
                    "Field_Umb_UpdateDate_datetimeoffsets",
                    FieldDefinitionTypes.DateTime
                ),
                new FieldDefinition("Sys_Culture", FieldDefinitionTypes.Raw)
            ),
        };
        IOptionsMonitor<Examine.Lucene.LuceneDirectoryIndexOptions> monitor = Substitute.For<
            IOptionsMonitor<Examine.Lucene.LuceneDirectoryIndexOptions>
        >();
        monitor.Get("Umb_Content_a").Returns(options);
        using var index = new Examine.Lucene.Providers.LuceneIndex(
            Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance,
            "Umb_Content_a",
            monitor
        );
        using (index.WithThreadingMode(Examine.Lucene.Providers.IndexThreadingMode.Synchronous))
        {
            index.IndexItems([
                .. Cultures.Select(culture => new ValueSet(
                    $"{Key}_{culture}",
                    "document",
                    new Dictionary<string, object>
                    {
                        ["Field_Umb_Id_keywords"] = Key.ToString("D"),
                        ["Field_Umb_UpdateDate_datetimeoffsets"] = new DateTime(2026, 10, 1),
                        ["Sys_Culture"] = culture,
                    }
                )),
            ]);
        }

        // Act
        (IReadOnlyList<string> Cultures, int Count, DateTime? Date)? found =
            UmbracoSearchNodePresenceSource.ReadPhysical(index, Guide);

        // Assert
        Assert.Equal(
            "da-DK,en-US|2|2026-10-01",
            $"{string.Join(",", found!.Value.Cultures)}|{found.Value.Count}|{found.Value.Date:yyyy-MM-dd}"
        );
    }

    private void ReturnTotal(long total)
    {
        _searcher
            .SearchAsync(default!)
            .ReturnsForAnyArgs(
                new SearchResult(total, [new Document(Key, UmbracoObjectTypes.Document)], [])
            )
            .AndDoes(call => _filtersSent.Add(call.ArgAt<IEnumerable<SearchFilter>?>(2)));

        // Configuring a substitute calls it, so the setup call is not a request.
        _filtersSent.Clear();
    }

    private async Task<IndexPresence> PresenceAsync(string alias, NodeFacts node) =>
        Assert.Single(
            await Source()
                .GetPresenceAsync(node, [Target(alias)], TestContext.Current.CancellationToken)
        );

    private UmbracoSearchNodePresenceSource Source()
    {
        var explore = new UmbracoSearchExploreSource(
            Options.Create(_options),
            Substitute.For<IExamineManager>(),
            Substitute.For<IIndexDiagnosticsFactory>(),
            Substitute.For<IEntityService>(),
            Substitute.For<IContentTypeService>(),
            Substitute.For<IMediaTypeService>(),
            Substitute.For<IMemberTypeService>(),
            Substitute.For<ILanguageService>(),
            new FakeTimeProvider(),
            _searcherResolver,
            Substitute.For<IActiveIndexManager>()
        );
        return new UmbracoSearchNodePresenceSource(
            Options.Create(_options),
            explore,
            Substitute.For<IIndexerResolver>()
        );
    }

    private static ExploreTarget Target(string alias) =>
        new(
            alias,
            "umbraco-search",
            ExploreTargetKind.Index,
            null,
            false,
            new ExploreCapabilities(false, false)
        );
}
