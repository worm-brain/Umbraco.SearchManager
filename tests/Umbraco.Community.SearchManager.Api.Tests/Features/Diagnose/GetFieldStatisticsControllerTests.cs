using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using NSubstitute;
using Umbraco.Community.SearchManager.Core.Diagnose;
using Umbraco.Community.SearchManager.Core.Explore;
using Umbraco.Community.SearchManager.Features.Diagnose.Fields;
using Umbraco.Community.SearchManager.Features.Indexes;

namespace Umbraco.Community.SearchManager.Api.Tests.Features.Diagnose;

public class GetFieldStatisticsControllerTests
{
    private static readonly ExploreTarget Content = Target("ExternalIndex", "examine", false);
    private static readonly ExploreTarget Members = Target("MembersIndex", "examine", false) with
    {
        IsMemberData = true,
    };
    private static readonly ExploreTarget Elastic = Target("ElasticIndex", "elastic", false);

    private readonly IExploreSourceRegistry _registry = Substitute.For<IExploreSourceRegistry>();
    private readonly IMemberIndexAccess _access = Substitute.For<IMemberIndexAccess>();
    private readonly IFieldStatisticsSource _source = Substitute.For<IFieldStatisticsSource>();

    public GetFieldStatisticsControllerTests()
    {
        IExploreSource explore = Substitute.For<IExploreSource>();
        _registry
            .FindAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call =>
                (string)call[0] switch
                {
                    "ExternalIndex" => (Content, explore),
                    "MembersIndex" => (Members, explore),
                    "ElasticIndex" => (Elastic, explore),
                    _ => ((ExploreTarget, IExploreSource)?)null,
                }
            );
        _source.Engine.Returns("examine");
        _source
            .GetFieldStatisticsAsync(Content, Arg.Any<CancellationToken>())
            .Returns(new FieldStatisticsResult(Content, true, null, 5, 0, true, false, null, []));
    }

    [Fact]
    public async Task GetFieldStatistics_Index_ReturnsTheEnginesStatistics()
    {
        // Act
        IActionResult result = await GetAsync("ExternalIndex");

        // Assert
        var statistics = Assert.IsType<FieldStatisticsResult>(
            Assert.IsType<OkObjectResult>(result).Value
        );
        Assert.Equal(5, statistics.DocumentCount);
    }

    [Fact]
    public async Task GetFieldStatistics_UnknownIndex_IsNotFound()
    {
        // Act
        IActionResult result = await GetAsync("NoSuchIndex");

        // Assert
        Assert.Equal(StatusCodes.Status404NotFound, Status(result));
    }

    [Fact]
    public async Task GetFieldStatistics_MemberIndexWithoutMembersAccess_IsForbidden()
    {
        // Act
        IActionResult result = await GetAsync("MembersIndex");

        // Assert
        Assert.Equal(StatusCodes.Status403Forbidden, Status(result));
    }

    [Fact]
    public async Task GetFieldStatistics_EngineWithoutASource_SaysWhyInsteadOfFailing()
    {
        // Act
        IActionResult result = await GetAsync("ElasticIndex");

        // Assert
        var statistics = Assert.IsType<FieldStatisticsResult>(
            Assert.IsType<OkObjectResult>(result).Value
        );
        Assert.False(statistics.Available);
    }

    private Task<IActionResult> GetAsync(string target)
    {
        var controller = new GetFieldStatisticsController(_registry, _access, [_source])
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
        return controller.GetFieldStatistics(target, TestContext.Current.CancellationToken);
    }

    private static int? Status(IActionResult result) =>
        (result as IStatusCodeActionResult)?.StatusCode;

    private static ExploreTarget Target(string name, string engine, bool member) =>
        new(
            name,
            engine,
            ExploreTargetKind.Index,
            null,
            member,
            new ExploreCapabilities(true, true)
        );
}
