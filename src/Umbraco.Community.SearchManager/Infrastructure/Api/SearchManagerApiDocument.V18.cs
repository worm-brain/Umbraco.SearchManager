#if UMBRACO_18
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Api.Common.OpenApi;
using Umbraco.Cms.Api.Management.OpenApi;
using Umbraco.Cms.Core.DependencyInjection;
using HttpJsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;

namespace Umbraco.Community.SearchManager.Infrastructure.Api;

/// <summary>
/// Umbraco 18: the API document is a Microsoft.AspNetCore.OpenApi document registered with
/// Umbraco's <c>AddBackOfficeOpenApiDocument</c> (the shape the 18.2 <c>umbraco-extension</c>
/// template generates; present since 18.0.0).
/// </summary>
internal static partial class SearchManagerApiDocument
{
    /// <inheritdoc cref="Register" />
    internal static partial void Register(IUmbracoBuilder builder)
    {
        // The document's schemas come from the minimal-API JsonOptions with this name, not the
        // MVC ones the controllers format with. Web defaults match the backoffice's camelCase;
        // strict number handling makes counts "integer" rather than "integer or string", so the
        // client generated from this document matches the one generated from Umbraco 17's.
        builder.Services.Configure<HttpJsonOptions>(
            SearchManagerApi.Name,
            options =>
            {
                options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
                options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
            }
        );

        builder.AddBackOfficeOpenApiDocument(
            SearchManagerApi.Name,
            document =>
                document
                    .WithTitle(Title)
                    .WithBackOfficeAuthentication()
                    .WithJsonOptions(SearchManagerApi.Name)
                    .ConfigureOpenApiOptions(options =>
                    {
                        options.AddDocumentTransformer(
                            (doc, _, _) =>
                            {
                                doc.Info.Version = "1.0";
                                return Task.CompletedTask;
                            }
                        );

                        // The bare action name as the operation id, as on Umbraco 17, so the
                        // generated client has short method names (getIndexes, searchExplore).
                        options.AddOperationTransformer(
                            (operation, context, _) =>
                            {
                                if (
                                    context.Description.ActionDescriptor.RouteValues.TryGetValue(
                                        "action",
                                        out string? action
                                    ) && !string.IsNullOrEmpty(action)
                                )
                                {
                                    operation.OperationId = action;
                                }

                                return Task.CompletedTask;
                            }
                        );
                    })
        );
    }
}
#endif
