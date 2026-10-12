using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Search.Core.Notifications;
using Umbraco.Community.SearchManager.Core.Indexes;
using Umbraco.Community.SearchManager.Infrastructure;
using Umbraco.Community.SearchManager.UmbracoSearch.Indexes;
using Umbraco.Extensions;

namespace Umbraco.Community.SearchManager.UmbracoSearch;

/// <summary>
/// Adds Umbraco Search to Search Manager: registers the Umbraco Search index source. Runs
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
