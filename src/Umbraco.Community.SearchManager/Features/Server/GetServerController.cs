using System.Text.Json.Serialization;
using Asp.Versioning;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Sync;
using Umbraco.Community.SearchManager.Infrastructure.Api;

namespace Umbraco.Community.SearchManager.Features.Server;

/// <summary>The server the backoffice user is on, and whether the site runs on more than one.</summary>
/// <param name="MachineName">This server's machine name (the name rebuild history records).</param>
/// <param name="Role">This server's role in Umbraco's load balancing.</param>
/// <param name="ActiveServers">Active server registrations, at least 1 (this server).</param>
/// <param name="IsLoadBalanced">Whether other servers serve the site, so core Examine rebuilds miss them.</param>
public sealed record ServerInfo(
    string MachineName,
    [property: JsonConverter(typeof(JsonStringEnumConverter<ServerRole>))] ServerRole Role,
    int ActiveServers,
    bool IsLoadBalanced
)
{
    /// <summary>
    /// Whether other servers serve the site: this server is a subscriber (so a scheduling
    /// publisher exists elsewhere), or more than one server is registered as active.
    /// </summary>
    /// <param name="role">This server's role.</param>
    /// <param name="activeServers">Active server registrations.</param>
    /// <returns><see langword="true"/> when other servers exist.</returns>
    public static bool DetectLoadBalancing(ServerRole role, int activeServers) =>
        role is ServerRole.Subscriber || activeServers > 1;
}

/// <summary><c>GET /server</c>: the server the user is on, for the Indexes view and the rebuild warning.</summary>
/// <param name="serverRoleAccessor">This server's role.</param>
/// <param name="serverRegistrationService">The server registrations (the <c>umbracoServer</c> table).</param>
[ApiVersion("1.0")]
[ApiExplorerSettings(GroupName = "Server")]
public sealed class GetServerController(
    IServerRoleAccessor serverRoleAccessor,
    IServerRegistrationService serverRegistrationService
) : SearchManagerApiControllerBase
{
    /// <summary>Describes this server and whether the site is load balanced.</summary>
    /// <returns>The server.</returns>
    [HttpGet("server")]
    [ProducesResponseType<ServerInfo>(StatusCodes.Status200OK)]
    public IActionResult GetServer()
    {
        ServerRole role = serverRoleAccessor.CurrentServerRole;
        int active = Math.Max(1, CountActiveServers());
        return Ok(
            new ServerInfo(
                Environment.MachineName,
                role,
                active,
                ServerInfo.DetectLoadBalancing(role, active)
            )
        );
    }

    /// <summary>
    /// Counts active server registrations; a registration table that cannot be read counts as
    /// none, so the view still loads.
    /// </summary>
    /// <returns>The count.</returns>
    private int CountActiveServers()
    {
        try
        {
            return serverRegistrationService.GetActiveServers().Count();
        }
#pragma warning disable CA1031 // The server line is informational; it must never break the view.
        catch (Exception)
#pragma warning restore CA1031
        {
            return 0;
        }
    }
}
