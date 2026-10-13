using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using NSubstitute;
using Umbraco.Community.SearchManager.Core.Diagnose;
using Umbraco.Community.SearchManager.Core.Explore;
using Umbraco.Community.SearchManager.Features.Diagnose.Analysis;
using Umbraco.Community.SearchManager.Features.Indexes;

namespace Umbraco.Community.SearchManager.Api.Tests.Features.Diagnose;

public class AnalyzerControllersTests
{
    private static readonly ExploreTarget Content = Target("ExternalIndex", "examine");
    private static readonly ExploreTarget Members = Target("MembersIndex", "examine") with
    {
        IsMemberData = true,
    };
    private static readonly ExploreTarget Elastic = Target("ElasticIndex", "elastic");

    private static readonly AnalyzeRequest Request = new("nodeName", "Opening hours", "open");

    private readonly IExploreSourceRegistry _registry = Substitute.For<IExploreSourceRegistry>();
    private readonly IMemberIndexAccess _access = Substitute.For<IMemberIndexAccess>();
    private readonly IAnalyzerSource _source = Substitute.For<IAnalyzerSource>();

    public AnalyzerControllersTests()
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
            .GetFieldsAsync(Content, Arg.Any<CancellationToken>())
            .Returns(new AnalyzerFieldsResult(Content, true, null, "nodeName", []));
        _source
            .AnalyzeAsync(Content, Request, Arg.Any<CancellationToken>())
            .Returns(
                new AnalysisResult(
                    Content,
                    true,
                    null,
                    "nodeName",
                    "StandardAnalyzer",
                    true,
                    null,
                    [],
                    null,
                    null,
                    false
                )
            );
    }

    [Fact]
    public async Task GetAnalyzerFields_Index_ReturnsTheEnginesFields()
    {
        // Act
        IActionResult result = await Fields().GetAnalyzerFields("ExternalIndex", Cancel);

        // Assert
        Assert.Equal("nodeName", Assert.IsType<AnalyzerFieldsResult>(Ok(result)).DefaultField);
    }

    [Fact]
    public async Task GetAnalyzerFields_UnknownIndex_IsNotFound()
    {
        // Act
        IActionResult result = await Fields().GetAnalyzerFields("NoSuchIndex", Cancel);

        // Assert
        Assert.Equal(StatusCodes.Status404NotFound, Status(result));
    }

    [Fact]
    public async Task GetAnalyzerFields_EngineWithoutASource_SaysWhyInsteadOfFailing()
    {
        // Act
        IActionResult result = await Fields().GetAnalyzerFields("ElasticIndex", Cancel);

        // Assert
        Assert.Equal(
            "The analyzer is not available for elastic indexes.",
            Assert.IsType<AnalyzerFieldsResult>(Ok(result)).UnavailableReason
        );
    }

    [Fact]
    public async Task Analyze_Index_ReturnsTheEnginesAnalysis()
    {
        // Act
        IActionResult result = await Analyze().Analyze("ExternalIndex", Request, Cancel);

        // Assert
        Assert.Equal("StandardAnalyzer", Assert.IsType<AnalysisResult>(Ok(result)).Analyzer);
    }

    [Fact]
    public async Task Analyze_MemberIndexWithoutMembersAccess_IsForbidden()
    {
        // Act
        IActionResult result = await Analyze().Analyze("MembersIndex", Request, Cancel);

        // Assert
        Assert.Equal(StatusCodes.Status403Forbidden, Status(result));
    }

    [Fact]
    public async Task Analyze_UnknownIndex_IsNotFound()
    {
        // Act
        IActionResult result = await Analyze().Analyze("NoSuchIndex", Request, Cancel);

        // Assert
        Assert.Equal(StatusCodes.Status404NotFound, Status(result));
    }

    public static TheoryData<AnalyzeRequest> InvalidRequests =>
        new()
        {
            new AnalyzeRequest("", "Opening hours", null),
            new AnalyzeRequest("nodeName", " ", null),
            new AnalyzeRequest(
                "nodeName",
                new string('a', AnalyzeTextController.MaxTextLength + 1),
                null
            ),
            new AnalyzeRequest(
                "nodeName",
                "Opening hours",
                new string('a', AnalyzeTextController.MaxSearchTermLength + 1)
            ),
        };

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public async Task Analyze_InvalidRequest_IsBadRequest(AnalyzeRequest request)
    {
        // Act
        IActionResult result = await Analyze().Analyze("ExternalIndex", request, Cancel);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, Status(result));
    }

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private GetAnalyzerFieldsController Fields() =>
        new(_registry, _access, [_source])
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

    private AnalyzeTextController Analyze() =>
        new(_registry, _access, [_source])
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

    private static object? Ok(IActionResult result) => Assert.IsType<OkObjectResult>(result).Value;

    private static int? Status(IActionResult result) =>
        (result as IStatusCodeActionResult)?.StatusCode;

    private static ExploreTarget Target(string name, string engine) =>
        new(
            name,
            engine,
            ExploreTargetKind.Index,
            null,
            false,
            new ExploreCapabilities(true, true)
        );
}
