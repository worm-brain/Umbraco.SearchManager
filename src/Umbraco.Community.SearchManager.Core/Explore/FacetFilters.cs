namespace Umbraco.Community.SearchManager.Core.Explore;

/// <summary>
/// The chips a field's top values are counted under in the fields panel (ADR 0021): every chip
/// except the field's own include chips, so picking one document type still lists the others
/// with their counts, as faceted search does. Exclusions stay, so an excluded value stays gone.
/// </summary>
public static class FacetFilters
{
    /// <summary>Drops the include chips that query a field.</summary>
    /// <param name="filters">The chips.</param>
    /// <param name="field">The field counted, as queried (culture variant already chosen).</param>
    /// <param name="schema">The target's fields, to resolve aliases.</param>
    /// <param name="culture">The culture picked, which moves chips to culture variant fields.</param>
    /// <returns>The chips to count under; the same list when none queries the field.</returns>
    public static IReadOnlyList<Filter> Without(
        IReadOnlyList<Filter> filters,
        string field,
        FieldSchema schema,
        string? culture
    )
    {
        ArgumentNullException.ThrowIfNull(filters);
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(schema);

        bool IsOwnInclude(Filter filter) =>
            filter.Kind == FilterKind.Field
            && !filter.Exclude
            // "Is missing" prohibits every value: an exclusion.
            && filter.Op != FilterOperator.Missing
            && !string.IsNullOrEmpty(filter.Field)
            && schema.Resolve(filter.Field) is IndexField resolved
            && string.Equals(
                schema.ForCulture(resolved, culture).Name,
                field,
                StringComparison.OrdinalIgnoreCase
            );

        return filters.Any(IsOwnInclude)
            ? [.. filters.Where(filter => !IsOwnInclude(filter))]
            : filters;
    }
}
