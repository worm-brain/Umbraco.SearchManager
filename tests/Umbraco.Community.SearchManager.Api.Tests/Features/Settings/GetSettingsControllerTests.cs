using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Umbraco.Community.SearchManager.Features.Settings;

namespace Umbraco.Community.SearchManager.Api.Tests.Features.Settings;

public class GetSettingsControllerTests
{
    [Fact]
    public void GetSettings_DefaultOptions_HidesTheCoreExamineDashboard()
    {
        // Arrange
        var controller = new GetSettingsController(Options.Create(new SearchManagerOptions()));

        // Act
        SearchManagerSettings settings = controller.GetSettings();

        // Assert
        Assert.True(settings.HideCoreExamineDashboard);
    }

    [Fact]
    public void GetSettings_SettingFalseInConfiguration_KeepsTheCoreExamineDashboard()
    {
        // Arrange
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["SearchManager:HideCoreExamineDashboard"] = "false",
                }
            )
            .Build();
        SearchManagerOptions options =
            configuration.GetSection(SearchManagerOptions.SectionName).Get<SearchManagerOptions>()
            ?? new SearchManagerOptions();
        var controller = new GetSettingsController(Options.Create(options));

        // Act
        SearchManagerSettings settings = controller.GetSettings();

        // Assert
        Assert.False(settings.HideCoreExamineDashboard);
    }
}
