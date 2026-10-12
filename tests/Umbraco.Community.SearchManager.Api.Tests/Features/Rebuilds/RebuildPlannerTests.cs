using Umbraco.Community.SearchManager.Core.Indexes;
using Umbraco.Community.SearchManager.Core.Rebuilds;
using Umbraco.Community.SearchManager.Features.Rebuilds;

namespace Umbraco.Community.SearchManager.Api.Tests.Features.Rebuilds;

public class RebuildPlannerTests
{
    private static readonly IndexSummary[] Indexes =
    [
        Index("ExternalIndex"),
        Index("MembersIndex", member: true),
        Index("DeliveryApiContentIndex", rebuild: false, reason: "No populator"),
    ];

    [Fact]
    public void Plan_MemberIndexWithoutMembersAccess_IsRefused()
    {
        // Act
        (_, IReadOnlyList<RebuildRefusal> refused) = RebuildPlanner.Plan(
            Indexes,
            ["MembersIndex"],
            canSeeMemberIndexes: false
        );

        // Assert
        Assert.Equal(
            "Rebuilding a member index needs access to the Members section.",
            Assert.Single(refused).Reason
        );
    }

    [Fact]
    public void Plan_MemberIndexWithMembersAccess_IsPlanned()
    {
        // Act
        (IReadOnlyList<RebuildRequest> requests, _) = RebuildPlanner.Plan(
            Indexes,
            ["MembersIndex"],
            canSeeMemberIndexes: true
        );

        // Assert
        Assert.True(Assert.Single(requests).IsMemberIndex);
    }

    [Fact]
    public void Plan_IndexThatCannotRebuild_IsRefusedWithItsReason()
    {
        // Act
        (_, IReadOnlyList<RebuildRefusal> refused) = RebuildPlanner.Plan(
            Indexes,
            ["DeliveryApiContentIndex"],
            canSeeMemberIndexes: true
        );

        // Assert
        Assert.Equal("No populator", Assert.Single(refused).Reason);
    }

    [Fact]
    public void Plan_UnknownIndex_IsRefused()
    {
        // Act
        (_, IReadOnlyList<RebuildRefusal> refused) = RebuildPlanner.Plan(
            Indexes,
            ["Nope"],
            canSeeMemberIndexes: true
        );

        // Assert
        Assert.Equal("Nope", Assert.Single(refused).IndexName);
    }

    [Fact]
    public void Plan_Rebuildable_EstimatesFromTheCurrentCountByDefault()
    {
        // Act
        (IReadOnlyList<RebuildRequest> requests, _) = RebuildPlanner.Plan(
            Indexes,
            ["externalindex", "ExternalIndex"],
            canSeeMemberIndexes: false
        );

        // Assert
        Assert.Equal(
            ("ExternalIndex", 42L),
            (Assert.Single(requests).IndexName, requests[0].EstimatedTotal)
        );
    }

    [Fact]
    public void Plan_PreviousRunKnown_EstimatesFromItsFinalCount()
    {
        // Act
        (IReadOnlyList<RebuildRequest> requests, _) = RebuildPlanner.Plan(
            Indexes,
            ["ExternalIndex"],
            canSeeMemberIndexes: false,
            estimate: _ => 1234
        );

        // Assert
        Assert.Equal(1234L, Assert.Single(requests).EstimatedTotal);
    }

    private static IndexSummary Index(
        string name,
        bool member = false,
        bool rebuild = true,
        string? reason = null
    ) =>
        new(
            name,
            "examine",
            null,
            IndexHealth.Healthy,
            null,
            42,
            0,
            1,
            1,
            new IndexCapabilities(rebuild, true, true, true),
            reason,
            false,
            member,
            new Dictionary<string, string>()
        );
}
