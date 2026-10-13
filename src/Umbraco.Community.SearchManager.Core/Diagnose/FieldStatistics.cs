using System.Text.Json.Serialization;
using Umbraco.Community.SearchManager.Core.Explore;

namespace Umbraco.Community.SearchManager.Core.Diagnose;

/// <summary>What kind of field a row of the field statistics is, so the view can hide the copies.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<FieldGroup>))]
public enum FieldGroup
{
    /// <summary>A field of the documents: a property or one of Umbraco's own fields.</summary>
    Field,

    /// <summary>A culture's copy of a field, such as <c>nodeName_en-us</c>.</summary>
    CultureVariant,

    /// <summary>An unanalysed copy Umbraco keeps for display, such as <c>__Raw_bodyText</c>.</summary>
    RawCopy,

    /// <summary>A segment's copy of a field (Umbraco Search).</summary>
    Segment,

    /// <summary>A field the engine writes for itself, such as <c>__IndexType</c> or <c>Sys_Culture</c>.</summary>
    System,
}

/// <summary>The lowest and highest value of a field, for date fields where top values say little.</summary>
/// <param name="Min">The lowest value, as a chip takes it (<c>yyyy-MM-dd</c> for dates).</param>
/// <param name="Max">The highest value, as a chip takes it.</param>
public sealed record ValueRange(string Min, string Max);

/// <summary>The statistics of one field of an index.</summary>
/// <param name="Name">
/// The field as a chip names it: the index field for core Examine, the field filters take for
/// Umbraco Search (<c>title</c>, not <c>Field_title_texts</c>).
/// </param>
/// <param name="Label">A human name, such as "Document type", or a segment's name; null when none.</param>
/// <param name="Type">How the field is indexed, which decides what a chip on a top term does.</param>
/// <param name="Group">What kind of field it is.</param>
/// <param name="Culture">The culture of a culture variant, lower case as Umbraco writes it; otherwise null.</param>
/// <param name="Analyzer">The analyzer the index runs over the field's values, or a note for unanalysed fields.</param>
/// <param name="Documents">Live documents with a value in the field; null when the field is stored only.</param>
/// <param name="Missing">Live documents without a value; null when the field is stored only.</param>
/// <param name="UniqueTerms">Distinct terms (distinct values for numbers and dates); null when stored only.</param>
/// <param name="UniqueTermsSampled">Whether counting stopped at the limit, so the field has at least that many.</param>
/// <param name="TopTerms">The most common terms, most documents first; empty for dates.</param>
/// <param name="AnalyzedTerms">Whether the terms are analysed words rather than whole values (text fields).</param>
/// <param name="Range">The lowest and highest value of a date field; otherwise null.</param>
/// <param name="StoredOnly">Whether the field is stored but not indexed, so it cannot be searched.</param>
/// <param name="Physical">The field's name in the Lucene index when it differs from <paramref name="Name"/>.</param>
public sealed record FieldStatistic(
    string Name,
    string? Label,
    FieldType Type,
    FieldGroup Group,
    string? Culture,
    string? Analyzer,
    long? Documents,
    long? Missing,
    long? UniqueTerms,
    bool UniqueTermsSampled,
    IReadOnlyList<TermCount> TopTerms,
    bool AnalyzedTerms,
    ValueRange? Range,
    bool StoredOnly,
    string? Physical
);

