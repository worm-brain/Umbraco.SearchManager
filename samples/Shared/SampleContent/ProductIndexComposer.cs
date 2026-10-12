using Examine;
using Examine.Lucene;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Infrastructure.Examine;
using Umbraco.Cms.Infrastructure.Scoping;

namespace SearchManager.Samples.SampleContent;

/// <summary>
/// Registers a custom Examine index, <c>ProductIndex</c>, holding published products only, so the
/// sample sites show a site-defined index next to Umbraco's own. It is an
/// <see cref="UmbracoContentIndex"/>, so Umbraco's content populators and default event handler
/// fill and update it like the built-in indexes.
/// </summary>
public sealed class ProductIndexComposer : IComposer
{
    /// <summary>The custom index's name.</summary>
    public const string IndexName = "ProductIndex";

    /// <inheritdoc />
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services.AddExamineLuceneIndex<
            UmbracoContentIndex,
            ConfigurationEnabledDirectoryFactory
        >(IndexName);
        builder.Services.ConfigureOptions<ConfigureProductIndexOptions>();
    }

    /// <summary>Only published products go in; <c>price</c> is a number.</summary>
    private sealed class ConfigureProductIndexOptions(
        IPublicAccessService publicAccessService,
        IScopeProvider scopeProvider
    ) : IConfigureNamedOptions<LuceneDirectoryIndexOptions>
    {
        public void Configure(string? name, LuceneDirectoryIndexOptions options)
        {
            if (name != IndexName)
            {
                return;
            }

            options.Validator = new ContentValueSetValidator(
                true,
                false,
                publicAccessService,
                scopeProvider,
                includeItemTypes: ["product"]
            );
            options.FieldDefinitions.AddOrUpdate(
                new FieldDefinition("price", FieldDefinitionTypes.Double)
            );
        }

        public void Configure(LuceneDirectoryIndexOptions options) =>
            Configure(string.Empty, options);
    }
}
