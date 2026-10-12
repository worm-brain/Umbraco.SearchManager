using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Umbraco.Community.SearchManager.Core.Explore;
using Umbraco.Community.SearchManager.Features.Explore;
using Umbraco.Community.SearchManager.Features.Indexes;

namespace Umbraco.Community.SearchManager.Api.Tests.Features.Explore;

public class ExploreControllerTests
{
    private static readonly ExploreTarget Content = Target("ExternalIndex", member: false);
    private static readonly ExploreTarget Members = Target("MembersIndex", member: true);

    private readonly IExploreSource _source = Substitute.For<IExploreSource>();
    private readonly IExploreSourceRegistry _registry = Substitute.For<IExploreSourceRegistry>();
    private readonly IMemberIndexAccess _access = Substitute.For<IMemberIndexAccess>();

    public ExploreControllerTests()
    {
        _registry.GetTargetsAsync(Arg.Any<CancellationToken>()).Returns([Content, Members]);
        _registry
            .FindAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call =>
                (string)call[0] switch
                {
                    "ExternalIndex" => (Content, _source),
                    "MembersIndex" => (Members, _source),
                    _ => ((ExploreTarget, IExploreSource)?)null,
                }
            );
        _source
            .SearchAsync(
                Arg.Any<ExploreTarget>(),
                Arg.Any<ExploreQuery>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(new ExploreResult(0, [], "*:*", []));
    }

    [Fact]
    public async Task GetExploreTargets_WithoutMembersAccess_LeavesOutMemberData()
    {
        // Arrange
        var controller = With(new GetExploreTargetsController(_registry, _access));

        // Act
        IActionResult result = await controller.GetExploreTargets(
            TestContext.Current.CancellationToken
        );

        // Assert
        ExploreTarget[] targets = Assert.IsType<ExploreTarget[]>(
            Assert.IsType<OkObjectResult>(result).Value
        );
        Assert.Equal(["ExternalIndex"], targets.Select(target => target.Name));
    }

    [Fact]
    public async Task SearchExplore_MemberIndexWithoutMembersAccess_IsForbidden()
    {
        // Act
        IActionResult result = await SearchAsync("MembersIndex", new ExploreQuery([]));

        // Assert
        Assert.Equal(StatusCodes.Status403Forbidden, Status(result));
    }

    [Fact]
    public async Task SearchExplore_MemberIndexWithMembersAccess_Runs()
    {
        // Arrange
        _access.CanSeeMemberIndexesAsync(Arg.Any<ClaimsPrincipal>()).Returns(true);

        // Act
        IActionResult result = await SearchAsync("MembersIndex", new ExploreQuery([]));

        // Assert
        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task SearchExplore_UnknownTarget_IsNotFound()
    {
        // Act
        IActionResult result = await SearchAsync("NoSuchIndex", new ExploreQuery([]));

        // Assert
        Assert.Equal(StatusCodes.Status404NotFound, Status(result));
    }

    [Theory]
    [InlineData(-1, 20)]
    [InlineData(0, 0)]
    [InlineData(0, 101)]
    public async Task SearchExplore_PageOutOfRange_IsBadRequest(int skip, int take)
    {
        // Act
        IActionResult result = await SearchAsync(
            "ExternalIndex",
            new ExploreQuery([], null, skip, take)
        );

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, Status(result));
    }

    [Fact]
    public async Task SearchExplore_InvalidRawLucene_IsBadRequestWithTheParserMessage()
    {
        // Arrange
        _source
            .SearchAsync(
                Arg.Any<ExploreTarget>(),
                Arg.Any<ExploreQuery>(),
                Arg.Any<CancellationToken>()
            )
            .Throws(new InvalidNativeQueryException("Cannot parse 'a:(b'"));

        // Act
        IActionResult result = await SearchAsync("ExternalIndex", new ExploreQuery([]));

        // Assert
        var problem = Assert.IsType<ProblemDetails>(
            Assert.IsType<BadRequestObjectResult>(result).Value
        );
        Assert.Equal("Cannot parse 'a:(b'", problem.Detail);
    }

