using System.Reflection;
using Umbraco.Cms.Core.Manifest;
using Umbraco.Cms.Infrastructure.Manifest;

namespace Umbraco.Community.SearchManager.UmbracoSearch.Backoffice;

/// <summary>
/// Registers the Search Manager box on Umbraco Search's index page (Umbraco Search's
/// <c>searchIndexDetailBox</c> extension type, right column). The box's module ships with the main
/// package's client; registering it from this adapter means it exists only on sites that have
/// Umbraco Search.
/// </summary>
internal sealed class SearchIndexBoxManifestReader : IPackageManifestReader
{
    /// <summary>The module the main package serves for the box.</summary>
    public const string ElementPath =
        "/App_Plugins/UmbracoCommunitySearchManager/umbraco-search-index-box.js";

    /// <inheritdoc />
    public Task<IEnumerable<PackageManifest>> ReadPackageManifestsAsync()
    {
        var manifest = new PackageManifest
        {
            Id = "Umbraco.Community.SearchManager.UmbracoSearch",
            Name = "Search Manager for Umbraco Search",
            Version = typeof(SearchIndexBoxManifestReader)
                .Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion,
            Extensions =
            [
                new
                {
                    type = "searchIndexDetailBox",
                    alias = "Umbraco.Community.SearchManager.SearchIndexDetailBox",
                    name = "Search Manager Search Index Box",
                    weight = 50,
                    element = ElementPath,
                    meta = new { label = "#searchManager_title", column = "right" },
                },
            ],
        };
        return Task.FromResult<IEnumerable<PackageManifest>>([manifest]);
    }
}
