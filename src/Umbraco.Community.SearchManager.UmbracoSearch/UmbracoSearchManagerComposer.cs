using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Infrastructure.Manifest;
using Umbraco.Cms.Search.Core.Notifications;
using Umbraco.Community.SearchManager.Core.Explore;
using Umbraco.Community.SearchManager.Core.Indexes;
using Umbraco.Community.SearchManager.Infrastructure;
using Umbraco.Community.SearchManager.UmbracoSearch.Backoffice;
using Umbraco.Community.SearchManager.UmbracoSearch.Explore;
using Umbraco.Community.SearchManager.UmbracoSearch.Indexes;
using Umbraco.Extensions;

namespace Umbraco.Community.SearchManager.UmbracoSearch;

/// <summary>
/// Adds Umbraco Search to Search Manager: registers the Umbraco Search index and Explore sources. Runs
/// automatically when the adapter package is installed, after the main package's composer so the
/// core Examine indexes are listed first.
/// </summary>
[ComposeAfter(typeof(SearchManagerComposer))]
public sealed class UmbracoSearchManagerComposer : IComposer
{
    /// <summary>Adds the adapter's services.</summary>
    /// <param name="builder">The Umbraco builder.</param>
    public void Compose(IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddSingleton<IIndexSource, UmbracoSearchIndexSource>();

        // Explore: chips compiled to ISearcher.SearchAsync (ADR 0016).
        builder.Services.AddSingleton<IExploreSource, UmbracoSearchExploreSource>();

        // The Search Manager box on Umbraco Search's index page.
        builder.Services.AddSingleton<IPackageManifestReader, SearchIndexBoxManifestReader>();

        // Rebuilds: Umbraco Search only announces its rebuilds through notifications.
        builder.Services.AddSingleton<UmbracoSearchRebuildTracker>();
        builder.Services.AddSingleton<UmbracoSearchExternalRebuildRecorder>();
        builder.AddNotificationHandler<
            IndexRebuildStartingNotification,
            UmbracoSearchRebuildNotificationHandler
        >();
        builder.AddNotificationHandler<
            IndexRebuildCompletedNotification,
            UmbracoSearchRebuildNotificationHandler
        >();
    }
}
