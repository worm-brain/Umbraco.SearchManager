using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Umbraco.Cms.Web.Common.Authorization;

namespace Umbraco.Community.SearchManager.Features.Indexes;

/// <summary>
/// Decides whether a user may see member indexes. Member indexes expose login names and email
/// addresses, so like core Umbraco (GHSA-w5q3-9wf8-43gg) they need Members section access on top
/// of the Settings access every endpoint already requires.
/// </summary>
/// <remarks>
/// Implemented here rather than with Umbraco's <c>IMemberIndexAuthorizer</c>, which only exists
/// from 17.6.2 and 18.1.1; the package's floor is 17.0.0.
/// </remarks>
public interface IMemberIndexAccess
{
    /// <summary>Whether <paramref name="user"/> may see member indexes.</summary>
    /// <param name="user">The signed-in backoffice user.</param>
    /// <returns><see langword="true"/> when the user has Members section access.</returns>
    Task<bool> CanSeeMemberIndexesAsync(ClaimsPrincipal user);
}

/// <inheritdoc />
internal sealed class MemberIndexAccess(IAuthorizationService authorizationService)
    : IMemberIndexAccess
{
    /// <inheritdoc />
    public async Task<bool> CanSeeMemberIndexesAsync(ClaimsPrincipal user)
    {
        AuthorizationResult result = await authorizationService.AuthorizeAsync(
            user,
            AuthorizationPolicies.SectionAccessMembers
        );
        return result.Succeeded;
    }
}
