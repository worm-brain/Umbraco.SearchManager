using Examine;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Umbraco.Cms.Core.Services;
using Umbraco.Community.SearchManager.Core.Explore;
using Umbraco.Community.SearchManager.Core.Indexes;
using Umbraco.Community.SearchManager.Features.Explore;

namespace Umbraco.Community.SearchManager.Api.Tests.Features.Explore;

/// <summary>
/// Show query and native mode against a real in-memory Lucene index: the parser Examine's
/// <c>NativeQuery</c> uses decides validity and error positions (ADR 0005).
/// </summary>
public sealed class ExamineExploreSourceNativeTests : IDisposable
{
    private readonly TestLuceneIndex _index = new(
        "ExternalIndex",
        TestLuceneIndex.Content(1, "blogPost", "Umbraco guide", 2, new DateTime(2026, 1, 15)),
        TestLuceneIndex.Content(2, "product", "Red hoodie", 3, new DateTime(2026, 9, 30)),
        TestLuceneIndex.Content(3, "product", "Blue mug", 4, new DateTime(2025, 12, 31))
    );

    private readonly ExamineExploreSource _source;
    private readonly ExploreTarget _target = Target(native: true);

    public ExamineExploreSourceNativeTests()
    {
        IExamineManager examineManager = Substitute.For<IExamineManager>();
        examineManager.Indexes.Returns([_index.Index]);
        examineManager
            .TryGetIndex("ExternalIndex", out Arg.Any<IIndex>()!)
            .Returns(call =>
            {
                call[1] = _index.Index;
                return true;
            });
        _source = new ExamineExploreSource(
            examineManager,
            Array.Empty<IIndexSource>(),
            Substitute.For<ILanguageService>(),
            new FakeTimeProvider(new DateTimeOffset(2026, 10, 12, 12, 0, 0, TimeSpan.Zero))
        );
    }

    /// <summary>
    /// A query that does not parse, and the zero-based position the parser points at: the
    /// offending token, or the end of the query when it ends too soon (an open quote included).
    /// </summary>
    [Theory]
    [InlineData("nodeName:(mug", 13)]
    [InlineData("a ) b", 2)]
    [InlineData("level:[3 TO", 11)]
    [InlineData("nodeName:\"red", 13)]
    public async Task ValidateNativeAsync_QueryThatDoesNotParse_GivesTheErrorPosition(
        string query,
        int position
    )
    {
        // Act
        NativeValidation result = await ValidateAsync(query);

        // Assert
        Assert.Equal((false, (int?)position), (result.Valid, result.Position));
    }

    [Fact]
    public async Task ValidateNativeAsync_ValidQuery_IsValidWithItsWarnings()
    {
        // Act
        NativeValidation result = await ValidateAsync("+nodeName:hoodie +level:3");

        // Assert
        Assert.Equal((true, "needs_range"), (result.Valid, Assert.Single(result.Warnings).Code));
    }

    [Fact]
    public async Task SearchAsync_NumberTermWithoutRange_MatchesNothing()
    {
        // Act: what the needs_range warning says, checked against Lucene.
        ExploreResult result = await NativeSearchAsync("level:3");

        // Assert
        Assert.Equal(0, result.Total);
    }

    [Fact]
    public async Task SearchAsync_LeadingWildcard_IsRejectedByTheParser()
    {
        // Act
        NativeValidation result = await ValidateAsync("*mber");

        // Assert
        Assert.Equal(
            (false, "leading_wildcard"),
            (result.Valid, Assert.Single(result.Warnings).Code)
        );
    }

    [Fact]
    public async Task SearchAsync_Native_RunsTheRawQueryAsTyped()
    {
        // Act
        ExploreResult result = await NativeSearchAsync("level:[3 TO 4] -nodeName:mug");

        // Assert
        Assert.Multiple(
            () => Assert.Equal("level:[3 TO 4] -nodeName:mug", result.Query),
            () => Assert.Equal(["2"], result.Hits.Select(hit => hit.Id))
        );
    }

    [Fact]
    public async Task CompileAsync_Chips_ShowLuceneParsedQueryAndFluentCSharp()
    {
        // Act
        CompiledQuery compiled = await _source.CompileAsync(
            _target,
            new CompileRequest([
                new Filter(FilterKind.Field, "type", FilterOperator.Is, "Product"),
                new Filter(FilterKind.Field, "level", FilterOperator.Equals, "3"),
            ]),
            TestContext.Current.CancellationToken
        );

        // Assert
        Assert.Multiple(
            () => Assert.Equal("+__NodeTypeAlias:Product +level:[3 TO 3]", compiled.Query),
            () => Assert.Equal("+__NodeTypeAlias:product +level:[3 TO 3]", compiled.Parsed),
            () => Assert.True(compiled.Fluent),
            () => Assert.Contains(".And().RangeQuery<int>", compiled.Code, StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task CompileAsync_Native_ShowsNativeQueryCSharp()
    {
        // Act
        CompiledQuery compiled = await _source.CompileAsync(
            _target,
            new CompileRequest([], null, "nodeName:mug OR nodeName:hoodie"),
            TestContext.Current.CancellationToken
        );

        // Assert
        Assert.Equal((false, "nodeName:mug nodeName:hoodie"), (compiled.Fluent, compiled.Parsed));
    }

    [Fact]
    public async Task ReadNativeAsync_CompiledText_BecomesChipsAgain()
    {
        // Act
        IReadOnlyList<Filter> chips = await _source.ReadNativeAsync(
            _target,
            "+__NodeTypeAlias:product -level:[* TO 3]",
            TestContext.Current.CancellationToken
        );

        // Assert
        Assert.Equal(
            [
                new Filter(FilterKind.Field, "__NodeTypeAlias", FilterOperator.Is, "product"),
                new Filter(FilterKind.Field, "level", FilterOperator.AtMost, "3", true),
            ],
            chips
        );
    }

    [Fact]
    public async Task ValidateNativeAsync_TargetWithoutNativeQueries_IsNotSupported()
    {
        // Act
        Task Validate() =>
            _source.ValidateNativeAsync(
                Target(native: false),
                "a",
                TestContext.Current.CancellationToken
            );

        // Assert
        await Assert.ThrowsAsync<NotSupportedException>(Validate);
    }

    /// <inheritdoc />
    public void Dispose() => _index.Dispose();

    private Task<NativeValidation> ValidateAsync(string query) =>
        _source.ValidateNativeAsync(_target, query, TestContext.Current.CancellationToken);

    private Task<ExploreResult> NativeSearchAsync(string native) =>
        _source.SearchAsync(
            _target,
            new ExploreQuery([], Native: native),
            TestContext.Current.CancellationToken
        );

    private static ExploreTarget Target(bool native) =>
        new(
            "ExternalIndex",
            "examine",
            ExploreTargetKind.Index,
            null,
            false,
            new ExploreCapabilities(native, native)
        );
}
