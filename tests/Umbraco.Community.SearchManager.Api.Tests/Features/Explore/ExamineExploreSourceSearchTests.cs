using System.Globalization;
using Examine;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Umbraco.Cms.Core.Services;
using Umbraco.Community.SearchManager.Core.Explore;
using Umbraco.Community.SearchManager.Core.Indexes;
using Umbraco.Community.SearchManager.Features.Explore;

namespace Umbraco.Community.SearchManager.Api.Tests.Features.Explore;

/// <summary>
/// Searches through <see cref="ExamineExploreSource"/> against a real in-memory Lucene index, so
/// the compiled Lucene is checked by what Examine's parser and Lucene actually match.
/// </summary>
public sealed class ExamineExploreSourceSearchTests : IDisposable
{
    private readonly TestLuceneIndex _index = new(
        "ExternalIndex",
        TestLuceneIndex.Content(
            1,
            "blogPost",
            "Umbraco guide",
            2,
            new DateTime(2026, 1, 15, 9, 30, 0)
        ),
        TestLuceneIndex.Content(
            2,
            "contentPage",
            "Getting started with Umbraco",
            3,
            new DateTime(2026, 3, 1, 23, 59, 0)
        ),
        TestLuceneIndex.Content(
            3,
            "product",
            "Red hoodie",
            3,
            new DateTime(2026, 9, 30, 12, 0, 0),
            new() { ["price"] = 25.5 }
        ),
        TestLuceneIndex.Content(
            4,
            "product",
            "Blue mug",
            4,
            new DateTime(2025, 12, 31, 18, 0, 0),
            new() { ["price"] = 9.0 }
        ),
        TestLuceneIndex.Content(
            5,
            "home",
            "Home",
            1,
            new DateTime(2026, 6, 1),
            new()
            {
                ["nodeName_en-us"] = "Home",
                ["nodeName_da-dk"] = "Hjem",
                ["__VariesByCulture"] = "y",
                ["__Published_en-us"] = "y",
                ["__Published_da-dk"] = "y",
            }
        )
    );

    private readonly ExamineExploreSource _source;
    private readonly ExploreTarget _target;

