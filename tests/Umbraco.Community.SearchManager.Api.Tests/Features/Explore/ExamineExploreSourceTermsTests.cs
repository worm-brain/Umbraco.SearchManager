using Examine;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Umbraco.Cms.Core.Services;
using Umbraco.Community.SearchManager.Core.Explore;
using Umbraco.Community.SearchManager.Core.Indexes;
using Umbraco.Community.SearchManager.Features.Explore;

namespace Umbraco.Community.SearchManager.Api.Tests.Features.Explore;

/// <summary>
/// Term suggestions, field coverage and fields-panel counts read from a real in-memory Lucene
/// index through Examine's public searcher API (ADR 0004).
/// </summary>
public sealed class ExamineExploreSourceTermsTests : IDisposable
{
    private readonly TestLuceneIndex _index = new(
        "ExternalIndex",
        TestLuceneIndex.Content(1, "blogPost", "Umbraco guide", 2, new DateTime(2026, 1, 15)),
        TestLuceneIndex.Content(2, "contentPage", "Getting started", 3, new DateTime(2026, 3, 1)),
        TestLuceneIndex.Content(
            3,
            "product",
            "Red hoodie",
            3,
            new DateTime(2026, 9, 30),
            new() { ["price"] = 25.5 }
        ),
        TestLuceneIndex.Content(
            4,
            "product",
            "Blue mug",
            4,
            new DateTime(2025, 12, 31),
            new() { ["price"] = 9.0 }
        ),
        TestLuceneIndex.Content(5, "home", "Home", 1, new DateTime(2026, 6, 1))
    );

    private readonly ExamineExploreSource _source;
    private readonly ExploreTarget _target = Target(termStatistics: true);

