using System.Text.Json.Serialization;

namespace Umbraco.Community.SearchManager.Core.Explore;

/// <summary>Whether an Explore target is an index or a searcher registered on its own.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ExploreTargetKind>))]
public enum ExploreTargetKind
{
    /// <summary>An index, searched through its own searcher.</summary>
    Index,

    /// <summary>A searcher registered by name, for example a multi-index searcher.</summary>
    Searcher,
}

/// <summary>What Explore can do with a target, beyond searching it with chips.</summary>
/// <param name="NativeQuery">Raw Lucene filters and native mode are available (a Lucene searcher).</param>
/// <param name="TermStatistics">Term and coverage statistics can be read (a Lucene searcher).</param>
public sealed record ExploreCapabilities(bool NativeQuery, bool TermStatistics);

/// <summary>One index or searcher the Explore view can search.</summary>
/// <param name="Name">The index or searcher name; unique across sources.</param>
/// <param name="Engine">The source that owns it, for example <c>examine</c>.</param>
/// <param name="Kind">Index or searcher.</param>
/// <param name="Description">What it holds, when known.</param>
/// <param name="IsMemberData">Whether it exposes member data, which needs Members section access.</param>
/// <param name="Capabilities">What Explore can do with it.</param>
public sealed record ExploreTarget(
    string Name,
    string Engine,
    ExploreTargetKind Kind,
    string? Description,
    bool IsMemberData,
    ExploreCapabilities Capabilities
);

/// <summary>The fields of a target as the Explore view uses them: chips, editor and column picker.</summary>
/// <param name="Fields">Every field, sorted by name.</param>
/// <param name="Aliases">Short names the search box accepts.</param>
/// <param name="Operators">The operators the source runs per field type.</param>
/// <param name="Cultures">The site's cultures for the culture picker, ISO codes.</param>
public sealed record ExploreSchema(
    IReadOnlyList<IndexField> Fields,
    IReadOnlyList<FieldAlias> Aliases,
    IReadOnlyList<FieldTypeOperators> Operators,
    IReadOnlyList<string> Cultures
);

/// <summary>A search request from the Explore view.</summary>
/// <param name="Filters">The chips the user has, disabled ones included; the source skips what it cannot run.</param>
/// <param name="Culture">The culture picked, or null for all.</param>
/// <param name="Skip">Results to skip, for paging.</param>
/// <param name="Take">Results to return, 1 to 100.</param>
public sealed record ExploreQuery(
    IReadOnlyList<Filter> Filters,
    string? Culture = null,
    int Skip = 0,
    int Take = 20
);

/// <summary>One document found.</summary>
/// <param name="Id">The engine's document id (the node id for Umbraco content).</param>
/// <param name="Score">The relevance score, or null when the results are not ranked.</param>
/// <param name="Name">The node name, in the picked culture when the document varies.</param>
/// <param name="DocumentType">The document, media or member type alias.</param>
/// <param name="EntityType">What the document is in Umbraco (<c>content</c>, <c>media</c>, <c>member</c>), when known.</param>
/// <param name="Key">The Umbraco key, for the link to the editor, when known.</param>
/// <param name="Cultures">The cultures the document has; empty when it does not vary.</param>
/// <param name="Updated">When it was last updated, when known.</param>
/// <param name="Fields">Every stored field with its values, for the column picker and the document drawer.</param>
public sealed record ExploreHit(
    string Id,
    float? Score,
    string? Name,
    string? DocumentType,
    string? EntityType,
    string? Key,
    IReadOnlyList<string> Cultures,
    DateTime? Updated,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Fields
);

/// <summary>The answer to an Explore search.</summary>
/// <param name="Total">How many documents match.</param>
/// <param name="Hits">The page of documents asked for.</param>
/// <param name="Query">The query the engine ran, in its own language.</param>
/// <param name="Skipped">Filters the source could not run, which were left out.</param>
public sealed record ExploreResult(
    long Total,
    IReadOnlyList<ExploreHit> Hits,
    string Query,
    IReadOnlyList<SkippedFilter> Skipped
);

