using Examine;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Infrastructure.Examine;

namespace SearchManager.Samples.SampleContent;

/// <summary>
/// Adds a populator that throws for the index named by <c>SearchManagerSamples:FailPopulatorFor</c>
/// (for example <c>ProductIndex</c>), to try out the Incomplete health. Off unless that setting is
/// present. It runs after Umbraco's own populators, so the index already holds documents when it
/// throws: a rebuild that fails part-way.
/// </summary>
[ComposeAfter(typeof(AddExamineComposer))]
public sealed class FailingPopulatorComposer : IComposer
{
    /// <inheritdoc />
    public void Compose(IUmbracoBuilder builder)
    {
        string? indexName = builder.Config["SearchManagerSamples:FailPopulatorFor"];
        if (string.IsNullOrWhiteSpace(indexName))
        {
            return;
        }

        builder.Services.AddSingleton<IIndexPopulator>(new FailingPopulator(indexName));
    }

    /// <summary>Throws for one index.</summary>
    /// <param name="indexName">The index.</param>
    private sealed class FailingPopulator(string indexName) : IIndexPopulator
    {
        public bool IsRegistered(IIndex index) =>
            string.Equals(index.Name, indexName, StringComparison.OrdinalIgnoreCase);

        public void Populate(params IIndex[] indexes)
        {
            if (indexes.Any(IsRegistered))
            {
                throw new InvalidOperationException(
                    $"Sample failure: the price feed for {indexName} is unavailable (SearchManagerSamples:FailPopulatorFor)."
                );
            }
        }
    }
}
