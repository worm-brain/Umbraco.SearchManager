using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using NSubstitute;
using Umbraco.Cms.Web.Common.Authorization;
using Umbraco.Community.SearchManager.Features.Rebuilds.Events;

namespace Umbraco.Community.SearchManager.Api.Tests.Features.Rebuilds.Events;

public class RebuildEventAuthorizerTests
{
    private readonly IAuthorizationService _authorization = Substitute.For<IAuthorizationService>();
    private readonly ClaimsPrincipal _user = new(new ClaimsIdentity("test"));

    public RebuildEventAuthorizerTests() =>
        _authorization
            .AuthorizeAsync(default!, default, default(string)!)
            .ReturnsForAnyArgs(AuthorizationResult.Failed());

    [Fact]
    public void AuthorizableEventSources_ListsBothRebuildSources()
    {
        // Act
        IEnumerable<string> sources = new RebuildEventAuthorizer(
            _authorization
        ).AuthorizableEventSources;

        // Assert
        Assert.Equal([RebuildEvents.Source, RebuildEvents.MemberSource], sources);
    }

    [Fact]
    public async Task AuthorizeAsync_RebuildsWithSettingsAccess_IsAllowed()
    {
        // Arrange
        Grant(AuthorizationPolicies.SectionAccessSettings);

        // Act
        bool allowed = await Authorize(RebuildEvents.Source);

        // Assert
        Assert.True(allowed);
    }

    [Fact]
    public async Task AuthorizeAsync_RebuildsWithoutSettingsAccess_IsRefused()
    {
        // Act
        bool allowed = await Authorize(RebuildEvents.Source);

        // Assert
        Assert.False(allowed);
    }

    [Fact]
    public async Task AuthorizeAsync_MemberRebuildsWithSettingsButNotMembersAccess_IsRefused()
    {
        // Arrange
        Grant(AuthorizationPolicies.SectionAccessSettings);

        // Act
        bool allowed = await Authorize(RebuildEvents.MemberSource);

        // Assert
        Assert.False(allowed);
    }

    [Fact]
    public async Task AuthorizeAsync_MemberRebuildsWithSettingsAndMembersAccess_IsAllowed()
    {
        // Arrange
        Grant(AuthorizationPolicies.SectionAccessSettings);
        Grant(AuthorizationPolicies.SectionAccessMembers);

        // Act
        bool allowed = await Authorize(RebuildEvents.MemberSource);

        // Assert
        Assert.True(allowed);
    }

    [Fact]
    public async Task AuthorizeAsync_MemberRebuildsWithMembersButNotSettingsAccess_IsRefused()
    {
        // Arrange
        Grant(AuthorizationPolicies.SectionAccessMembers);

        // Act
        bool allowed = await Authorize(RebuildEvents.MemberSource);

        // Assert
        Assert.False(allowed);
    }

    [Fact]
    public async Task AuthorizeAsync_UnknownSource_IsRefused()
    {
        // Arrange
        Grant(AuthorizationPolicies.SectionAccessSettings);
        Grant(AuthorizationPolicies.SectionAccessMembers);

        // Act
        bool allowed = await Authorize("Umbraco:CMS:Document");

        // Assert
        Assert.False(allowed);
    }

    private Task<bool> Authorize(string source) =>
        new RebuildEventAuthorizer(_authorization).AuthorizeAsync(_user, source);

    private void Grant(string policy) =>
        _authorization
            .AuthorizeAsync(_user, Arg.Any<object?>(), policy)
            .Returns(AuthorizationResult.Success());
}