    public ExamineExploreSourceTermsTests()
    {
        IExamineManager examineManager = Substitute.For<IExamineManager>();
        examineManager.Indexes.Returns([_index.Index]);
        examineManager
            .TryGetIndex("ExternalIndex", out Arg.Any<IIndex>()!)
            .Returns(call =>
            {
                call[1] = _index.Index;
                return true;
            });
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 12, 12, 0, 0, TimeSpan.Zero));
        clock.SetLocalTimeZone(TimeZoneInfo.Utc);
        _source = new ExamineExploreSource(
            examineManager,
            Array.Empty<IIndexSource>(),
            Substitute.For<ILanguageService>(),
            clock
        );
    }

    [Fact]
    public async Task GetTermsAsync_KeywordField_ReturnsTermsMostCommonFirst()
    {
        // Act
        TermsResult? terms = await TermsAsync("type", "");

        // Assert
        Assert.Equal(
            ["product:2", "blogpost:1", "contentpage:1", "home:1"],
            terms!.Terms.Select(term => $"{term.Value}:{term.Count}")
        );
    }

    [Fact]
    public async Task GetTermsAsync_Prefix_MatchesCaseInsensitively()
    {
        // Act
        TermsResult? terms = await TermsAsync("type", "BL");

        // Assert
        Assert.Equal(["blogpost"], terms!.Terms.Select(term => term.Value));
    }

    [Fact]
    public async Task GetTermsAsync_IntegerField_DecodesFullPrecisionTermsOnly()
    {
        // Act
        TermsResult? terms = await TermsAsync("level", "");

        // Assert
        Assert.Equal(
            ["3:2", "1:1", "2:1", "4:1"],
            terms!.Terms.Select(term => $"{term.Value}:{term.Count}")
        );
    }

    [Fact]
    public async Task GetTermsAsync_DoubleField_DecodesNumbers()
    {
        // Act
        TermsResult? terms = await TermsAsync("price", "");

        // Assert
        Assert.Equal(["25.5", "9"], terms!.Terms.Select(term => term.Value));
    }

    [Fact]
    public async Task GetTermsAsync_TextField_ReturnsAnalysedTermsMarkedAsText()
    {
        // Act
        TermsResult? terms = await TermsAsync("name", "ho");

        // Assert
        Assert.Multiple(
            () => Assert.Equal(FieldType.Text, terms!.Type),
            () => Assert.Equal(["home", "hoodie"], terms!.Terms.Select(term => term.Value))
        );
    }

    [Fact]
    public void Top_MoreTermsThanTheLimit_IsMarkedSampled()
    {
        // Arrange
        using Examine.Lucene.Search.ISearcherReference reference = (
            (Examine.Lucene.Providers.BaseLuceneSearcher)_index.Index.Searcher
        )
            .GetSearchContext()
            .GetSearcher();

        // Act
        (IReadOnlyList<TermCount> terms, bool sampled) = LuceneTerms.Top(
            reference.IndexSearcher.IndexReader,
            "__NodeTypeAlias",
            null,
            "",
            10,
            limit: 2
        );

        // Assert
        Assert.Equal((2, true), (terms.Count, sampled));
    }

    [Fact]
    public async Task GetTermsAsync_UnknownField_ReturnsNull()
    {
        // Act
        TermsResult? terms = await TermsAsync("colour", "");

        // Assert
        Assert.Null(terms);
    }

    [Fact]
    public async Task GetTermsAsync_TargetWithoutTermStatistics_IsNotSupported()
    {
        // Act
        Task Read() =>
            _source.GetTermsAsync(
                Target(termStatistics: false),
                "type",
                "",
                10,
                TestContext.Current.CancellationToken
            );

        // Assert
        await Assert.ThrowsAsync<NotSupportedException>(Read);
    }

    [Fact]
    public async Task GetSchemaAsync_Fields_CarryHowManyDocumentsHaveThem()
    {
        // Act
        FieldSchema schema = await _source.GetSchemaAsync(
            _target,
            TestContext.Current.CancellationToken
        );

        // Assert
        Assert.Equal(
            (5L, 2L, 5L),
            (
                schema.DocumentCount,
                schema.Resolve("price")!.Documents,
                schema.Resolve("type")!.Documents
            )
        );
    }

    [Fact]
    public async Task GetFacetsAsync_NoFilters_CountsTheWholeIndex()
    {
        // Act
        FacetsResult facets = await FacetsAsync([], "type");

        // Assert
        Assert.Multiple(
            () => Assert.Equal((true, 5L), (facets.WholeIndex, facets.Total)),
            () =>
                Assert.Equal(
                    ["product:2", "blogpost:1", "contentpage:1", "home:1"],
                    Single(facets).Values.Select(value => $"{value.Value}:{value.Count}")
                )
        );
    }

    [Fact]
    public async Task GetFacetsAsync_Filters_CountOverTheResults()
    {
        // Act
        FacetsResult facets = await FacetsAsync(
            [new Filter(FilterKind.Field, "level", FilterOperator.Equals, "3")],
            "type"
        );

        // Assert
        Assert.Multiple(
            () => Assert.Equal((false, 2L), (facets.WholeIndex, facets.Total)),
            () =>
                Assert.Equal(
                    ["contentpage:1", "product:1"],
                    Single(facets).Values.Select(value => $"{value.Value}:{value.Count}")
                )
        );
    }

    [Fact]
    public async Task GetFacetsAsync_DateField_CountsRelativeRanges()
    {
        // Act
        FacetsResult facets = await FacetsAsync([], "updated");

        // Assert
        Assert.Equal(
            ["-7d:0", "-30d:1", "-365d:5"],
            Single(facets).Values.Select(value => $"{value.Value}:{value.Count}")
        );
    }

    [Fact]
    public async Task GetFacetsAsync_UnknownField_IsLeftOut()
    {
        // Act
        FacetsResult facets = await FacetsAsync([], "colour");

        // Assert
        Assert.Empty(facets.Fields);
    }

    /// <inheritdoc />
    public void Dispose() => _index.Dispose();

    [Fact]
    public async Task GetFacetsAsync_IncludeChipOnTheField_StillCountsTheFieldsOtherValues()
    {
        // Act
        FacetsResult facets = await FacetsAsync(
            [new Filter(FilterKind.Field, "type", FilterOperator.Is, "product")],
            "type"
        );

        // Assert
        Assert.Equal(
            ["product:2", "blogpost:1", "contentpage:1", "home:1"],
            Single(facets).Values.Select(value => $"{value.Value}:{value.Count}")
        );
    }

    [Fact]
    public async Task GetFacetsAsync_IncludeChipOnTheField_TotalIsStillTheResults()
    {
        // Act
        FacetsResult facets = await FacetsAsync(
            [new Filter(FilterKind.Field, "type", FilterOperator.Is, "product")],
            "type"
        );

        // Assert
        Assert.Equal(2L, facets.Total);
    }

    [Fact]
    public async Task GetFacetsAsync_ExcludeChipOnTheField_KeepsTheValueOut()
    {
        // Act
        FacetsResult facets = await FacetsAsync(
            [new Filter(FilterKind.Field, "type", FilterOperator.Is, "product", Exclude: true)],
            "type"
        );

        // Assert
        Assert.DoesNotContain(Single(facets).Values, value => value.Value == "product");
    }

    [Fact]
    public async Task GetFacetsAsync_ChipOnAnotherField_StillNarrowsTheCounts()
    {
        // Act
        FacetsResult facets = await _source.GetFacetsAsync(
            _target,
            new FacetsQuery(
                [
                    new Filter(FilterKind.Field, "type", FilterOperator.Is, "product"),
                    new Filter(FilterKind.Field, "level", FilterOperator.Equals, "3"),
                ],
                null,
                ["type", "level"]
            ),
            TestContext.Current.CancellationToken
        );

        // Assert: type counts under level=3 only; level counts under type=product only.
        Assert.Equal(
            "contentpage:1,product:1 | 3:1,4:1",
            string.Join(
                " | ",
                facets.Fields.Select(facet =>
                    string.Join(",", facet.Values.Select(value => $"{value.Value}:{value.Count}"))
                )
            )
        );
    }

    [Fact]
    public async Task GetTermsAsync_LowerCasedKeyword_CarriesTheValueAsWritten()
    {
        // Act
        TermCount blogPost = (await TermsAsync("type", "blog"))!.Terms.Single();

        // Assert
        Assert.Equal(("blogpost", "blogPost"), (blogPost.Value, blogPost.Display));
    }

    [Fact]
    public async Task GetTermsAsync_KeywordWrittenInLowerCase_HasNoDisplay()
    {
        // Act
        TermCount product = (await TermsAsync("type", "product"))!.Terms.Single();

        // Assert
        Assert.Null(product.Display);
    }

    [Fact]
    public async Task GetTermsAsync_TextField_KeepsTheTermWithoutDisplay()
    {
        // Act: "Getting started" is stored whole, so no stored value equals the term.
        TermCount getting = (await TermsAsync("nodeName", "gett"))!.Terms.Single();

        // Assert
        Assert.Equal(("getting", (string?)null), (getting.Value, getting.Display));
    }

    [Fact]
    public async Task GetFacetsAsync_LowerCasedKeyword_CarriesTheValueAsWritten()
    {
        // Act
        FacetsResult facets = await FacetsAsync([], "type");

        // Assert
        Assert.Equal(
            "blogPost",
            Single(facets).Values.Single(value => value.Value == "blogpost").Display
        );
    }

    private Task<TermsResult?> TermsAsync(string field, string prefix) =>
        _source.GetTermsAsync(_target, field, prefix, 10, TestContext.Current.CancellationToken);

    private Task<FacetsResult> FacetsAsync(Filter[] filters, string field) =>
        _source.GetFacetsAsync(
            _target,
            new FacetsQuery(filters, null, [field]),
            TestContext.Current.CancellationToken
        );

    private static FieldFacet Single(FacetsResult facets) => Assert.Single(facets.Fields);

    private static ExploreTarget Target(bool termStatistics) =>
        new(
            "ExternalIndex",
            "examine",
            ExploreTargetKind.Index,
            null,
            false,
            new ExploreCapabilities(termStatistics, termStatistics)
        );
}
