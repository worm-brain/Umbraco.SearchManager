using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Umbraco.Cms.Core.ServerEvents;
using Umbraco.Cms.Web.Common.Authorization;

namespace Umbraco.Community.SearchManager.Features.Rebuilds.Events;

/// <summary>
/// Decides which backoffice users join the rebuild event sources when they connect to Umbraco's
/// server event hub: Settings section access for <see cref="RebuildEvents.Source"/>, and Settings
/// plus Members section access for <see cref="RebuildEvents.MemberSource"/>. Umbraco sends a
/// routed event only to the users in its source's group.
/// </summary>
/// <param name="authorizationService">Evaluates Umbraco's section access policies.</param>
public sealed class RebuildEventAuthorizer(IAuthorizationService authorizationService)
    : IEventSourceAuthorizer
{
    /// <inheritdoc />
    public IEnumerable<string> AuthorizableEventSources =>
        [RebuildEvents.Source, RebuildEvents.MemberSource];

    /// <inheritdoc />
    public async Task<bool> AuthorizeAsync(ClaimsPrincipal principal, string eventSource)
    {
        if (eventSource is not (RebuildEvents.Source or RebuildEvents.MemberSource))
        {
            return false;
        }

        if (!await HasAsync(principal, AuthorizationPolicies.SectionAccessSettings))
        {
            return false;
        }

        return eventSource is RebuildEvents.Source
            || await HasAsync(principal, AuthorizationPolicies.SectionAccessMembers);
    }

    /// <summary>Evaluates one policy for the user.</summary>
    /// <param name="principal">The user.</param>
    /// <param name="policy">The policy name.</param>
    /// <returns>Whether the user meets it.</returns>
    private async Task<bool> HasAsync(ClaimsPrincipal principal, string policy) =>
        (await authorizationService.AuthorizeAsync(principal, policy)).Succeeded;
}
