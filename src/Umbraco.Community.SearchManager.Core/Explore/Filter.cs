using System.Text.Json.Serialization;

namespace Umbraco.Community.SearchManager.Core.Explore;

/// <summary>What a <see cref="Filter"/> (one chip in the Explore search box) matches against.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<FilterKind>))]
public enum FilterKind
{
    /// <summary>Free text: words or a phrase searched in every field the engine searches by default.</summary>
    Text,

    /// <summary>One field compared with a value through a <see cref="FilterOperator"/>.</summary>
    Field,

    /// <summary>A raw query in the engine's own language (Lucene for core Examine).</summary>
    Lucene,
}

/// <summary>
/// How a <see cref="FilterKind.Field"/> filter compares its field with its value. Which operators
/// a field offers depends on its <see cref="FieldType"/> (<see cref="FieldOperators.For"/>).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<FilterOperator>))]
public enum FilterOperator
{
    /// <summary>Text: every word of the value appears in the field. Also the operator of a free-text filter.</summary>
    Contains,

    /// <summary>Text: the value appears as an exact phrase. Free-text filters use it for quoted input too.</summary>
    Phrase,

    /// <summary>Text or keyword: a term starts with the value.</summary>
    StartsWith,

    /// <summary>Text: a term is within two edits of the value.</summary>
    Fuzzy,

    /// <summary>Keyword: the field equals the value.</summary>
    Is,

    /// <summary>Number: the field equals the value.</summary>
    Equals,

    /// <summary>Number: the field is greater than the value.</summary>
    GreaterThan,

    /// <summary>Number: the field is greater than or equal to the value.</summary>
    AtLeast,

    /// <summary>Number: the field is less than the value.</summary>
    LessThan,

    /// <summary>Number: the field is less than or equal to the value.</summary>
    AtMost,

    /// <summary>Date: the field falls on the value's day.</summary>
    On,

    /// <summary>Date: the field is on or after the start of the value's day.</summary>
    After,

    /// <summary>Date: the field is before the start of the value's day.</summary>
    Before,

    /// <summary>Any type: the field has no value; the filter's value is ignored.</summary>
    Missing,
}

/// <summary>
/// One chip in the Explore search box: the portable query model every engine compiles from
/// (ADR 0002). Chips are a flat list; see <c>LuceneQueryCompiler</c> for how they combine.
/// </summary>
/// <param name="Kind">What the filter matches against.</param>
/// <param name="Field">
/// The index field for <see cref="FilterKind.Field"/> (an alias already resolved to the field
/// name, or the name as typed when the index has no such field); null for the other kinds.
/// </param>
/// <param name="Op">The comparison; <see cref="FilterOperator.Contains"/> or <see cref="FilterOperator.Phrase"/> for free text.</param>
/// <param name="Value">
/// The words, value or raw query, as the user typed it. Dates are <c>yyyy-MM-dd</c>, <c>today</c>
/// or a number of days back such as <c>-7d</c>; numbers use the invariant culture.
/// </param>
/// <param name="Exclude">Whether the filter removes the documents it matches instead of keeping them.</param>
public sealed record Filter(
    FilterKind Kind,
    string? Field,
    FilterOperator Op,
    string Value,
    bool Exclude = false
);
