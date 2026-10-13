using Examine;
using Examine.Lucene;
using Examine.Lucene.Providers;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Store;
using Lucene.Net.Util;
using NSubstitute;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Community.SearchManager.Api.Tests.Features.Explore;
using Umbraco.Community.SearchManager.Core.Diagnose;
using Umbraco.Community.SearchManager.Core.Explore;
using Umbraco.Community.SearchManager.Features.Diagnose;
using Umbraco.Community.SearchManager.Features.Diagnose.Fields;
using FieldType = Umbraco.Community.SearchManager.Core.Explore.FieldType;

namespace Umbraco.Community.SearchManager.Api.Tests.Features.Diagnose;

/// <summary>
/// Field statistics read from a real in-memory Lucene index through Examine's searcher (ADR 0018).
/// </summary>
public sealed class ExamineFieldStatisticsSourceTests : IDisposable
{
    private readonly TestLuceneIndex _index = new(
        "ExternalIndex",
        TestLuceneIndex.Content(
            1,
            "blogPost",
            "Umbraco guide",
            2,
            new DateTime(2026, 1, 15),
            new() { ["nodeName_en-us"] = "Umbraco guide" }
        ),
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

    private readonly IExamineManager _examineManager = Substitute.For<IExamineManager>();
    private readonly FieldStatisticsCache _cache = new();
    private readonly ExamineFieldStatisticsSource _source;

    public ExamineFieldStatisticsSourceTests()
    {
        _examineManager
            .TryGetIndex("ExternalIndex", out Arg.Any<IIndex>()!)
            .Returns(call =>
            {
                call[1] = _index.Index;
                return true;
            });
        ILanguage english = Substitute.For<ILanguage>();
        english.IsoCode.Returns("en-US");
        ILanguageService languages = Substitute.For<ILanguageService>();
        languages.GetAllAsync().Returns([english]);
        _source = new ExamineFieldStatisticsSource(_examineManager, languages, _cache);
    }

    [Fact]
    public async Task GetFieldStatisticsAsync_Index_CountsDocumentsAndMissingPerField()
    {
        // Act
        FieldStatistic price = FieldOf(await ReadAsync(), "price");

        // Assert
        Assert.Equal((2L, 3L), (price.Documents, price.Missing));
    }

    [Fact]
    public async Task GetFieldStatisticsAsync_AfterADelete_CoverageCountsLiveDocumentsOnly()
    {
        // Arrange: the deleted document stays in the segment until a merge.
        using (_index.Index.WithThreadingMode(IndexThreadingMode.Synchronous))
        {
            _index.Index.DeleteFromIndex(["3"]);
        }

        // Act
        FieldStatisticsResult result = await ReadAsync();

        // Assert
        FieldStatistic price = FieldOf(result, "price");
        Assert.Equal(
            (4L, 1L, true, 1L, 3L, "9:1"),
            (
                result.DocumentCount,
                result.DeletedDocumentCount,
                result.CoverageExact,
                price.Documents!.Value,
                price.Missing!.Value,
                string.Join(",", price.TopTerms.Select(term => $"{term.Value}:{term.Count}"))
            )
        );
    }

    [Fact]
    public async Task GetFieldStatisticsAsync_IntegerField_DecodesTopTermsMostCommonFirst()
    {
        // Act
        FieldStatistic level = FieldOf(await ReadAsync(), "level");

        // Assert
        Assert.Equal(
            ["3:2", "1:1", "2:1", "4:1"],
            level.TopTerms.Select(term => $"{term.Value}:{term.Count}")
        );
    }

    [Fact]
    public async Task GetFieldStatisticsAsync_IntegerField_CountsDistinctValuesNotPrecisionTerms()
    {
        // Act
        FieldStatistic level = FieldOf(await ReadAsync(), "level");

        // Assert
        Assert.Equal(4L, level.UniqueTerms);
    }

    [Fact]
    public async Task GetFieldStatisticsAsync_DateField_ReportsTheRangeInsteadOfTopTerms()
    {
        // Act
        FieldStatistic updated = FieldOf(await ReadAsync(), "updateDate");

        // Assert
        Assert.Equal(
            (new ValueRange("2025-12-31", "2026-09-30"), 0),
            (updated.Range, updated.TopTerms.Count)
        );
    }

    [Fact]
    public async Task GetFieldStatisticsAsync_TextField_IsMarkedAsAnalysedTerms()
    {
        // Act
        FieldStatistic name = FieldOf(await ReadAsync(), "nodeName");

        // Assert
        Assert.Equal((FieldType.Text, true), (name.Type, name.AnalyzedTerms));
    }

    [Theory]
    [InlineData("nodeName", "StandardAnalyzer")]
    [InlineData("level", ExamineAnalyzers.Numeric)]
    [InlineData("__Published", ExamineAnalyzers.WholeValue)]
    [InlineData("__Key", "CultureInvariantWhitespaceAnalyzer")]
    public async Task GetFieldStatisticsAsync_Field_NamesItsAnalyzer(string field, string analyzer)
    {
        // Act
        FieldStatistic statistic = FieldOf(await ReadAsync(), field);

        // Assert
        Assert.Equal(analyzer, statistic.Analyzer);
    }

    [Theory]
    [InlineData("nodeName", FieldGroup.Field, null)]
    [InlineData("nodeName_en-us", FieldGroup.CultureVariant, "en-us")]
    [InlineData("__IndexType", FieldGroup.System, null)]
    public async Task GetFieldStatisticsAsync_Field_IsGrouped(
        string field,
        FieldGroup group,
        string? culture
    )
    {
        // Act
        FieldStatistic statistic = FieldOf(await ReadAsync(), field);

        // Assert
        Assert.Equal((group, culture), (statistic.Group, statistic.Culture));
    }

    [Fact]
    public async Task GetFieldStatisticsAsync_UmbracoField_CarriesItsLabel()
    {
        // Act
        FieldStatistic type = FieldOf(await ReadAsync(), "__NodeTypeAlias");

        // Assert
        Assert.Equal("Document type", type.Label);
    }

    [Fact]
    public async Task GetFieldStatisticsAsync_Searcher_IsUnavailableWithTheReason()
    {
        // Arrange
        ExploreTarget searcher = Target("MultiSearcher", ExploreTargetKind.Searcher);

        // Act
        FieldStatisticsResult result = await _source.GetFieldStatisticsAsync(
            searcher,
            TestContext.Current.CancellationToken
        );

        // Assert
        Assert.Equal(
            (false, true),
            (
                result.Available,
                result.UnavailableReason!.Contains("per index", StringComparison.Ordinal)
            )
        );
    }

    [Fact]
    public async Task GetFieldStatisticsAsync_NonLuceneIndex_IsUnavailable()
    {
        // Arrange
        IIndex other = Substitute.For<IIndex>();
        _examineManager
            .TryGetIndex("AzureIndex", out Arg.Any<IIndex>()!)
            .Returns(call =>
            {
                call[1] = other;
                return true;
            });

        // Act
        FieldStatisticsResult result = await _source.GetFieldStatisticsAsync(
            Target("AzureIndex", ExploreTargetKind.Index),
            TestContext.Current.CancellationToken
        );

        // Assert
        Assert.False(result.Available);
    }

    [Fact]
    public async Task GetFieldStatisticsAsync_UnchangedIndex_IsServedFromTheCache()
    {
        // Arrange
        int reads = 0;

        // Act
        await CachedReadAsync(() => reads++);
        await CachedReadAsync(() => reads++);

        // Assert
        Assert.Equal(1, reads);
    }

    [Fact]
    public async Task GetFieldStatisticsAsync_AfterASave_ReadsAgain()
    {
        // Arrange
        int reads = 0;
        await CachedReadAsync(() => reads++);

        // Act: a save commits a new reader version.
        using (_index.Index.WithThreadingMode(IndexThreadingMode.Synchronous))
        {
            _index.Index.IndexItems([
                TestLuceneIndex.Content(6, "home", "Second home", 1, new DateTime(2026, 7, 1)),
            ]);
        }

        LuceneFieldReading reading = await CachedReadAsync(() => reads++);

        // Assert
        Assert.Equal((2, 6L), (reads, reading.DocumentCount));
    }

    [Fact]
    public async Task GetFieldStatisticsAsync_AfterARebuildClearsTheIndex_ReadsAgain()
    {
        // Arrange
        int reads = 0;
        await CachedReadAsync(() => reads++);

        // Act: a rebuild starts by recreating the index; searches see it once the reader reopens.
        _index.Index.CreateIndex();
        _index.Index.WaitForChanges();
        LuceneFieldReading reading = await CachedReadAsync(() => reads++);

        // Assert
        Assert.Equal((2, 0L), (reads, reading.DocumentCount));
    }

    [Fact]
    public void Read_TermLimit_StopsAndMarksTheCountSampled()
    {
        // Arrange
        using var reference = ((BaseLuceneSearcher)_index.Index.Searcher)
            .GetSearchContext()
            .GetSearcher();

        // Act
        LuceneFieldReading reading = LuceneFieldStatistics.Read(
            reference.IndexSearcher.IndexReader,
            _index.Index,
            name => new LuceneFieldIdentity(name, null, FieldGroup.Field, null),
            TestContext.Current.CancellationToken,
            termLimit: 2
        );

        // Assert
        FieldStatistic name = reading.Fields.Single(field => field.Name == "nodeName");
        Assert.Equal((2L, true), (name.UniqueTerms, name.UniqueTermsSampled));
    }

    [Fact]
    public void Read_StoredOnlyField_HasNoCounts()
    {
        // Arrange: a field stored for display only, and an Examine sort copy.
        using var directory = new RAMDirectory();
        using (
            var writer = new IndexWriter(
                directory,
                new IndexWriterConfig(
                    LuceneVersion.LUCENE_48,
                    new StandardAnalyzer(LuceneVersion.LUCENE_48)
                )
            )
        )
        {
            writer.AddDocument(
                new Document
                {
                    new StoredField("snapshot", "{}"),
                    new StringField("__Sort_nodeName", "home", Field.Store.NO),
                }
            );
        }

        using DirectoryReader reader = DirectoryReader.Open(directory);

        // Act
        LuceneFieldReading reading = LuceneFieldStatistics.Read(
            reader,
            _index.Index,
            name => new LuceneFieldIdentity(name, null, FieldGroup.Field, null),
            TestContext.Current.CancellationToken
        );

        // Assert
        FieldStatistic stored = Assert.Single(reading.Fields);
        Assert.Equal(
            ("snapshot", true, (long?)null),
            (stored.Name, stored.StoredOnly, stored.Documents)
        );
    }

    /// <inheritdoc />
    public void Dispose() => _index.Dispose();

    private Task<FieldStatisticsResult> ReadAsync() =>
        _source.GetFieldStatisticsAsync(
            Target("ExternalIndex", ExploreTargetKind.Index),
            TestContext.Current.CancellationToken
        );

    private Task<LuceneFieldReading> CachedReadAsync(Action onRead) =>
        _cache.GetAsync(
            "ExternalIndex",
            _index.Index,
            (reader, token) =>
            {
                onRead();
                return LuceneFieldStatistics.Read(
                    reader,
                    _index.Index,
                    name => new LuceneFieldIdentity(name, null, FieldGroup.Field, null),
                    token
                );
            },
            TestContext.Current.CancellationToken
        );

    private static FieldStatistic FieldOf(FieldStatisticsResult result, string name) =>
        result.Fields.Single(field => field.Name == name);

    private static ExploreTarget Target(string name, ExploreTargetKind kind) =>
        new(name, "examine", kind, null, false, new ExploreCapabilities(true, true));
}
