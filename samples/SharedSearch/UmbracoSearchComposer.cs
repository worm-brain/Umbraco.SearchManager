using Microsoft.Extensions.Configuration;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Search.BackOffice.DependencyInjection;
using Umbraco.Cms.Search.Core.DependencyInjection;
using Umbraco.Cms.Search.Provider.Examine.DependencyInjection;

namespace SearchManager.Samples.UmbracoSearch;

/// <summary>
/// Turns on Umbraco Search for the Umbraco Search sample sites: the core services, the Examine
/// provider and backoffice search, as Umbraco Search's own test site does. Whether the Examine
/// provider uses zero-downtime indexing comes from <c>Umbraco:CMS:Search:Examine</c> in each site's
/// appsettings.json (on for SearchSite17, off for SearchSite18, so both shapes are covered).
/// <para>
/// <c>SearchManagerSamples:SkipUmbracoSearch</c> leaves Umbraco Search out. The e2e suite sets it
/// for a site's first boot, which installs it and seeds the sample content: on SQLite, seeding with
/// Umbraco Search running can deadlock Umbraco's cache instruction sync (Umbraco issue 23940). The
/// next boot adds Umbraco Search, which then indexes the seeded content on its own.
/// </para>
/// </summary>
public sealed class UmbracoSearchComposer : IComposer
{
    /// <inheritdoc />
    public void Compose(IUmbracoBuilder builder)
    {
        if (builder.Config.GetValue<bool>("SearchManagerSamples:SkipUmbracoSearch"))
        {
            return;
        }

        builder.AddSearchCore().AddExamineSearchProvider().AddBackOfficeSearch();
    }
}
