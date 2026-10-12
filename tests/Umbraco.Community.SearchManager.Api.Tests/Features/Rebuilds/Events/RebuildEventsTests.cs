using Umbraco.Cms.Core.Models.ServerEvents;
using Umbraco.Community.SearchManager.Core.Rebuilds;
using Umbraco.Community.SearchManager.Features.Rebuilds.Events;

namespace Umbraco.Community.SearchManager.Api.Tests.Features.Rebuilds.Events;

public class RebuildEventsTests
{
    private static readonly Guid RunId = Guid.Parse("8d2b6c43-7a0f-4d64-9a3e-2f0d5c1e9b11");

    [Theory]
    [InlineData(RebuildStatus.Queued, null, RebuildEvents.Queued)]
    [InlineData(RebuildStatus.Running, null, RebuildEvents.Started)]
    [InlineData(RebuildStatus.Running, 120L, RebuildEvents.Progress)]
    [InlineData(RebuildStatus.Succeeded, 220L, RebuildEvents.Succeeded)]
    [InlineData(RebuildStatus.Failed, 12L, RebuildEvents.Failed)]
    [InlineData(RebuildStatus.Cancelled, null, RebuildEvents.Cancelled)]
    public void For_RunInEachState_NamesWhatHappened(
        RebuildStatus status,
        long? written,
        string expected
    )
    {
        // Act
        ServerEvent serverEvent = RebuildEvents.For(Run(status, written, member: false));

        // Assert
        Assert.Equal(expected, serverEvent.EventType);
    }

    [Fact]
    public void For_Run_CarriesTheRunIdAsItsKey()
    {
        // Act
        ServerEvent serverEvent = RebuildEvents.For(Run(RebuildStatus.Queued, null, false));

        // Assert
        Assert.Equal(RunId, serverEvent.Key);
    }

    [Fact]
    public void For_IndexWithoutMemberData_UsesTheRebuildSource()
    {
        // Act
        ServerEvent serverEvent = RebuildEvents.For(Run(RebuildStatus.Queued, null, false));

        // Assert
        Assert.Equal(RebuildEvents.Source, serverEvent.EventSource);
    }

    [Fact]
    public void For_MemberIndex_UsesTheMemberRebuildSource()
    {
        // Act
        ServerEvent serverEvent = RebuildEvents.For(Run(RebuildStatus.Queued, null, true));

        // Assert
        Assert.Equal(RebuildEvents.MemberSource, serverEvent.EventSource);
    }

    private static RebuildRun Run(RebuildStatus status, long? written, bool member) =>
        new(
            RunId,
            "ProductIndex",
            "examine",
            RebuildMode.OneAtATime,
            status,
            "Jack",
            DateTimeOffset.UnixEpoch,
            null,
            null,
            100,
            null,
            written,
            100,
            false,
            member,
            null
        );
}
