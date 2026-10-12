using Umbraco.Cms.Search.Provider.Examine.Configuration;
using Umbraco.Community.SearchManager.Core.Explore;
using SearchFieldNames = Umbraco.Cms.Search.Core.Constants.FieldNames;

namespace Umbraco.Community.SearchManager.UmbracoSearch.Explore;

/// <summary>
/// The fields of an Umbraco Search index as Explore uses them (ADR 0016): the names filters take
/// (property aliases and the <c>Umb_*</c> system fields, never a culture or segment suffix) and the
/// kind of values each holds, which decides both the chip's <see cref="FieldType"/> and the
/// Umbraco Search filter a chip compiles to.
/// </summary>
internal sealed class UmbracoSearchFields
{
    /// <summary>The Examine provider's prefix for a field's physical name: <c>Field_{name}_{values}</c>.</summary>
    internal const string PhysicalPrefix = "Field_";

    /// <summary>
    /// Umbraco Search's own fields, which every index has whatever the provider: their kinds come
    /// from <c>SystemFieldsContentIndexer</c> (verified against 17.2.0 and 18.2.0).
    /// </summary>
    internal static IReadOnlyDictionary<string, FieldValues> SystemFields { get; } =
        new Dictionary<string, FieldValues>(StringComparer.OrdinalIgnoreCase)
        {
            [SearchFieldNames.Id] = FieldValues.Keywords,
            [SearchFieldNames.ParentId] = FieldValues.Keywords,
            [SearchFieldNames.PathIds] = FieldValues.Keywords,
            [SearchFieldNames.Name] = FieldValues.TextsR1,
            [SearchFieldNames.ContentTypeId] = FieldValues.Keywords,
            [SearchFieldNames.CreateDate] = FieldValues.DateTimeOffsets,
            [SearchFieldNames.UpdateDate] = FieldValues.DateTimeOffsets,
            [SearchFieldNames.Level] = FieldValues.Integers,
            [SearchFieldNames.SortOrder] = FieldValues.Integers,
            [SearchFieldNames.ObjectType] = FieldValues.Keywords,
            [SearchFieldNames.Tags] = FieldValues.Keywords,
        };

    /// <summary>Short names the search box accepts, offered when their field exists.</summary>
    internal static IReadOnlyList<FieldAlias> Aliases { get; } =
    [
        new("type", SearchFieldNames.ContentTypeId, "Document type"),
        new("kind", SearchFieldNames.ObjectType, "Object type"),
        new("name", SearchFieldNames.Name, "Name"),
        new("updated", SearchFieldNames.UpdateDate, "Updated"),
        new("created", SearchFieldNames.CreateDate, "Created"),
        new("key", SearchFieldNames.Id, "Key"),
        new("parent", SearchFieldNames.ParentId, "Parent key"),
        new("path", SearchFieldNames.PathIds, "Path keys"),
        new("level", SearchFieldNames.Level, "Level"),
        new("sort", SearchFieldNames.SortOrder, "Sort order"),
        new("tags", SearchFieldNames.Tags, "Tags"),
    ];

    /// <summary>
    /// The physical suffixes of each kind of value, as the Examine provider writes them; a field
    /// written with more than one kind is typed by the first in this order.
    /// </summary>
    private static readonly (string Suffix, FieldValues Values)[] Suffixes =
    [
        ("keywords", FieldValues.Keywords),
        ("integers", FieldValues.Integers),
        ("decimals", FieldValues.Decimals),
        ("datetimeoffsets", FieldValues.DateTimeOffsets),
        ("textsr1", FieldValues.TextsR1),
        ("textsr2", FieldValues.TextsR2),
        ("textsr3", FieldValues.TextsR3),
        ("texts", FieldValues.Texts),
    ];

    private readonly Dictionary<string, FieldValues> _kinds;

    /// <summary>Creates the fields of an index.</summary>
    /// <param name="kinds">Each field's name and the kind of values it holds.</param>
    /// <param name="documentCount">Documents in the index, or null when unknown.</param>
    internal UmbracoSearchFields(
        IReadOnlyDictionary<string, FieldValues> kinds,
        long? documentCount = null
    )
    {
        _kinds = new Dictionary<string, FieldValues>(kinds, StringComparer.OrdinalIgnoreCase);
        Schema = new FieldSchema(
            _kinds.Select(pair => new IndexField(pair.Key, TypeOf(pair.Value))),
            Aliases,
            documentCount
        );
    }

