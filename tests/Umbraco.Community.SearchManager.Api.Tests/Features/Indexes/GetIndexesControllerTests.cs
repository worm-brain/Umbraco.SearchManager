using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Umbraco.Community.SearchManager.Core.Indexes;
using Umbraco.Community.SearchManager.Features.Indexes;

namespace Umbraco.Community.SearchManager.Api.Tests.Features.Indexes;

public class GetIndexesControllerTests
{
    [Fact]
    public async Task GetIndexes_UserWithoutMembersAccess_LeavesOutMemberIndexes()
    {
        // Arrange
        GetIndexesController controller = CreateController(membersAccess: false);

        // Act
        IndexSummary[] indexes = await GetAsync(controller);

        // Assert
        Assert.Equal(["ExternalIndex"], indexes.Select(index => index.Name));
    }

    [Fact]
    public async Task GetIndexes_UserWithMembersAccess_IncludesMemberIndexes()
    {
        // Arrange
        GetIndexesController controller = CreateController(membersAccess: true);

        // Act
        IndexSummary[] indexes = await GetAsync(controller);

        // Assert
        Assert.Equal(["ExternalIndex", "MembersIndex"], indexes.Select(index => index.Name));
    }

    private static GetIndexesController CreateController(bool membersAccess)
    {
        IIndexSourceRegistry registry = Substitute.For<IIndexSourceRegistry>();
        registry
            .GetIndexesAsync(Arg.Any<CancellationToken>())
            .Returns([
                Summary("ExternalIndex", member: false),
                Summary("MembersIndex", member: true),
            ]);

        IMemberIndexAccess access = Substitute.For<IMemberIndexAccess>();
        access.CanSeeMemberIndexesAsync(Arg.Any<ClaimsPrincipal>()).Returns(membersAccess);

        return new GetIndexesController(registry, access)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
    }

    private static async Task<IndexSummary[]> GetAsync(GetIndexesController controller)
    {
        IActionResult result = await controller.GetIndexes(TestContext.Current.CancellationToken);
        return Assert.IsType<IndexSummary[]>(Assert.IsType<OkObjectResult>(result).Value);
    }

    private static IndexSummary Summary(string name, bool member) =>
        new(
            name,
            "examine",
            null,
            IndexHealth.Healthy,
            null,
            1,
            0,
            1,
            1,
            new IndexCapabilities(true, true, true, true),
            null,
            false,
            member,
            new Dictionary<string, string>()
        );
}
