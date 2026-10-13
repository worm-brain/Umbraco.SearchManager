using Examine;
using Umbraco.Cms.Infrastructure.Examine;

namespace Umbraco.Community.SearchManager.Features.Rebuilds.External;

/// <summary>
/// Wraps a populator handed to Umbraco's index rebuilder so a failure is noted on the tracked
/// runs of the indexes it was filling, then rethrown unchanged.
/// </summary>
/// <remarks>
/// Umbraco's rebuild of several indexes (the startup rebuild among them) catches and logs every
/// populator failure, so without this a failed startup rebuild would look like a success.
/// </remarks>
/// <param name="inner">The populator.</param>
/// <param name="recorder">Receives the failures.</param>
internal sealed class RecordingIndexPopulator(IIndexPopulator inner, CoreRebuildRecorder recorder)
    : IIndexPopulator
{
    /// <summary>The wrapped populator.</summary>
    public IIndexPopulator Inner => inner;

    /// <inheritdoc />
    public bool IsRegistered(IIndex index) => inner.IsRegistered(index);

    /// <inheritdoc />
    public void Populate(params IIndex[] indexes)
    {
        try
        {
            inner.Populate(indexes);
        }
        catch (Exception exception)
        {
            string message = $"{inner.GetType().Name}: {exception.Message}";
            foreach (IIndex index in indexes.Where(inner.IsRegistered))
            {
                recorder.Fail(index.Name, message);
            }

            throw;
        }
    }
}
