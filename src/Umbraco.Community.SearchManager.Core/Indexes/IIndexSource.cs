namespace Umbraco.Community.SearchManager.Core.Indexes;

/// <summary>
/// One search engine's indexes (core Examine, Umbraco Search, ...). Sources are registered in
/// DI; <see cref="IIndexSourceRegistry"/> combines them.
/// </summary>
public interface IIndexSource
{
    /// <summary>The engine id written on every index this source lists, for example <c>examine</c>.</summary>
    string Engine { get; }

    /// <summary>
    /// Names of Examine indexes this source owns, so the core Examine source does not list them
    /// a second time (Umbraco Search's physical Lucene indexes, for example).
    /// </summary>
    IReadOnlySet<string> ClaimedExamineIndexNames { get; }

    /// <summary>Lists the source's indexes. A broken index is listed with its health, never thrown.</summary>
    /// <param name="claimedElsewhere">Examine index names other sources own, to leave out.</param>
    /// <param name="cancellationToken">Cancels the listing.</param>
    /// <returns>The indexes, in the source's preferred order.</returns>
    Task<IReadOnlyList<IndexSummary>> GetIndexesAsync(
        IReadOnlySet<string> claimedElsewhere,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Rebuilds one index on this server and completes only once the rebuilt index is committed
    /// and searchable, so a caller can record the run as succeeded.
    /// </summary>
    /// <param name="indexName">The index, as <see cref="IndexSummary.Name"/> gives it.</param>
    /// <param name="documentsWritten">
    /// Receives the number of documents in the copy being built, when the source can tell, so the
    /// caller can estimate progress.
    /// </param>
    /// <param name="cancellationToken">Stops waiting (the rebuild itself may carry on).</param>
    /// <returns>A task that completes when the rebuild is done.</returns>
    /// <exception cref="InvalidOperationException">
    /// The index cannot be rebuilt, the engine refused, or the rebuild failed; the message says why
    /// in words a user can act on.
    /// </exception>
    Task RebuildAsync(
        string indexName,
        IProgress<long> documentsWritten,
        CancellationToken cancellationToken
    );
}

/// <summary>Combines every registered <see cref="IIndexSource"/>.</summary>
public interface IIndexSourceRegistry
{
    /// <summary>Lists the indexes of every source, each index once.</summary>
    /// <param name="cancellationToken">Cancels the listing.</param>
    /// <returns>All indexes, grouped by source in registration order.</returns>
    Task<IReadOnlyList<IndexSummary>> GetIndexesAsync(CancellationToken cancellationToken);

    /// <summary>Finds the source for an engine id.</summary>
    /// <param name="engine">The engine id, for example <c>examine</c>.</param>
    /// <returns>The source, or null when no source has that id.</returns>
    IIndexSource? GetSource(string engine);
}

/// <inheritdoc />
public sealed class IndexSourceRegistry(IEnumerable<IIndexSource> sources) : IIndexSourceRegistry
{
    private readonly IIndexSource[] _sources = [.. sources];

    /// <inheritdoc />
    public async Task<IReadOnlyList<IndexSummary>> GetIndexesAsync(
        CancellationToken cancellationToken
    )
    {
        var result = new List<IndexSummary>();
        foreach (IIndexSource source in _sources)
        {
            // Each source skips the Examine indexes the other sources claim, so an index that is
            // really Umbraco Search's (for example) is listed once, by its owner.
            var claimedElsewhere = new HashSet<string>(
                _sources
                    .Where(other => !ReferenceEquals(other, source))
                    .SelectMany(other => other.ClaimedExamineIndexNames),
                StringComparer.OrdinalIgnoreCase
            );

            result.AddRange(await source.GetIndexesAsync(claimedElsewhere, cancellationToken));
        }

        return result;
    }

    /// <inheritdoc />
    public IIndexSource? GetSource(string engine) =>
        _sources.FirstOrDefault(source =>
            string.Equals(source.Engine, engine, StringComparison.Ordinal)
        );
}
