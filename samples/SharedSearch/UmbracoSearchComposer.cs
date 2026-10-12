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
/// </summary>
public sealed class UmbracoSearchComposer : IComposer
{
    /// <inheritdoc />
    public void Compose(IUmbracoBuilder builder) =>
        builder.AddSearchCore().AddExamineSearchProvider().AddBackOfficeSearch();
}
