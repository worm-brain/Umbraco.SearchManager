using Examine;
using Examine.Lucene;
using Examine.Lucene.Directories;
using Examine.Lucene.Providers;
using Lucene.Net.Store;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Search.Core.Services;
using Umbraco.Cms.Search.Core.Services.ContentIndexing;
using Umbraco.Cms.Search.Provider.Examine.Services;
using Umbraco.Community.SearchManager.Core.Diagnose;
using Umbraco.Community.SearchManager.Core.Explore;
using Umbraco.Community.SearchManager.Features.Diagnose.Fields;
using Umbraco.Community.SearchManager.UmbracoSearch.Diagnose;
using IndexOptions = Umbraco.Cms.Search.Core.Configuration.IndexOptions;

namespace Umbraco.Community.SearchManager.UmbracoSearch.Tests.Diagnose;

/// <summary>
/// Field statistics of an Umbraco Search index, read from a real in-memory Lucene index laid out
/// as the Examine provider writes it (one document per culture, <c>Field_{name}_{values}</c>).
/// </summary>
public sealed class UmbracoSearchFieldStatisticsSourceTests : IDisposable
{
    private const string Alias = "Umb_Content";

    private readonly RAMDirectory _directory = new();
    private readonly LuceneIndex _physical;
    private readonly IndexOptions _options = new();
    private readonly IExamineManager _examineManager = Substitute.For<IExamineManager>();
    private readonly ILanguageService _languages = Substitute.For<ILanguageService>();

    public UmbracoSearchFieldStatisticsSourceTests()
    {
        var options = new LuceneDirectoryIndexOptions
        {
            DirectoryFactory = new GenericDirectoryFactory(_ => _directory),
            FieldDefinitions = new FieldDefinitionCollection(
                new FieldDefinition("Field_Umb_Level_integers", FieldDefinitionTypes.Integer),
                new FieldDefinition(
                    "Field_Umb_UpdateDate_datetimeoffsets",
                    FieldDefinitionTypes.DateTime
                ),
                new FieldDefinition("Field_title_texts", FieldDefinitionTypes.FullText),
                new FieldDefinition("Field_title_texts_mobile", FieldDefinitionTypes.FullText),
                new FieldDefinition("Sys_Culture", FieldDefinitionTypes.Raw)
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
                Document("1_en-US", "en-US", "Summer sale", 2),
                Document("1_da-DK", "da-DK", "Sommerudsalg", 2),
                Document("2", "none", "Contact", 3, mobile: "Call us"),
            ]);
        }

        _examineManager
            .TryGetIndex(Alias + "_a", out Arg.Any<IIndex>()!)
            .Returns(call =>
            {
                call[1] = _physical;
                return true;
            });
        _languages.GetDefaultIsoCodeAsync().Returns("en-US");
    }

    [Fact]
    public async Task GetFieldStatisticsAsync_PhysicalField_IsShownUnderItsFilterName()
    {
        // Act
        FieldStatisticsResult result = await ReadAsync<IExamineIndexer>();

        // Assert
        FieldStatistic title = result.Fields.Single(field =>
            field.Name == "title" && field.Group == FieldGroup.Field
        );
        Assert.Equal(
            ("Field_title_texts", FieldType.Text, 3L),
            (title.Physical, title.Type, title.Documents)
        );
    }

    [Fact]
    public async Task GetFieldStatisticsAsync_SegmentField_IsASegmentRow()
    {
        // Act
        FieldStatisticsResult result = await ReadAsync<IExamineIndexer>();

        // Assert
        FieldStatistic mobile = result.Fields.Single(field => field.Group == FieldGroup.Segment);
        Assert.Equal(("title", "mobile segment"), (mobile.Name, mobile.Label));
    }

    [Fact]
    public async Task GetFieldStatisticsAsync_SystemField_DecodesNumbersAndCarriesItsLabel()
    {
        // Act
        FieldStatisticsResult result = await ReadAsync<IExamineIndexer>();

        // Assert
        FieldStatistic level = result.Fields.Single(field => field.Name == "Umb_Level");
        Assert.Equal(
            ("Level", "2:2,3:1"),
            (level.Label, string.Join(",", level.TopTerms.Select(t => $"{t.Value}:{t.Count}")))
        );
    }

    [Fact]
    public async Task GetFieldStatisticsAsync_ProviderField_IsASystemField()
    {
        // Act
        FieldStatisticsResult result = await ReadAsync<IExamineIndexer>();

        // Assert
        Assert.Equal(
            FieldGroup.System,
            result.Fields.Single(field => field.Name == "Sys_Culture").Group
        );
    }

    [Fact]
    public async Task GetFieldStatisticsAsync_VariantDocuments_CountsCultureDocumentsAndLinksInTheDefaultCulture()
    {
        // Act
        FieldStatisticsResult result = await ReadAsync<IExamineIndexer>();

        // Assert
        Assert.Equal(
            (3L, true, "en-US"),
            (result.DocumentCount, result.DocumentsPerCulture, result.ExploreCulture)
        );
    }

    [Fact]
    public async Task GetFieldStatisticsAsync_OtherProvider_IsUnavailableWithTheReason()
    {
        // Act
        FieldStatisticsResult result = await ReadAsync<IIndexer>();

        // Assert
        Assert.Equal(
            (false, true),
            (
                result.Available,
                result.UnavailableReason!.Contains(
                    "does not expose a Lucene index",
                    StringComparison.Ordinal
                )
            )
        );
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _physical.Dispose();
        _directory.Dispose();
    }

    private Task<FieldStatisticsResult> ReadAsync<TIndexer>()
        where TIndexer : class, IIndexer
    {
        _options.RegisterContentIndex<TIndexer, IExamineSearcher, IDraftContentChangeStrategy>(
            Alias,
            UmbracoObjectTypes.Document
        );
        IActiveIndexManager active = Substitute.For<IActiveIndexManager>();
        active.ResolveActiveIndexName(Alias).Returns(Alias + "_a");
        var source = new UmbracoSearchFieldStatisticsSource(
            Options.Create(_options),
            _examineManager,
            _languages,
            new FieldStatisticsCache(),
            Substitute.For<IIndexerResolver>(),
            active
        );
        return source.GetFieldStatisticsAsync(
            new ExploreTarget(
                Alias,
                "umbraco-search",
                ExploreTargetKind.Index,
                null,
                false,
                new ExploreCapabilities(false, false)
            ),
            TestContext.Current.CancellationToken
        );
    }

    private static ValueSet Document(
        string id,
        string culture,
        string title,
        int level,
        string? mobile = null
    )
    {
        var values = new Dictionary<string, object>
        {
            ["Field_title_texts"] = title,
            ["Field_Umb_Level_integers"] = level,
            ["Field_Umb_UpdateDate_datetimeoffsets"] = new DateTime(2026, 10, 1),
            ["Sys_Culture"] = culture,
        };
        if (mobile is not null)
        {
            values["Field_title_texts_mobile"] = mobile;
        }

        return new ValueSet(id, "document", values);
    }
}
