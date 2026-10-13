using Examine;
using Examine.Lucene;
using Examine.Lucene.Directories;
using Examine.Lucene.Providers;
using Lucene.Net.Store;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.Entities;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Infrastructure.Examine;
using Umbraco.Cms.Search.Core.Models.Searching;
using Umbraco.Cms.Search.Core.Models.Searching.Faceting;
using Umbraco.Cms.Search.Core.Models.Searching.Filtering;
using Umbraco.Cms.Search.Core.Models.Searching.Sorting;
using Umbraco.Cms.Search.Core.Services;
using Umbraco.Cms.Search.Core.Services.ContentIndexing;
using Umbraco.Cms.Search.Provider.Examine.Services;
using Umbraco.Community.SearchManager.Core.Explore;
using Umbraco.Community.SearchManager.UmbracoSearch.Explore;
using ExploreFilter = Umbraco.Community.SearchManager.Core.Explore.Filter;
using IndexOptions = Umbraco.Cms.Search.Core.Configuration.IndexOptions;
using SearchFilter = Umbraco.Cms.Search.Core.Models.Searching.Filtering.Filter;
using UmbracoSearcher = Umbraco.Cms.Search.Core.Services.ISearcher;

namespace Umbraco.Community.SearchManager.UmbracoSearch.Tests.Explore;

public class UmbracoSearchExploreSourceTests
{
    private const string Alias = "Umb_PublishedContent";
    private static readonly Guid Home = new("11111111-2222-3333-4444-555555555555");
    private static readonly Guid BlogPost = new("8c5f6c9a-1d1b-4c43-9b0a-6f7bdbd0a1e2");

    private readonly IndexOptions _options = new();
    private readonly ISearcherResolver _searcherResolver = Substitute.For<ISearcherResolver>();
    private readonly UmbracoSearcher _searcher = Substitute.For<UmbracoSearcher>();
    private readonly IEntityService _entityService = Substitute.For<IEntityService>();
    private readonly IContentTypeService _contentTypes = Substitute.For<IContentTypeService>();
    private readonly IMediaTypeService _mediaTypes = Substitute.For<IMediaTypeService>();
    private readonly IMemberTypeService _memberTypes = Substitute.For<IMemberTypeService>();
    private readonly ILanguageService _languages = Substitute.For<ILanguageService>();
    private readonly ExploreTarget _target;
    private readonly UmbracoSearchExploreSource _source;

    public UmbracoSearchExploreSourceTests()
    {
        _options.RegisterContentIndex<
            IExamineIndexer,
            IExamineSearcher,
            IPublishedContentChangeStrategy
        >(Alias, UmbracoObjectTypes.Document);
        _searcherResolver.GetSearcher(Alias).Returns(_searcher);
        _searcher
            .SearchAsync(default!)
            .ReturnsForAnyArgs(
                new Umbraco.Cms.Search.Core.Models.Searching.SearchResult(
                    1,
                    [new Document(Home, UmbracoObjectTypes.Document)],
                    []
                )
            );
        IContentType blogPost = Substitute.For<IContentType>();
        blogPost.Key.Returns(BlogPost);
        _contentTypes.Get(Arg.Any<string>()).Returns((IContentType?)null);
        _contentTypes.Get("blogPost").Returns(blogPost);
        _mediaTypes.Get(Arg.Any<string>()).Returns((IMediaType?)null);
        _memberTypes.Get(Arg.Any<string>()).Returns((IMemberType?)null);

        _source = new UmbracoSearchExploreSource(
            Options.Create(_options),
            Substitute.For<IExamineManager>(),
            Substitute.For<IIndexDiagnosticsFactory>(),
            _entityService,
            _contentTypes,
            _mediaTypes,
            _memberTypes,
            _languages,
            new FakeTimeProvider(new DateTimeOffset(2026, 10, 12, 9, 0, 0, TimeSpan.Zero)),
            _searcherResolver,
            Substitute.For<IActiveIndexManager>()
        );
        _target = new ExploreTarget(
            Alias,
            "umbraco-search",
            ExploreTargetKind.Index,
            null,
            false,
            new ExploreCapabilities(false, false)
        );
    }

    [Fact]
    public async Task GetTargetsAsync_RegistrationWithASearcher_IsAnIndexWithoutNativeQueries()
    {
        // Act
        ExploreTarget target = Assert.Single(
            await _source.GetTargetsAsync(TestContext.Current.CancellationToken)
        );

        // Assert
        Assert.Equal(
            (Alias, "umbraco-search", false, false),
            (target.Name, target.Engine, target.Capabilities.NativeQuery, target.IsMemberData)
        );
    }