/// <summary>A raw query the engine could not parse.</summary>
/// <param name="message">The parser's message.</param>
/// <param name="position">The zero-based character position of the error, when known.</param>
public sealed class InvalidNativeQueryException(string message, int? position = null)
    : Exception(message)
{
    /// <summary>The zero-based character position of the error, when known.</summary>
    public int? Position { get; } = position;
}

/// <summary>
/// One search engine's side of the Explore view (ADR 0002): the targets it can search, their
/// fields, and running a query. Sources are registered in DI; <see cref="IExploreSourceRegistry"/>
/// finds the one that owns a target, so a new engine adds a source without changing the API.
/// </summary>
public interface IExploreSource
{
    /// <summary>The engine id, matching <c>IndexSummary.Engine</c>.</summary>
    string Engine { get; }

    /// <summary>Lists what this engine can search.</summary>
    /// <param name="cancellationToken">Cancels the listing.</param>
    /// <returns>The targets.</returns>
    Task<IReadOnlyList<ExploreTarget>> GetTargetsAsync(CancellationToken cancellationToken);

    /// <summary>Reads a target's fields.</summary>
    /// <param name="target">A target this source listed.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The fields; empty when the engine cannot list them.</returns>
    Task<FieldSchema> GetSchemaAsync(ExploreTarget target, CancellationToken cancellationToken);

    /// <summary>The site's cultures, for the culture picker.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>ISO culture codes.</returns>
    Task<IReadOnlyList<string>> GetCulturesAsync(CancellationToken cancellationToken);

    /// <summary>The operators this engine runs per field type.</summary>
    IReadOnlyList<FieldTypeOperators> Operators { get; }

    /// <summary>Runs a query.</summary>
    /// <param name="target">A target this source listed.</param>
    /// <param name="query">The query.</param>
    /// <param name="cancellationToken">Cancels the search.</param>
    /// <returns>The results.</returns>
    /// <exception cref="InvalidNativeQueryException">A raw query filter could not be parsed.</exception>
    Task<ExploreResult> SearchAsync(
        ExploreTarget target,
        ExploreQuery query,
        CancellationToken cancellationToken
    );
}

/// <summary>Combines every registered <see cref="IExploreSource"/>.</summary>
public interface IExploreSourceRegistry
{
    /// <summary>Lists the targets of every source.</summary>
    /// <param name="cancellationToken">Cancels the listing.</param>
    /// <returns>All targets, grouped by source in registration order.</returns>
    Task<IReadOnlyList<ExploreTarget>> GetTargetsAsync(CancellationToken cancellationToken);

    /// <summary>Finds a target by name and the source that owns it.</summary>
    /// <param name="name">The target name, compared case-insensitively.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>The target and its source, or null when no source lists it.</returns>
    Task<(ExploreTarget Target, IExploreSource Source)?> FindAsync(
        string name,
        CancellationToken cancellationToken
    );
}

/// <inheritdoc />
public sealed class ExploreSourceRegistry(IEnumerable<IExploreSource> sources)
    : IExploreSourceRegistry
{
    private readonly IExploreSource[] _sources = [.. sources];

    /// <inheritdoc />
    public async Task<IReadOnlyList<ExploreTarget>> GetTargetsAsync(
        CancellationToken cancellationToken
    )
    {
        var result = new List<ExploreTarget>();
        foreach (IExploreSource source in _sources)
        {
            result.AddRange(await source.GetTargetsAsync(cancellationToken));
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<(ExploreTarget Target, IExploreSource Source)?> FindAsync(
        string name,
        CancellationToken cancellationToken
    )
    {
        foreach (IExploreSource source in _sources)
        {
            ExploreTarget? target = (
                await source.GetTargetsAsync(cancellationToken)
            ).FirstOrDefault(candidate =>
                string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase)
            );
            if (target is not null)
            {
                return (target, source);
            }
        }

        return null;
    }
}
