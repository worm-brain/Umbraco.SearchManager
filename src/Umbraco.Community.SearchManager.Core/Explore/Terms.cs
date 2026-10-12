namespace Umbraco.Community.SearchManager.Core.Explore;

/// <summary>One value of a field and how many documents have it.</summary>
/// <param name="Value">The value as a chip takes it: the term, a decoded number, or a relative date such as <c>-7d</c>.</param>
/// <param name="Count">Documents with the value.</param>
public sealed record TermCount(string Value, long Count);

/// <summary>A field's most common values, for the search box's value suggestions.</summary>
/// <param name="Field">The field.</param>
/// <param name="Type">Its type: text fields return analysed terms, not whole values.</param>
/// <param name="Terms">The values, most common first.</param>
/// <param name="Sampled">
/// Whether the source stopped reading terms at its limit, so rarer values may be missing.
/// Counts include documents deleted but not yet merged away.
/// </param>
public sealed record TermsResult(
    string Field,
    FieldType Type,
    IReadOnlyList<TermCount> Terms,
    bool Sampled
);

/// <summary>A request for the fields panel's top values.</summary>
/// <param name="Filters">The chips; with none that run, the values are counted over the whole index.</param>
/// <param name="Culture">The culture picked, or null.</param>
/// <param name="Fields">The fields to count.</param>
/// <param name="Take">Values to return per field, 1 to 50.</param>
public sealed record FacetsQuery(
    IReadOnlyList<Filter> Filters,
    string? Culture,
    IReadOnlyList<string> Fields,
    int Take = 6
);

/// <summary>One field's top values in the fields panel.</summary>
/// <param name="Field">The field.</param>
/// <param name="Type">Its type; date fields count relative ranges (<c>-7d</c>, <c>-30d</c>).</param>
/// <param name="Values">The values, most common first.</param>
/// <param name="Sampled">Whether the field has more values than the source counts, so rarer ones may be missing.</param>
public sealed record FieldFacet(
    string Field,
    FieldType Type,
    IReadOnlyList<TermCount> Values,
    bool Sampled
);

/// <summary>The fields panel's top values.</summary>
/// <param name="WholeIndex">Whether the counts are over the whole index (no filter ran) rather than the results.</param>
/// <param name="Total">Documents counted.</param>
/// <param name="Fields">One entry per requested field the target has.</param>
public sealed record FacetsResult(bool WholeIndex, long Total, IReadOnlyList<FieldFacet> Fields);

/// <summary>The relative date ranges the fields panel counts and the search box suggests.</summary>
public static class RelativeDates
{
    /// <summary><c>-7d</c>, <c>-30d</c> and <c>-365d</c>: on or after that many days ago.</summary>
    public static IReadOnlyList<string> Ranges { get; } = ["-7d", "-30d", "-365d"];
}
