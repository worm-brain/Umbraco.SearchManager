using Examine;
using Examine.Lucene;
using Examine.Lucene.Directories;
using Examine.Lucene.Providers;
using Lucene.Net.Store;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Search.Core.Services;
using Umbraco.Cms.Search.Core.Services.ContentIndexing;
using Umbraco.Cms.Search.Provider.Examine.Services;
using Umbraco.Community.SearchManager.Core.Diagnose;
using Umbraco.Community.SearchManager.Core.Explore;
using Umbraco.Community.SearchManager.UmbracoSearch.Diagnose;
using IndexOptions = Umbraco.Cms.Search.Core.Configuration.IndexOptions;

namespace Umbraco.Community.SearchManager.UmbracoSearch.Tests.Diagnose;

/// <summary>
/// The analyzer view of an Umbraco Search index, run on a real in-memory Lucene index laid out as
/// the Examine provider writes it.
/// </summary>
public sealed class UmbracoSearchAnalyzerSourceTests : IDisposable
{
    private const string Alias = "Umb_Content";
    private const string Text = "Café menu 2026: the opening hours";

    private static readonly ExploreTarget Target = new(
        Alias,
        "umbraco-search",
        ExploreTargetKind.Index,
        null,
        false,
        new ExploreCapabilities(false, false)
    );

    private readonly RAMDirectory _directory = new();
    private readonly LuceneIndex _physical;
    private readonly IndexOptions _options = new();
    private readonly IExamineManager _examineManager = Substitute.For<IExamineManager>();

    public UmbracoSearchAnalyzerSourceTests()
    {
        var options = new LuceneDirectoryIndexOptions
        {
            DirectoryFactory = new GenericDirectoryFactory(_ => _directory),
            FieldDefinitions = new FieldDefinitionCollection(
                new FieldDefinition("Sys_aggregated_texts", FieldDefinitionTypes.FullText),
                new FieldDefinition("Field_title_texts", FieldDefinitionTypes.FullText),
                new FieldDefinition("Field_Umb_ContentTypeId_keywords", FieldDefinitionTypes.Raw),
                new FieldDefinition("Field_Umb_Level_integers", FieldDefinitionTypes.Integer)
            ),
        };
        IOptionsMonitor<LuceneDirectoryIndexOptions> monitor = Substitute.For<
            IOptionsMonitor<LuceneDirectoryIndexOptions>
        >();
        monitor.Get(Alias + "_a").Returns(options);
        _physical = new LuceneIndex(NullLoggerFactory.Instance, Alias + "_a", monitor);
        using (_physical.WithThreadingMode(IndexThreadingMode.Synchronous))
        {
            _physical.IndexItems([
                new ValueSet(
                    "1",
                    "document",
                    new Dictionary<string, object>
                    {
                        ["Sys_aggregated_texts"] = "Opening hours",
                        ["Field_title_texts"] = "Opening hours",
                        ["Field_Umb_ContentTypeId_keywords"] = "a1b2",
                        ["Field_Umb_Level_integers"] = 2,
                    }
                ),
            ]);
        }

        _examineManager
            .TryGetIndex(Alias + "_a", out Arg.Any<IIndex>()!)
            .Returns(call =>
            {
                call[1] = _physical;
                return true;
            });
    }

    [Fact]
    public async Task GetFieldsAsync_ExamineProvider_DefaultsToAllText()
    {
        // Act
        AnalyzerFieldsResult result = await Source<IExamineIndexer>()
            .GetFieldsAsync(Target, Cancel);

        // Assert
        Assert.Equal(
            ("Sys_aggregated_texts", "All text (what a search query reads)"),
            (result.DefaultField, result.Fields[0].Label)
        );
    }

