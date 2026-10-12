using System.Text.Json.Serialization;

namespace Umbraco.Community.SearchManager.Core.Explore;

/// <summary>How a field is indexed, which decides the operators a chip on it offers.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<FieldType>))]
public enum FieldType
{
    /// <summary>Analysed text: matched word by word.</summary>
    Text,

    /// <summary>A whole value matched exactly (possibly lower-cased), such as a document type alias.</summary>
    Keyword,

    /// <summary>A number, matched with ranges.</summary>
    Number,

    /// <summary>A date, matched with ranges.</summary>
    Date,
}

/// <summary>One field of an index or searcher.</summary>
/// <param name="Name">The field name in the index, for example <c>nodeName_en-us</c>.</param>
/// <param name="Type">How it is indexed.</param>
/// <param name="Documents">
/// How many documents have a value in the field, for its coverage; null when the source cannot
/// count. Lucene counts documents deleted but not yet merged away too.
/// </param>
public sealed record IndexField(string Name, FieldType Type, long? Documents = null);

/// <summary>A short name the search box accepts for a field, such as <c>type</c> for <c>__NodeTypeAlias</c>.</summary>
/// <param name="Alias">What the user types, lower case.</param>
/// <param name="Field">The field it stands for.</param>
/// <param name="Label">A human name for chips and suggestions, such as "Document type".</param>
public sealed record FieldAlias(string Alias, string Field, string Label);

/// <summary>The operators a source runs on fields of one type.</summary>
/// <param name="Type">The field type.</param>
/// <param name="Operators">The operators, in the order the chip editor lists them.</param>
public sealed record FieldTypeOperators(FieldType Type, IReadOnlyList<FilterOperator> Operators);

/// <summary>
/// The fields of one index or searcher as the parser and compilers need them: names, types and
/// aliases. Field names compare case-insensitively, as Examine's own lookups do not but users type.
/// </summary>
public sealed class FieldSchema
{
    private readonly Dictionary<string, IndexField> _fields;
    private readonly Dictionary<string, FieldAlias> _aliases;

    /// <summary>Creates a schema.</summary>
    /// <param name="fields">The fields; a name listed twice keeps its first entry.</param>
    /// <param name="aliases">Aliases; ones naming a field the schema does not have are dropped.</param>
    /// <param name="documentCount">Documents in the index, or null when unknown.</param>
    public FieldSchema(
        IEnumerable<IndexField> fields,
        IEnumerable<FieldAlias> aliases,
        long? documentCount = null
    )
    {
        DocumentCount = documentCount;
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(aliases);

        _fields = new Dictionary<string, IndexField>(StringComparer.OrdinalIgnoreCase);
        foreach (IndexField field in fields)
        {
            _fields.TryAdd(field.Name, field);
        }

        _aliases = new Dictionary<string, FieldAlias>(StringComparer.OrdinalIgnoreCase);
        foreach (FieldAlias alias in aliases.Where(alias => _fields.ContainsKey(alias.Field)))
        {
            _aliases.TryAdd(alias.Alias, alias);
        }

        Fields = [.. _fields.Values.OrderBy(field => field.Name, StringComparer.OrdinalIgnoreCase)];
        Aliases = [.. _aliases.Values];
    }

    /// <summary>A schema with no fields, for sources that cannot list them.</summary>
    public static FieldSchema Empty { get; } = new([], []);

    /// <summary>Documents in the index, or null when unknown.</summary>
    public long? DocumentCount { get; }

    /// <summary>Every field, sorted by name.</summary>
    public IReadOnlyList<IndexField> Fields { get; }

    /// <summary>The aliases whose field exists.</summary>
    public IReadOnlyList<FieldAlias> Aliases { get; }

    /// <summary>Finds a field by alias or by name.</summary>
    /// <param name="nameOrAlias">What the user typed before the colon.</param>
    /// <returns>The field, or null when the schema has no such field.</returns>
    public IndexField? Resolve(string nameOrAlias)
    {
        ArgumentNullException.ThrowIfNull(nameOrAlias);
        if (_aliases.TryGetValue(nameOrAlias, out FieldAlias? alias))
        {
            return _fields[alias.Field];
        }

        return _fields.GetValueOrDefault(nameOrAlias);
    }

    /// <summary>
    /// The field to query for <paramref name="field"/> in <paramref name="culture"/>: its
    /// culture variant (<c>nodeName_en-us</c>) when the index has one, otherwise the field itself.
    /// </summary>
    /// <param name="field">A field of this schema.</param>
    /// <param name="culture">An ISO culture code, or null for no culture.</param>
    /// <returns>The field to query.</returns>
    /// <remarks>A variant of another type is ignored, so a range never lands on a text field.</remarks>
    public IndexField ForCulture(IndexField field, string? culture)
    {
        ArgumentNullException.ThrowIfNull(field);
        if (string.IsNullOrWhiteSpace(culture))
        {
            return field;
        }

        // Umbraco writes culture variants with a lower-case suffix (nodeName_en-us).
        IndexField? variant = _fields.GetValueOrDefault(
            $"{field.Name}_{culture.ToLowerInvariant()}"
        );
        return variant is not null && variant.Type == field.Type ? variant : field;
    }
}
