using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Community.SearchManager.Core.Indexes;
using Umbraco.Community.SearchManager.Core.Rebuilds;
using Umbraco.Community.SearchManager.Features.Indexes;
using Umbraco.Community.SearchManager.Features.Rebuilds;

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

        builder.Services.AddSingleton<IIndexSourceRegistry, IndexSourceRegistry>();
        builder.Services.AddSingleton<IIndexSource, ExamineIndexSource>();
        builder.Services.AddScoped<IMemberIndexAccess, MemberIndexAccess>();

        // Rebuild queue: in-memory status, drained by a hosted service on this server.
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<IRebuildQueue, RebuildQueue>();
        builder.Services.AddSingleton<RebuildRunner>();
        builder.Services.AddHostedService<RebuildWorker>();
    }
}
