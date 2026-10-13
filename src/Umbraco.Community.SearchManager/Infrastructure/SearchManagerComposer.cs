using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Community.SearchManager.Core.Diagnose;
using Umbraco.Community.SearchManager.Core.Explore;
using Umbraco.Community.SearchManager.Core.Indexes;
using Umbraco.Community.SearchManager.Core.Rebuilds;
using Umbraco.Community.SearchManager.Features.Diagnose.Fields;
using Umbraco.Community.SearchManager.Features.Explore;
using Umbraco.Community.SearchManager.Features.Indexes;
using Umbraco.Community.SearchManager.Features.Rebuilds;
using Umbraco.Community.SearchManager.Features.Rebuilds.Events;
using Umbraco.Community.SearchManager.Features.Rebuilds.History;
using Umbraco.Community.SearchManager.Features.Settings;

namespace Umbraco.Community.SearchManager.Infrastructure;

/// <summary>
/// Registers Search Manager with Umbraco: the source registry and the core Examine source.
/// Runs automatically when the package is installed; source packages (such as the Umbraco Search
/// adapter) add their own <see cref="IIndexSource"/> the same way.
/// </summary>
public sealed class SearchManagerComposer : IComposer
{
    /// <summary>Adds the package's services.</summary>
    /// <param name="builder">The Umbraco builder.</param>
    public void Compose(IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Settings: the SearchManager configuration section (ADR 0017).
        builder.Services.Configure<SearchManagerOptions>(
            builder.Config.GetSection(SearchManagerOptions.SectionName)
        );

        builder.Services.AddSingleton<IIndexSourceRegistry, IndexSourceRegistry>();
        builder.Services.AddSingleton<IIndexSource, ExamineIndexSource>();
        builder.Services.AddScoped<IMemberIndexAccess, MemberIndexAccess>();

        // Explore: one source per engine behind a registry (ADR 0002); core Examine's is here.
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<IExploreSourceRegistry, ExploreSourceRegistry>();
        builder.Services.AddSingleton<IExploreSource, ExamineExploreSource>();

        // Diagnose: one small source per engine and tool, keyed by Explore's targets (ADR 0018).
        builder.Services.AddSingleton<FieldStatisticsCache>();
        builder.Services.AddSingleton<IFieldStatisticsSource, ExamineFieldStatisticsSource>();

        // Rebuild queue: in-memory status, drained by a hosted service on this server.
        builder.Services.AddSingleton<IRebuildQueue, RebuildQueue>();
        builder.Services.AddSingleton<RebuildRunner>();
        builder.Services.AddHostedService<RebuildWorker>();

        // Live status: queue changes become server events for users the authorizer admits.
        builder.Services.AddHostedService<RebuildEventBroadcaster>();
        builder.EventSourceAuthorizers().Append<RebuildEventAuthorizer>();

        // Rebuild history: a package migration creates the table; the repository reads and prunes it.
        builder.Services.AddSingleton<IRebuildHistory, RebuildHistoryRepository>();
    }
}
