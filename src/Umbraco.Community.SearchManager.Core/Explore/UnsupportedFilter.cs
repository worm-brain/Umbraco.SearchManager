namespace Umbraco.Community.SearchManager.Core.Explore;

/// <summary>
/// A kind of chip an engine cannot run, with the reason the chip shows (ADR 0016). The schema
/// endpoint returns a source's rules, so the client draws matching chips disabled and never sends
/// them; the source skips them with the same reason.
/// </summary>
/// <param name="Kind">The chip kind the rule applies to.</param>
/// <param name="Types">The field types for <see cref="FilterKind.Field"/> chips; empty for any type.</param>
/// <param name="Operators">The operators the rule applies to; empty for any operator.</param>
/// <param name="Exclude">
/// Whether the rule applies only to excluding chips (<see langword="true"/>), only to including
/// ones (<see langword="false"/>), or to both (null).
/// </param>
/// <param name="Reason">A sentence for the chip's tooltip, without a full stop.</param>
public sealed record UnsupportedFilter(
    FilterKind Kind,
    IReadOnlyList<FieldType> Types,
    IReadOnlyList<FilterOperator> Operators,
    bool? Exclude,
    string Reason
)
{
    /// <summary>Whether the rule applies to a chip.</summary>
    /// <param name="filter">The chip.</param>
    /// <param name="fieldType">The type of the chip's field, or null for free text and raw queries.</param>
    /// <returns><see langword="true"/> when the engine cannot run the chip.</returns>
    public bool Matches(Filter filter, FieldType? fieldType)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return filter.Kind == Kind
            && (Types.Count == 0 || (fieldType is { } type && Types.Contains(type)))
            && (Operators.Count == 0 || Operators.Contains(filter.Op))
            && (Exclude is null || Exclude == filter.Exclude);
    }

    /// <summary>The reason of the first rule that applies to a chip.</summary>
    /// <param name="rules">The rules, in order.</param>
    /// <param name="filter">The chip.</param>
    /// <param name="fieldType">The type of the chip's field, or null for free text and raw queries.</param>
    /// <returns>The reason, or null when no rule applies.</returns>
    public static string? FirstReason(
        IEnumerable<UnsupportedFilter> rules,
        Filter filter,
        FieldType? fieldType
    )
    {
        ArgumentNullException.ThrowIfNull(rules);
        return rules.FirstOrDefault(rule => rule.Matches(filter, fieldType))?.Reason;
    }
}
