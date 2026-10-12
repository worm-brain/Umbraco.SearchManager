using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Api.Common.Attributes;
using Umbraco.Cms.Api.Management.Controllers;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Web.Common.Authorization;
using Umbraco.Cms.Web.Common.Routing;

namespace Umbraco.Community.SearchManager.Infrastructure.Api;

/// <summary>Names shared by the package's Management API.</summary>
public static class SearchManagerApi
{
    /// <summary>
    /// The API name: the route segment (<c>/umbraco/search-manager/api/v1</c>) and the OpenAPI
    /// document name.
    /// </summary>
    public const string Name = "search-manager";
}

/// <summary>
/// Base class for every Search Manager Management API controller. Routes under
/// <c>/umbraco/search-manager/api/v{version}</c>, maps to the package's own OpenAPI document and
/// requires Settings section access. Deriving from <see cref="ManagementApiControllerBase"/> gives
/// the backoffice's JSON options and conventions, as the core Examine controllers have.
/// </summary>
[ApiController]
[BackOfficeRoute($"{SearchManagerApi.Name}/api/v{{version:apiVersion}}")]
[Authorize(Policy = AuthorizationPolicies.SectionAccessSettings)]
[MapToApi(SearchManagerApi.Name)]
public abstract class SearchManagerApiControllerBase : ManagementApiControllerBase { }

/// <summary>Registers the package's OpenAPI document.</summary>
/// <remarks>
/// Registering an API document differs between Umbraco majors (Swashbuckle in 17,
/// Microsoft.AspNetCore.OpenApi in 18), so <see cref="SearchManagerApiDocument.Register"/> has one
/// implementation per major.
/// </remarks>
public sealed class SearchManagerApiComposer : IComposer
{
    /// <summary>Adds the API document.</summary>
    /// <param name="builder">The Umbraco builder.</param>
    public void Compose(IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        SearchManagerApiDocument.Register(builder);
    }
}

/// <summary>
/// Registers the <c>search-manager</c> OpenAPI document. Implemented once per Umbraco major in
/// <c>SearchManagerApiDocument.V17.cs</c> and <c>SearchManagerApiDocument.V18.cs</c>.
/// </summary>
internal static partial class SearchManagerApiDocument
{
    /// <summary>The document title shown in the Swagger UI.</summary>
    internal const string Title = "Umbraco Search Manager Management API";

    /// <summary>Adds the document and its backoffice security requirements.</summary>
    /// <param name="builder">The Umbraco builder.</param>
    internal static partial void Register(IUmbracoBuilder builder);
}
