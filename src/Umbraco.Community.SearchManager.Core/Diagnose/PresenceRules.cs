namespace Umbraco.Community.SearchManager.Core.Diagnose;

/// <summary>
/// What an index's own rules say about one node, read by the engine's source from the index's
/// validator or registration. Every rule defaults to letting the node in, so a source sets only
/// the rules its index has.
/// </summary>
/// <param name="HoldsEntityType">Whether the index holds this kind of entity (documents) at all.</param>
/// <param name="ItemTypeIncluded">Whether the index's include and exclude lists let the node's document type in.</param>
/// <param name="Empty">Whether the index holds no documents.</param>
/// <param name="PublishedOnly">Whether the index only holds published content.</param>
/// <param name="IncludesTrashed">Whether the index holds content in the recycle bin.</param>
/// <param name="ParentId">The node the index is limited to, when it is.</param>
/// <param name="PathAllowed">Whether the node is inside the part of the tree the index holds.</param>
/// <param name="IncludesProtected">Whether the index holds content public access protects.</param>
public sealed record IndexRules(
    bool HoldsEntityType = true,
    bool ItemTypeIncluded = true,
    bool Empty = false,
    bool PublishedOnly = false,
    bool IncludesTrashed = true,
    int? ParentId = null,
    bool PathAllowed = true,
    bool IncludesProtected = true
);

/// <summary>
/// Explains why an index does not hold a node (ADR 0019), checking the index's rules in a fixed
/// order so the first reason that applies is the one shown.
/// </summary>
public static class PresenceRules
{
    /// <summary>The reason given when no rule keeps the node out.</summary>
    public const string OutOfDate =
        "It should be here: the index is out of date. Rebuild it to add the node.";

    /// <summary>Decides the status and reason for a node an index has no document for.</summary>
    /// <param name="node">The node.</param>
    /// <param name="rules">What the index's rules say about it.</param>
    /// <returns>
    /// Not applicable when the index never holds a node like this one; otherwise missing, with
    /// the first rule that keeps it out, or <see cref="OutOfDate"/>.
    /// </returns>
    public static (PresenceStatus Status, string Reason) Explain(NodeFacts node, IndexRules rules)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(rules);

        if (!rules.HoldsEntityType)
        {
            return (PresenceStatus.NotApplicable, "This index does not hold content pages.");
        }

        if (!rules.ItemTypeIncluded)
        {
            return (
                PresenceStatus.NotApplicable,
                $"This index leaves out the {node.ContentTypeAlias} document type."
            );
        }

        if (rules.Empty)
        {
            return (PresenceStatus.Missing, "The index is empty. Rebuild it.");
        }

        if (rules.PublishedOnly && !node.Published)
        {
            return (
                PresenceStatus.Missing,
                "The page is not published, and this index only holds published content."
            );
        }

        if (node.Trashed && !rules.IncludesTrashed)
        {
            return (
                PresenceStatus.Missing,
                "The page is in the recycle bin, which this index leaves out."
            );
        }

        if (!rules.PathAllowed)
        {
            return (
                PresenceStatus.Missing,
                rules.ParentId is { } parent
                    ? $"The page is outside the part of the tree this index holds (under node {parent})."
                    : "The page is outside the part of the tree this index holds."
            );
        }

        if (node.Protected && !rules.IncludesProtected)
        {
            return (
                PresenceStatus.Missing,
                "Public access protects the page, and this index leaves protected content out."
            );
        }

        return (PresenceStatus.Missing, OutOfDate);
    }
}

/// <summary>
/// Spots an index copy older than the node (ADR 0019). A published-only index changes only when
/// the node is published, so it is compared with the publish date; other indexes with the save date.
/// </summary>
public static class StaleCheck
{
    /// <summary>How much older the index's date may be before it counts as stale (clock and write rounding).</summary>
    public static readonly TimeSpan Tolerance = TimeSpan.FromSeconds(1);

    /// <summary>The date an index's copy of the node should carry.</summary>
    /// <param name="node">The node.</param>
    /// <param name="publishedOnly">Whether the index only holds published content.</param>
    /// <param name="culture">The culture of the copy, or null for the node as a whole.</param>
    /// <returns>The date, or null when there is nothing to compare with (an unpublished culture).</returns>
    public static DateTime? Expected(NodeFacts node, bool publishedOnly, string? culture)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (culture is null)
        {
            return publishedOnly ? node.PublishDate : node.UpdateDate;
        }

        NodeCulture? match = node.Cultures.FirstOrDefault(candidate =>
            string.Equals(candidate.Iso, culture, StringComparison.OrdinalIgnoreCase)
        );
        return publishedOnly ? match?.PublishDate : match?.UpdateDate;
    }

    /// <summary>Whether the index's copy is older than it should be.</summary>
    /// <param name="indexDate">The date in the index, or null when it records none.</param>
    /// <param name="expected">The date it should carry, or null when unknown.</param>
    /// <returns>Null when either date is unknown; otherwise whether the index is more than <see cref="Tolerance"/> behind.</returns>
    public static bool? IsStale(DateTime? indexDate, DateTime? expected) =>
        indexDate is null || expected is null ? null : indexDate.Value < expected.Value - Tolerance;
}
