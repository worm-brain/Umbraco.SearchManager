using System.Globalization;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.Entities;
using Umbraco.Cms.Core.Services;
using Umbraco.Community.SearchManager.Core.Diagnose;
using Umbraco.Extensions;

namespace Umbraco.Community.SearchManager.Features.Diagnose.Nodes;

/// <summary>Finds content nodes and reads what Umbraco knows about one (ADR 0019).</summary>
public interface INodeFactsReader
{
    /// <summary>Finds documents by id, key or name.</summary>
    /// <param name="query">A node id, a key, or part of a name.</param>
    /// <param name="take">The most candidates to return.</param>
    /// <returns>The candidates; empty for a blank query.</returns>
    IReadOnlyList<NodeCandidate> Find(string query, int take);

    /// <summary>Reads a document's facts.</summary>
    /// <param name="key">The document key.</param>
    /// <returns>The facts, or null when there is no such document.</returns>
    NodeFacts? Read(Guid key);
}

/// <inheritdoc />
/// <param name="entityService">Looks nodes up by id and key.</param>
/// <param name="entitySearchService">Finds nodes by name.</param>
/// <param name="contentService">Reads the document.</param>
/// <param name="publicAccessService">Says whether public access protects it.</param>
internal sealed class NodeFactsReader(
    IEntityService entityService,
    IEntitySearchService entitySearchService,
    IContentService contentService,
    IPublicAccessService publicAccessService
) : INodeFactsReader
{
    /// <inheritdoc />
    public IReadOnlyList<NodeCandidate> Find(string query, int take)
    {
        string trimmed = (query ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return [];
        }

        // An id or a key names one node; anything else is searched by name.
        IEntitySlim? exact =
            int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id)
                ? entityService.Get(id, UmbracoObjectTypes.Document)
            : Guid.TryParse(trimmed, out Guid key)
                ? entityService.Get(key, UmbracoObjectTypes.Document)
            : null;
        if (exact is not null)
        {
            return [Candidate(exact)];
        }

        // The single-object-type overload is the only one Umbraco 17.0 has.
        return
        [
            .. entitySearchService
                .Search(UmbracoObjectTypes.Document, trimmed, 0, take)
                .Items.Select(Candidate),
        ];
    }

    /// <inheritdoc />
    public NodeFacts? Read(Guid key)
    {
        IContent? content = contentService.GetById(key);
        if (content is null)
        {
            return null;
        }

        NodeCulture[] cultures = content.ContentType.VariesByCulture()
            ?
            [
                .. content.AvailableCultures.Select(culture => new NodeCulture(
                    culture,
                    content.IsCulturePublished(culture),
                    content.IsCultureEdited(culture),
                    content.GetUpdateDate(culture),
                    content.GetPublishDate(culture)
                )),
            ]
            : [];

        return new NodeFacts(
            content.Id,
            content.Key,
            content.Name ?? string.Empty,
            "document",
            content.ContentType.Alias,
            content.Path,
            content.Published,
            content.Trashed,
            publicAccessService.IsProtected(content).Success,
            content.UpdateDate,
            content.PublishDate,
            cultures
        );
    }

    private static NodeCandidate Candidate(IEntitySlim entity) =>
        new(
            entity.Id,
            entity.Key,
            entity.Name ?? string.Empty,
            (entity as IContentEntitySlim)?.ContentTypeIcon,
            entity.Trashed
        );
}