    [Fact]
    public async Task GetTargetsAsync_RegistrationWithASearcher_CannotExplainScores()
    {
        // Act
        ExploreTarget target = Assert.Single(
            await _source.GetTargetsAsync(TestContext.Current.CancellationToken)
        );

        // Assert
        Assert.False(target.Capabilities.ExplainScore);
    }

    [Fact]
    public async Task ExplainAsync_AnyRequest_IsNotSupported()
    {
        // Act
        Task Explain() =>
            _source.ExplainAsync(
                _target,
                new ExplainRequest([], null, null, [Home.ToString()]),
                TestContext.Current.CancellationToken
            );

        // Assert
        await Assert.ThrowsAsync<NotSupportedException>(Explain);
    }

    [Fact]
    public async Task GetDefaultCultureAsync_SiteWithADefaultLanguage_ReturnsItsIsoCode()
    {
        // Arrange
        _languages.GetDefaultIsoCodeAsync().Returns("en-US");

        // Act
        string? culture = await _source.GetDefaultCultureAsync(
            TestContext.Current.CancellationToken
        );

        // Assert
        Assert.Equal("en-US", culture);
    }

    [Fact]
    public async Task GetTargetsAsync_NoSearcherResolver_ListsNothing()
    {
        // Arrange
        var source = new UmbracoSearchExploreSource(
            Options.Create(_options),
            Substitute.For<IExamineManager>(),
            Substitute.For<IIndexDiagnosticsFactory>(),
            _entityService,
            _contentTypes,
            _mediaTypes,
            _memberTypes,
            Substitute.For<ILanguageService>(),
            TimeProvider.System
        );

        // Act
        IReadOnlyList<ExploreTarget> targets = await source.GetTargetsAsync(
            TestContext.Current.CancellationToken
        );

        // Assert
        Assert.Empty(targets);
    }

    [Fact]
    public async Task SearchAsync_WordAndDocumentType_SendsQueryKeyFilterAndCulture()
    {
        // Act
        await SearchAsync(
            "en-US",
            new ExploreFilter(FilterKind.Text, null, FilterOperator.Contains, "summer"),
            new ExploreFilter(FilterKind.Field, "type", FilterOperator.Is, "blogPost")
        );

        // Assert
        await _searcher
            .Received(1)
            .SearchAsync(
                Alias,
                "summer",
                Arg.Is<IEnumerable<SearchFilter>>(filters =>
                    filters.Single() is KeywordFilter
                    && ((KeywordFilter)filters.Single()).Values.Single()
                        == "8c5f6c9a-1d1b-4c43-9b0a-6f7bdbd0a1e2"
                ),
                null,
                null,
                "en-US",
                null,
                Arg.Is<AccessContext>(context => context.Bypass),
                0,
                20,
                0
            );
    }

    [Fact]
    public async Task SearchAsync_NoFreeText_SortsNewestFirst()
    {
        // Act
        ExploreResult result = await SearchAsync(null);

        // Assert
        Assert.Multiple(
            () => Assert.True(result.NewestFirst),
            () =>
                _searcher
                    .Received(1)
                    .SearchAsync(
                        Alias,
                        null,
                        Arg.Any<IEnumerable<SearchFilter>>(),
                        null,
                        Arg.Is<IEnumerable<Sorter>>(sorters =>
                            sorters.Single() is DateTimeOffsetSorter
                        ),
                        null,
                        null,
                        Arg.Any<AccessContext>(),
                        0,
                        20,
                        0
                    )
        );
    }

    [Fact]
    public async Task SearchAsync_VariantDocument_ShowsNameIconTypeAndCulture()
    {
        // Arrange
        IDocumentEntitySlim entity = Substitute.For<IDocumentEntitySlim>();
        entity.Key.Returns(Home);
        entity.Name.Returns("Home");
        entity.CultureNames.Returns(new Dictionary<string, string> { ["da-DK"] = "Hjem" });
        entity.Variations.Returns(ContentVariation.Culture);
        entity.ContentTypeIcon.Returns("icon-home");
        entity.ContentTypeAlias.Returns("home");
        _entityService.GetAll(UmbracoObjectTypes.Document, Arg.Any<Guid[]>()).Returns([entity]);

        // Act
        ExploreHit hit = Assert.Single((await SearchAsync("da-DK")).Hits);

        // Assert
        Assert.Equal(
            ("Hjem", "icon-home", "home", "content", "da-DK", Home.ToString("D")),
            (hit.Name, hit.Icon, hit.DocumentType, hit.EntityType, hit.Cultures.Single(), hit.Key)
        );
    }