    [Fact]
    public async Task ParseExploreInput_Input_ReturnsChipsForTheTargetsFields()
    {
        // Arrange
        _source
            .GetSchemaAsync(Content, Arg.Any<CancellationToken>())
            .Returns(new FieldSchema([new IndexField("level", FieldType.Number)], []));
        var controller = With(new ParseExploreInputController(_registry, _access));

        // Act
        IActionResult result = await controller.ParseExploreInput(
            "ExternalIndex",
            new ParseRequest("level:3"),
            TestContext.Current.CancellationToken
        );

        // Assert
        var parsed = Assert.IsType<ParseResult>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(
            new Filter(FilterKind.Field, "level", FilterOperator.Equals, "3"),
            Assert.Single(parsed.Filters)
        );
    }

    [Fact]
    public async Task GetExploreTerms_TargetWithoutTermStatistics_IsBadRequest()
    {
        // Arrange
        _source
            .GetTermsAsync(
                Arg.Any<ExploreTarget>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            )
            .Throws(new NotSupportedException("not Lucene"));

        // Act
        IActionResult result = await With(new GetExploreTermsController(_registry, _access))
            .GetExploreTerms("ExternalIndex", "type", null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, Status(result));
    }

    [Fact]
    public async Task GetExploreTerms_UnknownField_IsNotFound()
    {
        // Act
        IActionResult result = await With(new GetExploreTermsController(_registry, _access))
            .GetExploreTerms(
                "ExternalIndex",
                "colour",
                null,
                TestContext.Current.CancellationToken
            );

        // Assert
        Assert.Equal(StatusCodes.Status404NotFound, Status(result));
    }

    [Fact]
    public async Task GetExploreFacets_TooManyFields_IsBadRequest()
    {
        // Act
        IActionResult result = await With(new GetExploreFacetsController(_registry, _access))
            .GetExploreFacets(
                "ExternalIndex",
                new FacetsQuery([], null, [.. Enumerable.Range(0, 21).Select(i => $"f{i}")]),
                TestContext.Current.CancellationToken
            );

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, Status(result));
    }

    [Fact]
    public async Task GetExploreFacets_MemberIndexWithoutMembersAccess_IsForbidden()
    {
        // Act
        IActionResult result = await With(new GetExploreFacetsController(_registry, _access))
            .GetExploreFacets(
                "MembersIndex",
                new FacetsQuery([], null, ["type"]),
                TestContext.Current.CancellationToken
            );

        // Assert
        Assert.Equal(StatusCodes.Status403Forbidden, Status(result));
    }

    [Fact]
    public async Task ValidateNativeQuery_TargetWithoutNativeQueries_IsBadRequest()
    {
        // Arrange
        _source
            .ValidateNativeAsync(
                Arg.Any<ExploreTarget>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Throws(new NotSupportedException("not Lucene"));

        // Act
        IActionResult result = await With(new ValidateNativeQueryController(_registry, _access))
            .ValidateNativeQuery(
                "ExternalIndex",
                new NativeRequest("a"),
                TestContext.Current.CancellationToken
            );

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, Status(result));
    }

    [Fact]
    public async Task CompileExplore_MemberIndexWithoutMembersAccess_IsForbidden()
    {
        // Act
        IActionResult result = await With(new CompileExploreController(_registry, _access))
            .CompileExplore(
                "MembersIndex",
                new CompileRequest([]),
                TestContext.Current.CancellationToken
            );

        // Assert
        Assert.Equal(StatusCodes.Status403Forbidden, Status(result));
    }

    [Fact]
    public async Task ReadNativeQuery_Query_ReturnsTheSourcesChips()
    {
        // Arrange
        Filter chip = new(FilterKind.Field, "level", FilterOperator.Equals, "3");
        _source
            .ReadNativeAsync(Content, "+level:[3 TO 3]", Arg.Any<CancellationToken>())
            .Returns([chip]);

        // Act
        IActionResult result = await With(new ReadNativeQueryController(_registry, _access))
            .ReadNativeQuery(
                "ExternalIndex",
                new NativeRequest("+level:[3 TO 3]"),
                TestContext.Current.CancellationToken
            );

        // Assert
        var parsed = Assert.IsType<ParseResult>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal([chip], parsed.Filters);
    }

    private Task<IActionResult> SearchAsync(string target, ExploreQuery query) =>
        With(new SearchExploreController(_registry, _access))
            .SearchExplore(target, query, TestContext.Current.CancellationToken);

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
