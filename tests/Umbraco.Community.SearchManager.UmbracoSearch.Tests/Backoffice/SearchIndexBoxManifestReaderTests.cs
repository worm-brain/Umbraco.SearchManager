using System.Text.Json;
using Umbraco.Cms.Core.Manifest;
using Umbraco.Community.SearchManager.UmbracoSearch.Backoffice;

namespace Umbraco.Community.SearchManager.UmbracoSearch.Tests.Backoffice;

public class SearchIndexBoxManifestReaderTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task ReadPackageManifestsAsync_Always_RegistersTheBoxInTheRightColumn()
    {
        // Act
        JsonElement extension = await ReadExtensionAsync();

        // Assert
        Assert.Equal(
            ("searchIndexDetailBox", "right"),
            (
                extension.GetProperty("type").GetString(),
                extension.GetProperty("meta").GetProperty("column").GetString()
            )
        );
    }

    [Fact]
    public async Task ReadPackageManifestsAsync_Always_PointsAtTheBoxModuleTheMainPackageServes()
    {
        // Act
        JsonElement extension = await ReadExtensionAsync();

        // Assert
        Assert.Equal(
            "/App_Plugins/UmbracoCommunitySearchManager/umbraco-search-index-box.js",
            extension.GetProperty("element").GetString()
        );
    }

    private static async Task<JsonElement> ReadExtensionAsync()
    {
        PackageManifest manifest = Assert.Single(
            await new SearchIndexBoxManifestReader().ReadPackageManifestsAsync()
        );
        return JsonSerializer.SerializeToElement(Assert.Single(manifest.Extensions), Web);
    }
}
