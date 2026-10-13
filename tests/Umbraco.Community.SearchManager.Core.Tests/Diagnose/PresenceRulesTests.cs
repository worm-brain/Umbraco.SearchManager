using Umbraco.Community.SearchManager.Core.Diagnose;

namespace Umbraco.Community.SearchManager.Core.Tests.Diagnose;

public class PresenceRulesTests
{
    private static readonly NodeFacts Page = new(
        1061,
        Guid.Parse("c8addc1f-0a30-4bd9-849d-243ae8ab24a2"),
        "Guide",
        "document",
        "contentPage",
        "-1,1060,1061",
        Published: true,
        Trashed: false,
        Protected: false,
        new DateTime(2026, 10, 1, 9, 0, 0),
        new DateTime(2026, 10, 1, 9, 0, 0),
        []
    );

    public static TheoryData<IndexRules, NodeFacts, PresenceStatus, string> Cases =>
        new()
        {
            {
                new IndexRules(HoldsEntityType: false, Empty: true),
                Page,
                PresenceStatus.NotApplicable,
                "does not hold content"
            },
            {
                new IndexRules(ItemTypeIncluded: false, Empty: true),
                Page,
                PresenceStatus.NotApplicable,
                "leaves out the contentPage document type"
            },
            {
                new IndexRules(Empty: true, PublishedOnly: true),
                Page with
                {
                    Published = false,
                },
                PresenceStatus.Missing,
                "index is empty"
            },
            {
                new IndexRules(PublishedOnly: true, IncludesTrashed: false),
                Page with
                {
                    Published = false,
                    Trashed = true,
                },
                PresenceStatus.Missing,
                "not published"
            },
            {
                new IndexRules(IncludesTrashed: false, PathAllowed: false),
                Page with
                {
                    Trashed = true,
                },
                PresenceStatus.Missing,
                "recycle bin"
            },
            {
                new IndexRules(ParentId: 2000, PathAllowed: false, IncludesProtected: false),
                Page with
                {
                    Protected = true,
                },
                PresenceStatus.Missing,
                "under node 2000"
            },
            {
                new IndexRules(IncludesProtected: false),
                Page with
                {
                    Protected = true,
                },
                PresenceStatus.Missing,
                "Public access"
            },
            { new IndexRules(), Page, PresenceStatus.Missing, "out of date" },
        };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Explain_Rules_GivesTheFirstReasonThatApplies(
        IndexRules rules,
        NodeFacts node,
        PresenceStatus status,
        string reason
    )
    {
        // Act
        (PresenceStatus Status, string Reason) result = PresenceRules.Explain(node, rules);

        // Assert
        Assert.Equal(
            (status, true),
            (result.Status, result.Reason.Contains(reason, StringComparison.Ordinal))
        );
    }

    [Theory]
    [InlineData(1060, true)]
    [InlineData(1061, true)]
    [InlineData(106, false)]
    public void IsUnder_AncestorId_ReadsThePath(int ancestor, bool under)
    {
        // Act
        bool result = Page.IsUnder(ancestor);

        // Assert
        Assert.Equal(under, result);
    }
}

public class StaleCheckTests
{
    private static readonly DateTime Saved = new(2026, 10, 2, 12, 0, 0);
    private static readonly DateTime Published = new(2026, 10, 1, 9, 0, 0);

    private static readonly NodeFacts Page = new(
        1061,
        Guid.NewGuid(),
        "Guide",
        "document",
        "contentPage",
        "-1,1061",
        true,
        false,
        false,
        Saved,
        Published,
        [new NodeCulture("da-DK", true, true, Saved, Published)]
    );

    [Theory]
    [InlineData(true, null, "2026-10-01T09:00:00")]
    [InlineData(false, null, "2026-10-02T12:00:00")]
    [InlineData(true, "DA-dk", "2026-10-01T09:00:00")]
    [InlineData(false, "da-DK", "2026-10-02T12:00:00")]
    public void Expected_Index_ComparesPublishedIndexesWithThePublishDate(
        bool publishedOnly,
        string? culture,
        string expected
    )
    {
        // Act
        DateTime? date = StaleCheck.Expected(Page, publishedOnly, culture);

        // Assert
        Assert.Equal(
            DateTime.Parse(expected, System.Globalization.CultureInfo.InvariantCulture),
            date
        );
    }

    [Fact]
    public void Expected_UnknownCulture_IsNull()
    {
        // Act
        DateTime? date = StaleCheck.Expected(Page, true, "fr-FR");

        // Assert
        Assert.Null(date);
    }

    [Theory]
    [InlineData(-2000, true)]
    [InlineData(-500, false)]
    [InlineData(0, false)]
    [InlineData(5000, false)]
    public void IsStale_IndexDate_IsStaleOnlyBeyondTheTolerance(int offsetMilliseconds, bool stale)
    {
        // Act
        bool? result = StaleCheck.IsStale(Saved.AddMilliseconds(offsetMilliseconds), Saved);

        // Assert
        Assert.Equal(stale, result);
    }

    [Fact]
    public void IsStale_NoIndexDate_IsUnknown()
    {
        // Act
        bool? result = StaleCheck.IsStale(null, Saved);

        // Assert
        Assert.Null(result);
    }
}