    [Fact]
    public async Task SearchAsync_UnsupportedChip_IsSkippedAndNotSent()
    {
        // Act
        ExploreResult result = await SearchAsync(
            null,
            new ExploreFilter(FilterKind.Text, null, FilterOperator.Contains, "summer", true)
        );

        // Assert
        Assert.Multiple(
            () => Assert.Equal(0, Assert.Single(result.Skipped).Index),
            () =>
                _searcher
                    .Received(1)
                    .SearchAsync(
                        Alias,
                        null,
                        Arg.Is<IEnumerable<SearchFilter>>(filters => !filters.Any()),
                        Arg.Any<IEnumerable<Facet>>(),
                        Arg.Any<IEnumerable<Sorter>>(),
                        Arg.Any<string>(),
                        Arg.Any<string>(),
                        Arg.Any<AccessContext>(),
                        Arg.Any<int>(),
                        Arg.Any<int>(),
                        Arg.Any<int>()
                    )
        );
    }

    [Fact]
    public async Task CompileAsync_Chips_ShowsTheSearchAsyncCallInCSharp()
    {
        // Act
        CompiledQuery compiled = await _source.CompileAsync(
            _target,
            new CompileRequest(
                [new ExploreFilter(FilterKind.Field, "type", FilterOperator.Is, "blogPost")],
                "en-US"
            ),
            TestContext.Current.CancellationToken
        );

        // Assert
        Assert.Equal(
            """
            var result = await searcher.SearchAsync(
                "Umb_PublishedContent",
                filters:
                [
                    new KeywordFilter("Umb_ContentTypeId", ["8c5f6c9a-1d1b-4c43-9b0a-6f7bdbd0a1e2"], Negate: false), // blogPost
                ],
                sorters: [new DateTimeOffsetSorter("Umb_UpdateDate", Direction.Descending)],
                culture: "en-US",
                accessContext: AccessContext.BypassProtection(),
                skip: 0,
                take: 20);
            """.ReplaceLineEndings("\n"),
            compiled.Code
        );
    }

    [Fact]
    public async Task CompileAsync_NativeQuery_IsNotSupported()
    {
        // Act
        Task Act() =>
            _source.CompileAsync(
                _target,
                new CompileRequest([], null, "x"),
                TestContext.Current.CancellationToken
            );

        // Assert
        await Assert.ThrowsAsync<NotSupportedException>(Act);
    }

    [Fact]
    public void ReadStoredFields_KeysAndCulture_ReturnsTheCulturesDocumentWithFilterNames()
    {
        // Arrange
        using LuceneIndex index = PhysicalIndex(
            Document(Home, "en-US", "Home"),
            Document(Home, "da-DK", "Hjem"),
            Document(BlogPost, "none", "Invariant post")
        );

        // Act
        IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, IReadOnlyList<string>>> fields =
            UmbracoSearchExploreSource.ReadStoredFields(index.Searcher, [Home, BlogPost], "da-DK");

        // Assert
        Assert.Equal(
            ("Hjem", "Invariant post"),
            (fields[Home]["Umb_Name"][0], fields[BlogPost]["Umb_Name"][0])
        );
    }

    private Task<ExploreResult> SearchAsync(string? culture, params ExploreFilter[] filters) =>
        _source.SearchAsync(
            _target,
            new ExploreQuery(filters, culture),
            TestContext.Current.CancellationToken
        );

    private static ValueSet Document(Guid key, string culture, string name) =>
        new(
            $"{key:D}_{culture}",
            "Document",
            new Dictionary<string, object>
            {
                ["Field_Umb_Id_keywords"] = key.ToString("D"),
                ["Sys_Culture"] = culture,
                ["Field_Umb_Name_textsr1"] = name,
            }
        );

    /// <summary>An in-memory Lucene index with the provider's raw key and culture fields.</summary>
    private static LuceneIndex PhysicalIndex(params ValueSet[] documents)
    {
        var directory = new RAMDirectory();
        var options = new LuceneDirectoryIndexOptions
        {
            DirectoryFactory = new GenericDirectoryFactory(_ => directory),
            FieldDefinitions = new FieldDefinitionCollection(
                new FieldDefinition("Field_Umb_Id_keywords", FieldDefinitionTypes.Raw),
                new FieldDefinition("Sys_Culture", FieldDefinitionTypes.Raw)
            ),
        };
        IOptionsMonitor<LuceneDirectoryIndexOptions> monitor = Substitute.For<
            IOptionsMonitor<LuceneDirectoryIndexOptions>
        >();
        monitor.Get("physical").Returns(options);
        var index = new LuceneIndex(NullLoggerFactory.Instance, "physical", monitor);
        using (index.WithThreadingMode(IndexThreadingMode.Synchronous))
        {
            index.IndexItems(documents);
        }

        return index;
    }
}