/// <summary>The field statistics of one index (Diagnose, ADR 0018).</summary>
/// <param name="Target">The index.</param>
/// <param name="Available">Whether statistics could be read; when not, <paramref name="UnavailableReason"/> says why.</param>
/// <param name="UnavailableReason">Why there are no statistics, for the view to show; null when available.</param>
/// <param name="DocumentCount">Live documents in the index; the base of every coverage figure.</param>
/// <param name="DeletedDocumentCount">Documents deleted but not yet merged away, when known.</param>
/// <param name="CoverageExact">
/// Whether every coverage figure is exact; false when a field had more terms than were read while
/// the index had deletions, so its coverage is an estimate.
/// </param>
/// <param name="DocumentsPerCulture">
/// Whether the index keeps one document per culture (Umbraco Search), so the counts are of
/// culture documents rather than of content items.
/// </param>
/// <param name="ExploreCulture">
/// The culture links into Explore should search in, when the index only finds variant content
/// with a culture picked (Umbraco Search); null otherwise.
/// </param>
/// <param name="Fields">One row per field, sorted by name.</param>
public sealed record FieldStatisticsResult(
    ExploreTarget Target,
    bool Available,
    string? UnavailableReason,
    long DocumentCount,
    long? DeletedDocumentCount,
    bool CoverageExact,
    bool DocumentsPerCulture,
    string? ExploreCulture,
    IReadOnlyList<FieldStatistic> Fields
)
{
    /// <summary>A result saying why a target has no field statistics.</summary>
    /// <param name="target">The target.</param>
    /// <param name="reason">Why, as a sentence for the view.</param>
    /// <returns>The result.</returns>
    public static FieldStatisticsResult Unavailable(ExploreTarget target, string reason) =>
        new(target, false, reason, 0, null, true, false, null, []);
}

/// <summary>
/// One engine's field statistics (Diagnose, ADR 0018), keyed by <see cref="ExploreTarget.Engine"/>
/// so Explore's targets are reused. An engine without one gets a reason instead of an error.
/// </summary>
public interface IFieldStatisticsSource
{
    /// <summary>The engine id, matching <see cref="ExploreTarget.Engine"/>.</summary>
    string Engine { get; }

    /// <summary>Reads the statistics of every field of a target.</summary>
    /// <param name="target">A target of this engine.</param>
    /// <param name="cancellationToken">Cancels the wait; a read already running finishes for the next caller.</param>
    /// <returns>The statistics, or an unavailable result with the reason.</returns>
    Task<FieldStatisticsResult> GetFieldStatisticsAsync(
        ExploreTarget target,
        CancellationToken cancellationToken
    );
}

/// <summary>Sorts Umbraco's field names into <see cref="FieldGroup"/>s.</summary>
public static class FieldGroups
{
    /// <summary>The prefix of Umbraco's unanalysed copies of rich text fields.</summary>
    public const string RawPrefix = "__Raw_";

    /// <summary>The prefix of fields Examine and Umbraco write for themselves.</summary>
    public const string SystemPrefix = "__";

    /// <summary>
    /// Classifies a core Examine field name: <c>__Raw_x</c> is a raw copy; <c>x_{culture}</c> is a
    /// culture variant when <c>x</c> is also a field and the suffix is one of the site's cultures;
    /// any other <c>__</c> name is a system field; the rest are fields.
    /// </summary>
    /// <param name="name">The field name.</param>
    /// <param name="fieldNames">Every field name of the index, compared case-insensitively.</param>
    /// <param name="cultures">The site's ISO culture codes, in any case.</param>
    /// <returns>The group, and the culture (lower case) of a culture variant.</returns>
    public static (FieldGroup Group, string? Culture) Classify(
        string name,
        IReadOnlySet<string> fieldNames,
        IEnumerable<string> cultures
    )
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(fieldNames);
        ArgumentNullException.ThrowIfNull(cultures);

        if (name.StartsWith(RawPrefix, StringComparison.Ordinal))
        {
            return (FieldGroup.RawCopy, null);
        }

        // Umbraco suffixes a variant with the lower-cased culture: nodeName_en-us, __Published_da-dk.
        foreach (string culture in cultures)
        {
            string suffix = "_" + culture.ToLowerInvariant();
            if (
                name.Length > suffix.Length
                && name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
                && fieldNames.Contains(name[..^suffix.Length])
            )
            {
                return (FieldGroup.CultureVariant, culture.ToLowerInvariant());
            }
        }

        return name.StartsWith(SystemPrefix, StringComparison.Ordinal)
            ? (FieldGroup.System, null)
            : (FieldGroup.Field, null);
    }
}
