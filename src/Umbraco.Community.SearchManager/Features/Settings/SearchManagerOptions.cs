namespace Umbraco.Community.SearchManager.Features.Settings;

/// <summary>
/// Search Manager's settings, bound from the <c>SearchManager</c> configuration section
/// (<c>appsettings.json</c> or any other configuration source). Every setting is optional.
/// </summary>
public sealed class SearchManagerOptions
{
    /// <summary>The configuration section the options bind from.</summary>
    public const string SectionName = "SearchManager";

    /// <summary>
    /// Whether the backoffice hides the core Examine Management dashboard (Settings section) and
    /// its two modals, so Settings has one place for indexes (ADR 0017). Defaults to
    /// <see langword="true"/>; <see langword="false"/> shows both.
    /// </summary>
    public bool HideCoreExamineDashboard { get; set; } = true;
}