    public ExamineExploreSourceSearchTests()
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
        _target = new ExploreTarget(
            "ExternalIndex",
            "examine",
            ExploreTargetKind.Index,
            null,
            false,
            new ExploreCapabilities(true, true)
        );
    }

    /// <summary>A name, the chips (Kind|Field|Op|Value|Exclude) and the ids they should find.</summary>
    public static TheoryData<string, string[], string[]> Cases =>
        new()
        {
            { "free text word", ["Text||Contains|umbraco|False"], ["1", "2"] },
            { "free text two words", ["Text||Contains|umbraco guide|False"], ["1"] },
            { "free text phrase", ["Text||Phrase|getting started|False"], ["2"] },
            { "free text prefix", ["Text||Contains|hood*|False"], ["3"] },
            { "keyword", ["Field|type|Is|product|False"], ["3", "4"] },
            { "keyword ignores case", ["Field|type|Is|BlogPost|False"], ["1"] },
            {
                "keyword OR group",
                ["Field|type|Is|product|False", "Field|type|Is|blogPost|False"],
                ["1", "3", "4"]
            },
            { "exclude only", ["Field|type|Is|product|True"], ["1", "2", "5"] },
            { "level equals 3", ["Field|level|Equals|3|False"], ["2", "3"] },
            { "level greater than 3", ["Field|level|GreaterThan|3|False"], ["4"] },
            { "level at most 2", ["Field|level|AtMost|2|False"], ["1", "5"] },
            { "decimal range", ["Field|price|AtLeast|10|False"], ["3"] },
            { "date on a day", ["Field|updated|On|2026-03-01|False"], ["2"] },
            { "date after", ["Field|updated|After|2026-03-01|False"], ["2", "3", "5"] },
            { "date before", ["Field|updated|Before|2026-01-01|False"], ["4"] },
            { "relative date", ["Field|updated|After|-30d|False"], ["3"] },
            { "missing", ["Field|price|Missing||False"], ["1", "2", "5"] },
            { "has a value", ["Field|price|Missing||True"], ["3", "4"] },
            { "text field contains", ["Field|name|Contains|hoodie|False"], ["3"] },
            { "text field starts with", ["Field|name|StartsWith|gett|False"], ["2"] },
            { "text field fuzzy", ["Field|name|Fuzzy|hoody|False"], ["3"] },
            {
                "raw lucene",
                ["Lucene||Contains|nodeName:mug OR nodeName:hoodie|False"],
                ["3", "4"]
            },
            { "no chips matches everything", [], ["1", "2", "3", "4", "5"] },
        };

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task SearchAsync_Chips_FindExpectedDocuments(
        string name,
        string[] chips,
        string[] expected
    )
    {
        // Act
        ExploreResult result = await SearchAsync([.. chips.Select(Chip)]);

        // Assert
        Assert.True(
            expected.SequenceEqual(result.Hits.Select(hit => hit.Id).Order()),
            $"{name}: {result.Query} found {string.Join(",", result.Hits.Select(hit => hit.Id))}"
        );
    }

    [Fact]
    public async Task SearchAsync_Culture_SearchesTheCultureVariantName()
    {
        // Act
        ExploreResult result = await SearchAsync(
            [Chip("Field|name|Contains|hjem|False")],
            culture: "da-DK"
        );

        // Assert
        Assert.Equal(["5"], result.Hits.Select(hit => hit.Id));
    }

    [Fact]
    public async Task SearchAsync_DateRangeUnderAnotherServerCulture_StillReadsIsoDates()
    {
        // Arrange
        CultureInfo original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("da-DK");

        try
        {
            // Act
            ExploreResult result = await SearchAsync([Chip("Field|updated|On|2026-03-01|False")]);

            // Assert
            Assert.Equal(["2"], result.Hits.Select(hit => hit.Id));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public async Task SearchAsync_TicksInADateRange_AreNotReadAsADate()
    {
        // Arrange: the ticks of 2026-09-01, which every document but 3 is before. Examine
        // converts range bounds with a TypeConverter, which cannot read ticks, so the bound
        // is dropped and the range matches every dated document (ADR 0003).
        long ticks = new DateTime(2026, 9, 1).Ticks;

        // Act
        ExploreResult result = await SearchAsync([
            Chip($"Lucene||Contains|updateDate:[{ticks} TO *]|False"),
        ]);

        // Assert
        Assert.Equal(5, result.Total);
    }

    [Fact]
    public async Task SearchAsync_NoRankingClause_SortsNewestFirst()
    {
        // Act
        ExploreResult result = await SearchAsync([]);

        // Assert
        Assert.Equal(["3", "5", "2", "1", "4"], result.Hits.Select(hit => hit.Id));
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("Text||Contains|home|False", false)]
    public async Task SearchAsync_RankingClauseOrNot_SaysWhetherHitsAreNewestFirst(
        string chip,
        bool expected
    )
    {
        // Act
        ExploreResult result = await SearchAsync(chip.Length == 0 ? [] : [Chip(chip)]);

        // Assert
        Assert.Equal(expected, result.NewestFirst);
    }

    [Fact]
    public async Task SearchAsync_Page_ReturnsTotalAndTheAskedSlice()
    {
        // Act
        ExploreResult result = await SearchAsync([], skip: 2, take: 2);

        // Assert
        Assert.Multiple(
            () => Assert.Equal(5L, result.Total),
            () => Assert.Equal(["2", "1"], result.Hits.Select(hit => hit.Id))
        );
    }

    [Fact]
    public async Task SearchAsync_Hit_CarriesUmbracoColumns()
    {
        // Act
        ExploreHit hit = Assert.Single(
            (await SearchAsync([Chip("Field|id|Is|5|False")], culture: "da-DK")).Hits
        );

        // Assert
        Assert.Multiple(
            () => Assert.Equal("Hjem", hit.Name),
            () => Assert.Equal("home", hit.DocumentType),
            () => Assert.Equal("content", hit.EntityType),
            () => Assert.Equal(["da-dk", "en-us"], hit.Cultures),
            () => Assert.Equal(new DateTime(2026, 6, 1), hit.Updated),
            () => Assert.Equal("00000000-0000-0000-0000-000000000005", hit.Key),
            () => Assert.Equal(["Hjem"], hit.Fields["nodeName_da-dk"])
        );
    }

    [Fact]
    public async Task SearchAsync_UnknownField_IsSkippedAndTheRestRuns()
    {
        // Act
        ExploreResult result = await SearchAsync([
            Chip("Field|colour|Is|red|False"),
            Chip("Field|type|Is|product|False"),
        ]);

        // Assert
        Assert.Equal(
            (0, "+__NodeTypeAlias:product", 2),
            (Assert.Single(result.Skipped).Index, result.Query, result.Hits.Count)
        );
    }

    [Fact]
    public async Task SearchAsync_RawLuceneThatDoesNotParse_ThrowsInvalidNativeQuery()
    {
        // Act
        Task Search() => SearchAsync([Chip("Lucene||Contains|nodeName:(mug|False")]);

        // Assert
        await Assert.ThrowsAsync<InvalidNativeQueryException>(Search);
    }

    [Fact]
    public async Task GetSchemaAsync_LuceneIndex_ListsFieldsWithTheirTypes()
    {
        // Act
        FieldSchema schema = await _source.GetSchemaAsync(
            _target,
            TestContext.Current.CancellationToken
        );

        // Assert
        Assert.Multiple(
            () => Assert.Equal(FieldType.Number, schema.Resolve("level")?.Type),
            () => Assert.Equal(FieldType.Number, schema.Resolve("price")?.Type),
            () => Assert.Equal(FieldType.Date, schema.Resolve("updateDate")?.Type),
            () => Assert.Equal(FieldType.Keyword, schema.Resolve("__Published")?.Type),
            () => Assert.Equal(FieldType.Keyword, schema.Resolve("type")?.Type),
            () => Assert.Equal(FieldType.Text, schema.Resolve("nodeName_da-dk")?.Type),
            () => Assert.DoesNotContain(schema.Aliases, alias => alias.Alias == "author")
        );
    }

    [Fact]
    public async Task GetSchemaAsync_AfterRestart_TypesFieldsAsTheIndexWritesThem()
    {
        // Arrange: an index that has written nothing since it opened, as after a site restart.
        using TestLuceneIndex reopened = _index.Reopen();
        IExamineManager examineManager = Substitute.For<IExamineManager>();
        examineManager.Indexes.Returns([reopened.Index]);
        examineManager
            .TryGetIndex("ExternalIndex", out Arg.Any<IIndex>()!)
            .Returns(call =>
            {
                call[1] = reopened.Index;
                return true;
            });
        var source = new ExamineExploreSource(
            examineManager,
            Array.Empty<IIndexSource>(),
            Substitute.For<ILanguageService>(),
            new FakeTimeProvider()
        );

        // Act
        FieldSchema schema = await source.GetSchemaAsync(
            _target,
            TestContext.Current.CancellationToken
        );

        // Assert: Examine's own fields and the defined ones keep their types, so the index
        // goes on writing them the same way.
        Assert.Multiple(
            () => Assert.Equal(FieldType.Keyword, schema.Resolve("__NodeTypeAlias")?.Type),
            () => Assert.Equal(FieldType.Keyword, schema.Resolve("__NodeId")?.Type),
            () => Assert.Equal(FieldType.Number, schema.Resolve("level")?.Type),
            () => Assert.Equal(FieldType.Date, schema.Resolve("updateDate")?.Type),
            () =>
                Assert.IsType<Examine.Lucene.Indexing.GenericAnalyzerFieldValueType>(
                    reopened.Index.FieldValueTypeCollection.GetValueType("__NodeTypeAlias")
                )
        );
    }

    /// <inheritdoc />
    public void Dispose() => _index.Dispose();

    private Task<ExploreResult> SearchAsync(
        Filter[] filters,
        string? culture = null,
        int skip = 0,
        int take = 20
    ) =>
        _source.SearchAsync(
            _target,
            new ExploreQuery(filters, culture, skip, take),
            TestContext.Current.CancellationToken
        );

    private static Filter Chip(string chip)
    {
        string[] parts = chip.Split('|');
        return new Filter(
            Enum.Parse<FilterKind>(parts[0]),
            parts[1].Length == 0 ? null : parts[1],
            Enum.Parse<FilterOperator>(parts[2]),
            parts[3],
            bool.Parse(parts[4])
        );
    }
}