    [Fact]
    public async Task GetFieldsAsync_ExamineProvider_ListsTextAndKeywordFieldsUnderTheirFilterNames()
    {
        // Act
        AnalyzerFieldsResult result = await Source<IExamineIndexer>()
            .GetFieldsAsync(Target, Cancel);

        // Assert
        Assert.Equal(
            [
                "Sys_aggregated_texts",
                "title:Text:Field_title_texts",
                "Umb_ContentTypeId:Keyword:Field_Umb_ContentTypeId_keywords",
            ],
            result.Fields.Select(field =>
                field.Physical is null ? field.Name : $"{field.Name}:{field.Type}:{field.Physical}"
            )
        );
    }

    [Theory]
    [InlineData("open", true)]
    [InlineData("menu open", true)]
    [InlineData("menu breakfast", false)]
    public async Task AnalyzeAsync_AllText_JudgesEveryWordAsAWordOrItsStart(
        string term,
        bool matches
    )
    {
        // Act
        AnalysisResult result = await AnalyzeAsync("Sys_aggregated_texts", term);

        // Assert
        Assert.Equal(matches, result.Search!.Matches);
    }

    [Fact]
    public async Task AnalyzeAsync_AllText_TheSearchBecomesTheWordOrItsStart()
    {
        // Act
        AnalysisResult result = await AnalyzeAsync("Sys_aggregated_texts", "open");

        // Assert
        Assert.Equal(
            ["Word open", "Prefix open"],
            result.Search!.Parts.Select(part => $"{part.Kind} {part.Text}")
        );
    }

    [Fact]
    public async Task AnalyzeAsync_TextField_JudgesTheTextFilter()
    {
        // Act
        AnalysisResult result = await AnalyzeAsync("title", "hours");

        // Assert
        Assert.True(result.Search!.Matches);
    }

    [Fact]
    public async Task AnalyzeAsync_KeywordField_AnalysesButDoesNotJudge()
    {
        // Act
        AnalysisResult result = await AnalyzeAsync("Umb_ContentTypeId", "menu");

        // Assert
        Assert.Equal(
            (true, true),
            (result.Search is null, result.SearchUnavailableReason is not null)
        );
    }

    [Fact]
    public async Task AnalyzeAsync_NumberField_IsUnavailable()
    {
        // Act
        AnalysisResult result = await AnalyzeAsync("Umb_Level", null);

        // Assert
        Assert.False(result.Available);
    }

    [Fact]
    public async Task GetFieldsAsync_OtherProvider_IsUnavailableWithTheReason()
    {
        // Act
        AnalyzerFieldsResult result = await Source<IIndexer>().GetFieldsAsync(Target, Cancel);

        // Assert
        Assert.Contains(
            "does not expose a Lucene index",
            result.UnavailableReason,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task GetFieldsAsync_PhysicalIndexMissing_IsUnavailableWithTheReason()
    {
        // Arrange
        _examineManager.TryGetIndex(Alias + "_a", out Arg.Any<IIndex>()!).Returns(false);

        // Act
        AnalyzerFieldsResult result = await Source<IExamineIndexer>()
            .GetFieldsAsync(Target, Cancel);

        // Assert
        Assert.Contains("could not be found", result.UnavailableReason, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _physical.Dispose();
        _directory.Dispose();
    }

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private Task<AnalysisResult> AnalyzeAsync(string field, string? term) =>
        Source<IExamineIndexer>()
            .AnalyzeAsync(Target, new AnalyzeRequest(field, Text, term), Cancel);

    private UmbracoSearchAnalyzerSource Source<TIndexer>()
        where TIndexer : class, IIndexer
    {
        if (_options.GetContentIndexRegistrations().Length == 0)
        {
            _options.RegisterContentIndex<TIndexer, IExamineSearcher, IDraftContentChangeStrategy>(
                Alias,
                UmbracoObjectTypes.Document
            );
        }

        IActiveIndexManager active = Substitute.For<IActiveIndexManager>();
        active.ResolveActiveIndexName(Alias).Returns(Alias + "_a");
        return new UmbracoSearchAnalyzerSource(
            Options.Create(_options),
            _examineManager,
            Substitute.For<IIndexerResolver>(),
            active
        );
    }
}