    /// <summary>The fields as Explore's chips, parser and column picker see them.</summary>
    internal FieldSchema Schema { get; }

    /// <summary>
    /// Builds the fields from the physical field names of the Examine provider's index, on top of
    /// the system fields. Names that are not <c>Field_{name}_{values}</c> (or
    /// <c>Field_{name}_{values}_{segment}</c>), such as <c>Sys_Culture</c> or Examine's <c>__</c>
    /// fields, are not filterable and are left out.
    /// </summary>
    /// <param name="physicalNames">The physical index's field names; empty for another provider.</param>
    /// <param name="documentCount">Documents in the index, or null when unknown.</param>
    /// <returns>The fields.</returns>
    internal static UmbracoSearchFields FromPhysicalNames(
        IEnumerable<string> physicalNames,
        long? documentCount = null
    )
    {
        ArgumentNullException.ThrowIfNull(physicalNames);
        var kinds = new Dictionary<string, FieldValues>(
            SystemFields,
            StringComparer.OrdinalIgnoreCase
        );
        var found = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (string physical in physicalNames)
        {
            if (TryParse(physical, out string? name, out FieldValues values, out _))
            {
                // A system field keeps its known kind; other fields keep the highest-ranked kind seen.
                int rank = Array.FindIndex(Suffixes, entry => entry.Values == values);
                if (
                    !SystemFields.ContainsKey(name)
                    && (!found.TryGetValue(name, out int best) || rank < best)
                )
                {
                    found[name] = rank;
                    kinds[name] = values;
                }
            }
        }

        return new UmbracoSearchFields(kinds, documentCount);
    }

    /// <summary>
    /// Reads a physical field name of the Examine provider: <c>Field_title_texts</c> is
    /// <c>title</c> with texts, <c>Field_title_texts_mobile</c> its <c>mobile</c> segment.
    /// </summary>
    /// <param name="physical">The physical name.</param>
    /// <param name="name">The field name filters take.</param>
    /// <param name="values">The kind of values.</param>
    /// <param name="segment">The segment, or null.</param>
    /// <returns>Whether the name is a provider field name.</returns>
    internal static bool TryParse(
        string physical,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? name,
        out FieldValues values,
        out string? segment
    )
    {
        ArgumentNullException.ThrowIfNull(physical);
        name = null;
        values = default;
        segment = null;
        if (!physical.StartsWith(PhysicalPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        string[] parts = physical[PhysicalPrefix.Length..].Split('_');
        // Field_{name}_{values}, where the name itself may hold underscores (Umb_Name).
        if (parts.Length >= 2 && TrySuffix(parts[^1], out values))
        {
            name = string.Join('_', parts[..^1]);
            return true;
        }

        // Field_{name}_{values}_{segment}.
        if (parts.Length >= 3 && TrySuffix(parts[^2], out values))
        {
            name = string.Join('_', parts[..^2]);
            segment = parts[^1];
            return true;
        }

        return false;
    }

    /// <summary>The kind of values a field holds.</summary>
    /// <param name="field">A field name of this index (not an alias).</param>
    /// <returns>The kind, or null when the index has no such field.</returns>
    internal FieldValues? KindOf(string field) =>
        _kinds.TryGetValue(field, out FieldValues values) ? values : null;

    /// <summary>The Explore field type of a kind of values.</summary>
    /// <param name="values">The kind.</param>
    /// <returns>Keyword, Number (integers and decimals), Date or Text.</returns>
    internal static FieldType TypeOf(FieldValues values) =>
        values switch
        {
            FieldValues.Keywords => FieldType.Keyword,
            FieldValues.Integers or FieldValues.Decimals => FieldType.Number,
            FieldValues.DateTimeOffsets => FieldType.Date,
            _ => FieldType.Text,
        };

    private static bool TrySuffix(string part, out FieldValues values)
    {
        foreach ((string suffix, FieldValues candidate) in Suffixes)
        {
            if (string.Equals(part, suffix, StringComparison.Ordinal))
            {
                values = candidate;
                return true;
            }
        }

        values = default;
        return false;
    }
}
