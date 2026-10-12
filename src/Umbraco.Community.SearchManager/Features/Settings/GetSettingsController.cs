using Asp.Versioning;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Umbraco.Community.SearchManager.Infrastructure.Api;

namespace Umbraco.Community.SearchManager.Features.Settings;

/// <summary>The settings the backoffice client needs when it starts. Only UI flags are exposed.</summary>
/// <param name="HideCoreExamineDashboard">
/// Whether the client removes the core Examine Management dashboard and its modals (ADR 0017).
/// </param>
public sealed record SearchManagerSettings(bool HideCoreExamineDashboard);

/// <summary><c>GET /settings</c>: the client-facing settings (ADR 0017, as LogExplorer's ADR 0011).</summary>
/// <param name="options">The configured options.</param>
[ApiVersion("1.0")]
[ApiExplorerSettings(GroupName = "Settings")]
public sealed class GetSettingsController(IOptions<SearchManagerOptions> options)
    : SearchManagerApiControllerBase
{
    /// <summary>Reads the settings as configured, defaults applied.</summary>
    /// <returns>The settings.</returns>
    [HttpGet("settings")]
    [ProducesResponseType<SearchManagerSettings>(StatusCodes.Status200OK)]
    public SearchManagerSettings GetSettings() => new(options.Value.HideCoreExamineDashboard);
}
