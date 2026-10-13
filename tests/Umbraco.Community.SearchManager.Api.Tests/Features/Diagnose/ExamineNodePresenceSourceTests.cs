using Examine;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Umbraco.Cms.Infrastructure.Examine;
using Umbraco.Community.SearchManager.Api.Tests.Features.Explore;
using Umbraco.Community.SearchManager.Core.Diagnose;
using Umbraco.Community.SearchManager.Core.Explore;
using Umbraco.Community.SearchManager.Features.Diagnose.Nodes;

namespace Umbraco.Community.SearchManager.Api.Tests.Features.Diagnose;

/// <summary>
/// "Find a node" against real in-memory Lucene indexes with substituted Umbraco validators (ADR 0019).
/// </summary>
public sealed class ExamineNodePresenceSourceTests : IDisposable
{
    private static readonly DateTime Saved = new(2026, 10, 1, 9, 0, 0);

    private static readonly NodeFacts Guide = new(
        1061,
        Guid.Parse("00000000-0000-0000-0000-000000001061"),
        "Umbraco guide",
        "document",
        "blogPost",
        "-1,1060,1061",
        Published: true,
        Trashed: false,
        Protected: false,
        Saved,
        Saved,
        []
    );

    private readonly IContentValueSetValidator _validator = Validator(publishedOnly: false);
    private readonly TestLuceneIndex _index;
    private readonly IExamineManager _examineManager = Substitute.For<IExamineManager>();
    private readonly List<IIndex> _indexes = [];
    private readonly ExamineNodePresenceSource _source;

    public ExamineNodePresenceSourceTests()
    {
        _index = new TestLuceneIndex(
            "InternalIndex",
            _validator,
            TestLuceneIndex.Content(1061, "blogPost", "Umbraco guide", 2, Saved),
            TestLuceneIndex.Content(1062, "contentPage", "Getting started", 3, Saved)
        );
        Register(_index.Index);
        _source = new ExamineNodePresenceSource(
            _examineManager,
            new FakeTimeProvider(new DateTimeOffset(2026, 10, 12, 12, 0, 0, TimeSpan.Zero))
        );
    }

    [Fact]
    public async Task GetPresenceAsync_NodeInTheIndex_IsPresentWithTheDocumentToView()
    {
        // Act
        IndexPresence presence = await PresenceAsync(Guide);

        // Assert
        Assert.Equal(
            (PresenceStatus.Present, 1, "1061", "__NodeId"),
            (
                presence.Status,
                presence.DocumentCount,
                presence.ViewDocumentId,
                presence.ViewFilter?.Field
            )
        );
    }

    [Fact]
    public async Task GetPresenceAsync_VariantNodeInADraftIndex_ListsEveryCulture()
    {
        // Arrange
        using TestLuceneIndex index = Variant("InternalIndex", Validator(publishedOnly: false));

        // Act
        IndexPresence presence = await PresenceAsync(Guide, index);

        // Assert
        Assert.Equal(["da-dk", "en-us"], presence.Cultures);
    }

    [Fact]
    public async Task GetPresenceAsync_VariantNodeInAPublishedIndex_ListsPublishedCulturesOnly()
    {
        // Arrange
        using TestLuceneIndex index = Variant("ExternalIndex", Validator(publishedOnly: true));

        // Act
        IndexPresence presence = await PresenceAsync(Guide, index);

        // Assert
        Assert.Equal(["en-us"], presence.Cultures);
    }

    [Fact]
    public async Task GetPresenceAsync_UnpublishedNodeMissingFromAPublishedIndex_SaysItIsNotPublished()
    {
        // Arrange
        _validator.PublishedValuesOnly.Returns(true);

        // Act
        IndexPresence presence = await PresenceAsync(Guide with { Id = 2000, Published = false });

        // Assert
        Assert.Equal(
            (PresenceStatus.Missing, true),
            (presence.Status, presence.Reason!.Contains("not published", StringComparison.Ordinal))
        );
    }

