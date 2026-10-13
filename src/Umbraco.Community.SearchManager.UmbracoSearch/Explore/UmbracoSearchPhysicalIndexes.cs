using Examine;
using Umbraco.Cms.Search.Core.Models.Configuration;
using Umbraco.Cms.Search.Provider.Examine.Services;

namespace Umbraco.Community.SearchManager.UmbracoSearch.Explore;

/// <summary>
/// Finds the Examine provider's physical index that serves queries for an Umbraco Search alias:
/// the alias itself, or the active <c>{alias}_a</c> / <c>{alias}_b</c> slot under zero-downtime
/// indexing. Shared by Explore and Diagnose.
/// </summary>
internal static class UmbracoSearchPhysicalIndexes
{
    /// <summary>Whether a registration's indexer is the Examine provider.</summary>
    /// <param name="registration">The registration.</param>
    /// <returns><see langword="true"/> for the Examine provider.</returns>
    public static bool IsExamineBacked(ContentIndexRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        return typeof(IExamineIndexer).IsAssignableFrom(registration.Indexer);
    }

    /// <summary>The physical index serving queries for an alias.</summary>
    /// <param name="registrations">Umbraco Search's content index registrations.</param>
    /// <param name="alias">The Umbraco Search alias, compared case-insensitively.</param>
    /// <param name="activeIndexManager">The Examine provider's slot manager; null without the provider.</param>
    /// <param name="examineManager">Examine's indexes.</param>
    /// <returns>
    /// The index, or null when the alias is unknown, its indexer is not the Examine provider, or
    /// Examine has no index by the active name.
    /// </returns>
    public static IIndex? Find(
        IEnumerable<ContentIndexRegistration> registrations,
        string alias,
        IActiveIndexManager? activeIndexManager,
        IExamineManager examineManager
    )
    {
        ArgumentNullException.ThrowIfNull(registrations);
        ArgumentNullException.ThrowIfNull(examineManager);
        ContentIndexRegistration? registration = registrations.FirstOrDefault(candidate =>
            string.Equals(candidate.IndexAlias, alias, StringComparison.OrdinalIgnoreCase)
        );
        if (registration is null || activeIndexManager is null || !IsExamineBacked(registration))
        {
            return null;
        }

        return examineManager.TryGetIndex(
            activeIndexManager.ResolveActiveIndexName(registration.IndexAlias),
            out IIndex? index
        )
            ? index
            : null;
    }
}
