using System.Globalization;
using Examine;
using Umbraco.Community.SearchManager.Core.Explore;

namespace Umbraco.Community.SearchManager.Features.Explore;

/// <summary>Turns Examine search results into Explore hits, reading Umbraco's own field names.</summary>
internal static class ExamineHits
{
    private const string PublishedPrefix = "__Published_";

    /// <summary>Builds the hit for one result.</summary>
    /// <param name="result">The Examine result.</param>
    /// <param name="culture">The picked culture, for the name, or null.</param>
    /// <returns>The hit.</returns>
    public static ExploreHit ToHit(ISearchResult result, string? culture)
    {
        IReadOnlyDictionary<string, IReadOnlyList<string>> values = result.AllValues;
        string? First(string field) =>
            values.TryGetValue(field, out IReadOnlyList<string>? list) && list.Count > 0
                ? list[0]
                : null;

        // Variant content has one __Published_{culture} field per culture it has.
        string[] cultures =
            First("__VariesByCulture") == "y"
                ?
                [
                    .. values
                        .Keys.Where(key =>
                            key.StartsWith(PublishedPrefix, StringComparison.OrdinalIgnoreCase)
                        )
                        .Select(key => key[PublishedPrefix.Length..])
                        .Order(StringComparer.Ordinal),
                ]
                : [];

        string? name =
            (string.IsNullOrEmpty(culture) ? null : First($"nodeName_{culture.ToLowerInvariant()}"))
            ?? First("nodeName");

        return new ExploreHit(
            Id: result.Id,
            Score: float.IsFinite(result.Score) ? result.Score : null,
            Name: name,
            DocumentType: First("__NodeTypeAlias"),
            EntityType: First("__IndexType"),
            Key: First("__Key"),
            Cultures: cultures,
            Updated: Ticks(First("updateDate")),
            Fields: values.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyList<string>)[.. pair.Value],
                StringComparer.Ordinal
            )
        );
    }

    /// <summary>Reads a date Examine stored as ticks.</summary>
    /// <param name="value">The stored value.</param>
    /// <returns>The date, or null when the value is not ticks.</returns>
    public static DateTime? Ticks(string? value) =>
        long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out long ticks)
        && ticks >= DateTime.MinValue.Ticks
        && ticks <= DateTime.MaxValue.Ticks
            ? new DateTime(ticks, DateTimeKind.Unspecified)
            : null;
}
