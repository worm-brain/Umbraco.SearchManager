using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.Entities;
using Umbraco.Cms.Search.Core.Models.Searching;
using Umbraco.Community.SearchManager.Core.Explore;
using Umbraco.Extensions;

namespace Umbraco.Community.SearchManager.UmbracoSearch.Explore;

/// <summary>
/// Turns Umbraco Search documents (a key and an object type) into Explore hits, with the name,
/// icon and document type from Umbraco's entity and the stored fields from the physical index.
/// </summary>
internal static class UmbracoSearchHits
{
    /// <summary>Builds the hit for one document.</summary>
    /// <param name="document">The document Umbraco Search found.</param>
    /// <param name="entity">Its entity, or null when Umbraco has no such item (an out-of-date index).</param>
    /// <param name="fields">Its stored fields, or null when the provider is not the Examine provider.</param>
    /// <param name="culture">The culture searched, or null.</param>
    /// <returns>The hit.</returns>
    internal static ExploreHit ToHit(
        Document document,
        IEntitySlim? entity,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? fields,
        string? culture
    )
    {
        ArgumentNullException.ThrowIfNull(document);
        var content = entity as IContentEntitySlim;
        var variant = entity as IDocumentEntitySlim;
        bool variesByCulture = variant?.Variations.VariesByCulture() ?? false;

        // A culture search finds that culture's document; its name is the culture's name.
        string? name =
            variesByCulture
            && culture is not null
            && variant!.CultureNames.TryGetValue(culture, out string? cultureName)
                ? cultureName
                : entity?.Name;

        return new ExploreHit(
            Id: document.Id.ToString("D"),
            Score: null,
            Name: name,
            Icon: content?.ContentTypeIcon,
            DocumentType: content?.ContentTypeAlias,
            EntityType: EntityType(document.ObjectType),
            Key: document.Id.ToString("D"),
            Cultures: variesByCulture && culture is not null ? [culture] : [],
            Updated: entity?.UpdateDate,
            Fields: fields ?? new Dictionary<string, IReadOnlyList<string>>()
        );
    }

    /// <summary>
    /// Names stored fields as filters take them: <c>Field_title_texts</c> becomes <c>title</c>
    /// (values of several kinds merged), a segment variant <c>title_{segment}</c>; other fields
    /// (<c>Sys_Culture</c>, Examine's <c>__</c> fields) keep their names.
    /// </summary>
    /// <param name="values">The stored fields, by physical name.</param>
    /// <returns>The fields, by name.</returns>
    internal static IReadOnlyDictionary<string, IReadOnlyList<string>> LogicalFields(
        IReadOnlyDictionary<string, IReadOnlyList<string>> values
    )
    {
        ArgumentNullException.ThrowIfNull(values);
        var fields = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach ((string physical, IReadOnlyList<string> list) in values)
        {
            string key = UmbracoSearchFields.TryParse(
                physical,
                out string? name,
                out _,
                out string? segment
            )
                ? segment is null
                    ? name
                    : $"{name}_{segment}"
                : physical;
            if (!fields.TryGetValue(key, out List<string>? merged))
            {
                fields[key] = merged = [];
            }

            merged.AddRange(list.Where(value => !merged.Contains(value)));
        }

        return fields.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<string>)pair.Value,
            StringComparer.Ordinal
        );
    }

    /// <summary>The entity type the editor link needs, as core Examine's <c>__IndexType</c> names it.</summary>
    /// <param name="objectType">The object type.</param>
    /// <returns><c>content</c>, <c>media</c>, <c>member</c>, or the type's name in lower case.</returns>
    internal static string EntityType(UmbracoObjectTypes objectType) =>
        objectType switch
        {
            UmbracoObjectTypes.Document => "content",
            UmbracoObjectTypes.Media => "media",
            UmbracoObjectTypes.Member => "member",
            _ => objectType.ToString().ToLowerInvariant(),
        };
}