    [Fact]
    public async Task GetPresenceAsync_NodeOutsideTheIndexesParent_SaysWhere()
    {
        // Arrange
        _validator.ParentId.Returns(1500);
        _validator.ValidatePath("-1,1060,2000", "content").Returns(false);

        // Act
        IndexPresence presence = await PresenceAsync(
            Guide with
            {
                Id = 2000,
                Path = "-1,1060,2000",
            }
        );

        // Assert
        Assert.Contains("under node 1500", presence.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetPresenceAsync_NodeNoRuleKeepsOut_SaysTheIndexIsOutOfDate()
    {
        // Act
        IndexPresence presence = await PresenceAsync(Guide with { Id = 2000 });

        // Assert
        Assert.Equal(PresenceRules.OutOfDate, presence.Reason);
    }

    [Fact]
    public async Task GetPresenceAsync_MemberIndex_IsNotApplicable()
    {
        // Arrange
        IIndex members = Substitute.For<IIndex, IUmbracoMemberIndex>();
        members.Name.Returns("MembersIndex");
        Register(members);

        // Act
        IndexPresence presence = await PresenceAsync(Guide, members);

        // Assert
        Assert.Equal(PresenceStatus.NotApplicable, presence.Status);
    }

    [Fact]
    public async Task GetPresenceAsync_IndexOfMediaOnly_IsNotApplicable()
    {
        // Arrange
        using var media = new TestLuceneIndex(
            "MediaIndex",
            new ValueSet(
                "3000",
                "media",
                "image",
                new Dictionary<string, object> { ["nodeName"] = "Logo" }
            )
        );

        // Act
        IndexPresence presence = await PresenceAsync(Guide, media);

        // Assert
        Assert.Equal(PresenceStatus.NotApplicable, presence.Status);
    }

    [Fact]
    public async Task GetPresenceAsync_IndexCopyOlderThanTheNode_IsFlaggedStale()
    {
        // Act
        IndexPresence presence = await PresenceAsync(
            Guide with
            {
                UpdateDate = Saved.AddMinutes(5),
            }
        );

        // Assert
        Assert.Equal(
            (true, Saved, Saved.AddMinutes(5)),
            (presence.Stale, presence.IndexUpdateDate, presence.ExpectedUpdateDate)
        );
    }

    [Fact]
    public async Task GetPresenceAsync_IndexCopyUpToDate_IsNotStale()
    {
        // Act
        IndexPresence presence = await PresenceAsync(Guide);

        // Assert
        Assert.False(presence.Stale);
    }

    [Fact]
    public async Task MatchAsync_Chips_TestsEachOnTheNodeWithItsValues()
    {
        // Arrange
        Filter[] chips =
        [
            new(FilterKind.Field, "nodeName", FilterOperator.Contains, "guide"),
            new(FilterKind.Field, "level", FilterOperator.Equals, "5"),
        ];

        // Act
        NodeMatchResult result = await MatchAsync(chips, nativeQuery: true);

        // Assert
        Assert.Equal(
            "0:True:nodeName=Umbraco guide|1:False:level=2",
            string.Join(
                "|",
                result.Clauses.Select(clause =>
                    $"{clause.Index}:{clause.Matched}:{clause.Field}={string.Join(",", clause.NodeValues)}"
                )
            )
        );
    }

    [Fact]
    public async Task MatchAsync_OneChipMisses_TheWholeQueryMisses()
    {
        // Arrange
        Filter[] chips =
        [
            new(FilterKind.Field, "nodeName", FilterOperator.Contains, "guide"),
            new(FilterKind.Field, "level", FilterOperator.Equals, "5"),
        ];

        // Act
        NodeMatchResult result = await MatchAsync(chips, nativeQuery: true);

        // Assert
        Assert.Equal((true, false), (result.InIndex, result.Matches));
    }

    [Fact]
    public async Task MatchAsync_RawLuceneWhereTheIndexTakesNone_IsSkippedWithTheReason()
    {
        // Act
        NodeMatchResult result = await MatchAsync(
            [new Filter(FilterKind.Lucene, null, FilterOperator.Contains, "nodeName:guide")],
            nativeQuery: false
        );

        // Assert
        ClauseMatch clause = Assert.Single(result.Clauses);
        Assert.Equal((null, true), (clause.Matched, clause.SkippedReason is not null));
    }

    [Fact]
    public async Task MatchAsync_OnlyAnExcludeChipThatSparesTheNode_Matches()
    {
        // Act
        NodeMatchResult result = await MatchAsync(
            [
                new Filter(
                    FilterKind.Field,
                    "__NodeTypeAlias",
                    FilterOperator.Is,
                    "contentPage",
                    Exclude: true
                ),
            ],
            nativeQuery: true
        );

        // Assert
        Assert.Equal((true, true), (result.Matches, result.Clauses[0].Matched));
    }

    [Fact]
    public async Task MatchAsync_NodeNotInTheIndex_SaysSo()
    {
        // Act
        NodeMatchResult result = await _source.MatchAsync(
            Target("InternalIndex", true),
            Guide with
            {
                Id = 2000,
            },
            new NodeMatchRequest([]),
            TestContext.Current.CancellationToken
        );

        // Assert
        Assert.Equal((false, false), (result.InIndex, result.Matches));
    }

    /// <inheritdoc />
    public void Dispose() => _index.Dispose();

    private async Task<IndexPresence> PresenceAsync(NodeFacts node, TestLuceneIndex index)
    {
        Register(index.Index);
        return await PresenceAsync(node, index.Index);
    }

    private async Task<IndexPresence> PresenceAsync(NodeFacts node, IIndex? index = null)
    {
        string name = index?.Name ?? "InternalIndex";
        return Assert.Single(
            await _source.GetPresenceAsync(
                node,
                [Target(name, true)],
                TestContext.Current.CancellationToken
            )
        );
    }

    private Task<NodeMatchResult> MatchAsync(Filter[] chips, bool nativeQuery) =>
        _source.MatchAsync(
            Target("InternalIndex", nativeQuery),
            Guide,
            new NodeMatchRequest(chips),
            TestContext.Current.CancellationToken
        );

    private void Register(IIndex index)
    {
        // Examine's index list gives each searcher its index, and so its fields' types.
        _indexes.Add(index);
        _examineManager.Indexes.Returns([.. _indexes]);
        _examineManager
            .TryGetIndex(index.Name, out Arg.Any<IIndex>()!)
            .Returns(call =>
            {
                call[1] = index;
                return true;
            });
    }

    private static TestLuceneIndex Variant(string name, IContentValueSetValidator validator) =>
        new(
            name,
            validator,
            TestLuceneIndex.Content(
                1061,
                "blogPost",
                "Umbraco guide",
                2,
                Saved,
                new()
                {
                    ["__VariesByCulture"] = "y",
                    ["__Published_en-us"] = "y",
                    ["__Published_da-dk"] = "n",
                }
            )
        );

    private static IContentValueSetValidator Validator(bool publishedOnly)
    {
        IContentValueSetValidator validator = Substitute.For<IContentValueSetValidator>();
        validator
            .Validate(Arg.Any<ValueSet>())
            .Returns(call => new ValueSetValidationResult(
                ValueSetValidationStatus.Valid,
                call.Arg<ValueSet>()
            ));
        validator.PublishedValuesOnly.Returns(publishedOnly);
        validator.SupportProtectedContent.Returns(true);
        validator.ValidatePath(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        validator.ValidateRecycleBin(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        return validator;
    }

    private static ExploreTarget Target(string name, bool nativeQuery) =>
        new(
            name,
            "examine",
            ExploreTargetKind.Index,
            null,
            false,
            new ExploreCapabilities(nativeQuery, nativeQuery)
        );
}
