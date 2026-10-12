using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Umbraco.Community.SearchManager.Core.Rebuilds;
using Umbraco.Community.SearchManager.Features.Indexes;
using Umbraco.Community.SearchManager.Features.Rebuilds;

namespace Umbraco.Community.SearchManager.Api.Tests.Features.Rebuilds;

public sealed class RebuildsControllersTests : IDisposable
{
    private readonly RebuildQueue _queue = new(TimeProvider.System);

    public RebuildsControllersTests() =>
        _queue.Enqueue(
            [
                new RebuildRequest("ExternalIndex", "examine", 1, 1, false, false),
                new RebuildRequest("MembersIndex", "examine", 1, 1, false, true),
            ],
            RebuildMode.OneAtATime,
            "Jack"
        );

    public void Dispose() => _queue.Dispose();

    [Fact]
    public async Task GetRebuilds_UserWithoutMembersAccess_LeavesOutMemberIndexRuns()
    {
        // Arrange
        var controller = new GetRebuildsController(_queue, Access(false))
        {
            ControllerContext = Context(),
        };

        // Act
        IActionResult result = await controller.GetRebuilds();

        // Assert
        RebuildRun[] runs = Assert.IsType<RebuildRun[]>(
            Assert.IsType<OkObjectResult>(result).Value
        );
        Assert.Equal(["ExternalIndex"], runs.Select(run => run.IndexName));
    }

    [Fact]
    public async Task CancelRebuild_RunningRun_IsAConflict()
    {
        // Arrange
        RebuildRun running = _queue.StartNext()[0];
        var controller = new CancelRebuildController(_queue, Access(true))
        {
            ControllerContext = Context(),
        };

        // Act
        IActionResult result = await controller.CancelRebuild(running.Id);

        // Assert
        Assert.IsType<ConflictObjectResult>(result);
    }

    [Fact]
    public async Task CancelRebuild_MemberRunWithoutMembersAccess_IsNotFound()
    {
        // Arrange
        RebuildRun memberRun = _queue.GetRuns().Single(run => run.IsMemberIndex);
        var controller = new CancelRebuildController(_queue, Access(false))
        {
            ControllerContext = Context(),
        };

        // Act
        IActionResult result = await controller.CancelRebuild(memberRun.Id);

        // Assert
        Assert.IsType<NotFoundObjectResult>(result);
    }

    private static IMemberIndexAccess Access(bool members)
    {
        IMemberIndexAccess access = Substitute.For<IMemberIndexAccess>();
        access.CanSeeMemberIndexesAsync(Arg.Any<ClaimsPrincipal>()).Returns(members);
        return access;
    }

    private static ControllerContext Context() => new() { HttpContext = new DefaultHttpContext() };
}
