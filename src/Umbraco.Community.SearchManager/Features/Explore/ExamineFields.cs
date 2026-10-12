using Examine;
using Examine.Lucene;
using Examine.Lucene.Indexing;
using Examine.Lucene.Providers;
using Examine.Lucene.Search;
using Lucene.Net.Index;
using Umbraco.Community.SearchManager.Core.Explore;

namespace Umbraco.Community.SearchManager.Features.Explore;

/// <summary>
/// Reads the fields of an Examine searcher and gives them Explore's field types and aliases.
/// </summary>
/// <remarks>
/// Types are resolved the way <see cref="LuceneIndex"/> resolves them when it writes a document
/// (field definitions, then the <c>__</c> prefix, then full text), never through
/// <see cref="ISearchContext.GetFieldValueType"/>: that call registers full text for a field the
/// index has not written since the site started, and the index would then go on writing it as
/// full text (ADR 0003).
/// </remarks>
internal static class ExamineFields
{
    /// <summary>
    /// Short names for Umbraco's own fields (ADR 0003). An alias is only offered when its field
    /// is in the index, so custom indexes get the ones that apply.
    /// </summary>
    public static IReadOnlyList<FieldAlias> Aliases { get; } =
    [
        new("type", "__NodeTypeAlias", "Document type"),
        new("kind", "__IndexType", "Index type"),
        new("published", "__Published", "Published"),
        new("updated", "updateDate", "Updated"),
        new("created", "createDate", "Created"),
        new("name", "nodeName", "Name"),
        new("id", "__NodeId", "Node id"),
        new("key", "__Key", "Key"),
        new("parent", "parentID", "Parent id"),
        new("path", "__Path", "Path"),
        new("url", "urlName", "URL name"),
        new("author", "writerName", "Last edited by"),
        new("creator", "creatorName", "Created by"),
        new("varies", "__VariesByCulture", "Varies by culture"),
    ];

    /// <summary>
    /// Reads the schema of a searcher: every field in its Lucene reader (so fields written before
    /// the site started are included) with the type the index writes it with. Searchers that are
    /// not Lucene searchers cannot list fields and get an empty schema.
    /// </summary>
    /// <param name="searcher">The searcher.</param>
    /// <param name="indexes">The Lucene indexes behind it, whose definitions give the types.</param>
    /// <returns>The schema.</returns>
    public static FieldSchema Read(ISearcher searcher, IReadOnlyList<LuceneIndex> indexes)
    {
        if (searcher is not BaseLuceneSearcher lucene)
        {
            return FieldSchema.Empty;
        }

        List<string> names;
        using (ISearcherReference reference = lucene.GetSearchContext().GetSearcher())
        {
            names =
            [
                .. MultiFields
                    .GetMergedFieldInfos(reference.IndexSearcher.IndexReader)
                    .Select(info => info.Name),
            ];
        }

        // Examine's sort copies (__Sort_nodeName) are not something to filter on.
        IEnumerable<IndexField> fields = names
            .Where(name => !name.StartsWith("__Sort_", StringComparison.Ordinal))
            .Select(name => new IndexField(name, TypeOf(ValueTypeOf(name, indexes))));
        return new FieldSchema(fields, Aliases);
    }

    /// <summary>
    /// The value type the first index writes a field with, resolved exactly as the index does
    /// when it writes a document, so registering it changes nothing.
    /// </summary>
    /// <param name="field">The field name.</param>
    /// <param name="indexes">The indexes behind the searcher.</param>
    /// <returns>The value type, or null when no Lucene index is known.</returns>
    private static IIndexFieldValueType? ValueTypeOf(
        string field,
        IReadOnlyList<LuceneIndex> indexes
    )
    {
        if (indexes.Count == 0)
        {
            return null;
        }

        FieldValueTypeCollection types = indexes[0].FieldValueTypeCollection;
        ValueTypeFactoryCollection factories = types.ValueTypeFactories;
        IFieldValueTypeFactory FactoryFor(string type) =>
            factories.TryGetFactory(type, out IFieldValueTypeFactory? factory)
                ? factory
                : factories.GetRequiredFactory(FieldDefinitionTypes.FullText);

        // The three fields LuceneIndex writes itself, before the value set's own.
        if (field == ExamineFieldNames.ItemIdFieldName)
        {
            return types.GetValueType(field, FactoryFor(FieldDefinitionTypes.Raw));
        }

        if (field is ExamineFieldNames.CategoryFieldName or ExamineFieldNames.ItemTypeFieldName)
        {
            return types.GetValueType(
                field,
                FactoryFor(FieldDefinitionTypes.InvariantCultureIgnoreCase)
            );
        }

        if (indexes[0].FieldDefinitions.TryGetValue(field, out FieldDefinition definition))
        {
            return types.GetValueType(definition.Name, FactoryFor(definition.Type));
        }

        return types.GetValueType(
            field,
            FactoryFor(
                field.StartsWith(ExamineFieldNames.SpecialFieldPrefix, StringComparison.Ordinal)
                    ? FieldDefinitionTypes.InvariantCultureIgnoreCase
                    : FieldDefinitionTypes.FullText
            )
        );
    }

    /// <summary>Maps an Examine field value type to the Explore field type its queries need.</summary>
    /// <param name="valueType">The value type, or null when unknown.</param>
    /// <returns>The field type; analysed text when unknown.</returns>
    public static FieldType TypeOf(IIndexFieldValueType? valueType) =>
        valueType switch
        {
            DateTimeType => FieldType.Date,
            IIndexRangeValueType => FieldType.Number,
            // Raw strings are unanalysed; the generic-analyzer types (InvariantCultureIgnoreCase,
            // EmailAddress) keep each value as one lower-cased token.
            RawStringType or GenericAnalyzerFieldValueType => FieldType.Keyword,
            _ => FieldType.Text,
        };
}
