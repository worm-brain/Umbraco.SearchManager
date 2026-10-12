using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Community.SearchManager.Core.Indexes;
using Umbraco.Community.SearchManager.Features.Indexes;

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
    }
}
