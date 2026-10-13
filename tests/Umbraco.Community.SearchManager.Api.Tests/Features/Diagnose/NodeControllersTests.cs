using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using NSubstitute;
using Umbraco.Community.SearchManager.Core.Diagnose;
using Umbraco.Community.SearchManager.Core.Explore;
using Umbraco.Community.SearchManager.Features.Diagnose.Nodes;
using Umbraco.Community.SearchManager.Features.Indexes;

namespace Umbraco.Community.SearchManager.Api.Tests.Features.Diagnose;

public class NodeControllersTests
{
    private static readonly Guid Key = Guid.Parse("c8addc1f-0a30-4bd9-849d-243ae8ab24a2");
    private static readonly ExploreTarget Content = Target("ExternalIndex", member: false);
    private static readonly ExploreTarget Members = Target("MembersIndex", member: true);

    private static readonly NodeFacts Guide = new(
        1061,
        Key,
        "Guide",
        "document",
        "contentPage",
        "-1,1061",
        true,
        false,
        false,
        DateTime.UnixEpoch,
        null,
        []
    );

    private readonly IExploreSourceRegistry _registry = Substitute.For<IExploreSourceRegistry>();
    private readonly IMemberIndexAccess _access = Substitute.For<IMemberIndexAccess>();
    private readonly INodeFactsReader _reader = Substitute.For<INodeFactsReader>();
    private readonly INodePresenceSource _source = Substitute.For<INodePresenceSource>();

    public NodeControllersTests()
    {
        IExploreSource explore = Substitute.For<IExploreSource>();
        _registry.GetTargetsAsync(Arg.Any<CancellationToken>()).Returns([Content, Members]);
        _registry
            .FindAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call =>
                (string)call[0] switch
                {
                    "ExternalIndex" => (Content, explore),
                    "MembersIndex" => (Members, explore),
                    _ => ((ExploreTarget, IExploreSource)?)null,
                }
            );
        _reader.Read(Key).Returns(Guide);
        _source.Engine.Returns("examine");
        _source
            .GetPresenceAsync(
                Guide,
                Arg.Any<IReadOnlyList<ExploreTarget>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
                [
                    .. call.Arg<IReadOnlyList<ExploreTarget>>()
                        .Select(target => new IndexPresence(
                            target.Name,
                            target.Engine,
                            PresenceStatus.Present,
                            1,
                            [],
                            null
                        )),
                ]
            );
    }

    [Fact]
    public async Task GetNodePresence_WithoutMembersAccess_LeavesOutMemberIndexes()
    {
        // Act
        IActionResult result = await PresenceAsync(Key);

        // Assert
        var presence = Assert.IsType<NodePresenceResult>(
            Assert.IsType<OkObjectResult>(result).Value
        );
        Assert.Equal(["ExternalIndex"], presence.Indexes.Select(index => index.Target));
    }

    [Fact]
    public async Task GetNodePresence_WithMembersAccess_ChecksMemberIndexesToo()
    {
        // Arrange
        _access.CanSeeMemberIndexesAsync(Arg.Any<ClaimsPrincipal>()).Returns(true);

        // Act
        IActionResult result = await PresenceAsync(Key);

        // Assert
        var presence = Assert.IsType<NodePresenceResult>(
            Assert.IsType<OkObjectResult>(result).Value
        );
        Assert.Equal(2, presence.Indexes.Count);
    }

    [Fact]
    public async Task GetNodePresence_UnknownDocument_IsNotFound()
    {
        // Act
        IActionResult result = await PresenceAsync(Guid.NewGuid());

        // Assert
        Assert.Equal(StatusCodes.Status404NotFound, Status(result));
    }

    [Fact]
    public async Task MatchNode_MemberIndexWithoutMembersAccess_IsForbidden()
    {
        // Act
        IActionResult result = await MatchAsync("MembersIndex", Key);

        // Assert
        Assert.Equal(StatusCodes.Status403Forbidden, Status(result));
    }

    [Fact]
    public async Task MatchNode_UnknownIndex_IsNotFound()
    {
        // Act
        IActionResult result = await MatchAsync("NoSuchIndex", Key);

        // Assert
        Assert.Equal(StatusCodes.Status404NotFound, Status(result));
    }

    [Fact]
    public async Task MatchNode_Index_ReturnsTheSourcesVerdict()
    {
        // Arrange
        _source
            .MatchAsync(Content, Guide, Arg.Any<NodeMatchRequest>(), Arg.Any<CancellationToken>())
            .Returns(new NodeMatchResult(true, true, "*:*", []));

        // Act
        IActionResult result = await MatchAsync("ExternalIndex", Key);

        // Assert
        var match = Assert.IsType<NodeMatchResult>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.True(match.Matches);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(51)]
    public void FindNodes_TakeOutOfRange_IsBadRequest(int take)
    {
        // Act
        IActionResult result = With(new FindNodesController(_registry, _access, _reader))
            .FindNodes("guide", take);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, Status(result));
    }

    private Task<IActionResult> PresenceAsync(Guid key) =>
        With(new GetNodePresenceController(_registry, _access, _reader, [_source]))
            .GetNodePresence(key, TestContext.Current.CancellationToken);

    private Task<IActionResult> MatchAsync(string target, Guid key) =>
        With(new MatchNodeController(_registry, _access, _reader, [_source]))
            .MatchNode(
                target,
                key,
                new NodeMatchRequest([]),
                TestContext.Current.CancellationToken
            );

    private static T With<T>(T controller)
        where T : ControllerBase
    {
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext(),
        };
        return controller;
    }

    private static int? Status(IActionResult result) =>
        (result as IStatusCodeActionResult)?.StatusCode;

    private static ExploreTarget Target(string name, bool member) =>
        new(
            name,
            "examine",
            ExploreTargetKind.Index,
            null,
            member,
            new ExploreCapabilities(true, true)
        );
}
