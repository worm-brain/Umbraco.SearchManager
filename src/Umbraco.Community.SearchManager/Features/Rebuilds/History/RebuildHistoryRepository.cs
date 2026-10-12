using NPoco;
using Umbraco.Cms.Infrastructure.Persistence;
using Umbraco.Cms.Infrastructure.Scoping;
using Umbraco.Community.SearchManager.Core.Rebuilds;
using Umbraco.Extensions;

namespace Umbraco.Community.SearchManager.Features.Rebuilds.History;

/// <summary>
/// Keeps the rebuild history in the <see cref="RebuildHistoryDto.TableName"/> table through
/// Umbraco's scopes, so it works on every database Umbraco supports (SQL Server and SQLite).
/// </summary>
/// <param name="scopeProvider">Opens database scopes.</param>
internal sealed class RebuildHistoryRepository(IScopeProvider scopeProvider) : IRebuildHistory
{
    /// <inheritdoc />
    public void Add(RebuildHistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        using IScope scope = scopeProvider.CreateScope();
        IUmbracoDatabase database = scope.Database;
        database.Insert(ToDto(entry));

        // Prune: keep the newest runs of this index (a higher id is a later entry).
        List<int> ids = database.Fetch<int>(
            scope
                .SqlContext.Sql()
                .Select<RebuildHistoryDto>(dto => dto.Id)
                .From<RebuildHistoryDto>()
                .Where<RebuildHistoryDto>(dto => dto.IndexName == entry.IndexName)
                .OrderByDescending<RebuildHistoryDto>(dto => dto.Id)
        );
        int[] excess = [.. ids.Skip(IRebuildHistory.RunsKeptPerIndex)];
        if (excess.Length > 0)
        {
            database.Execute(
                scope
                    .SqlContext.Sql()
                    .Delete<RebuildHistoryDto>()
                    .WhereIn<RebuildHistoryDto>(dto => dto.Id, excess)
            );
        }

        scope.Complete();
    }

    /// <inheritdoc />
    public IReadOnlyList<RebuildHistoryEntry> GetRecent(string indexName)
    {
        using IScope scope = scopeProvider.CreateScope();
        List<RebuildHistoryDto> rows = scope.Database.Fetch<RebuildHistoryDto>(
            scope
                .SqlContext.Sql()
                .SelectAll()
                .From<RebuildHistoryDto>()
                .Where<RebuildHistoryDto>(dto => dto.IndexName == indexName)
                .OrderByDescending<RebuildHistoryDto>(dto => dto.Id)
        );
        scope.Complete();
        return [.. rows.Take(IRebuildHistory.RunsKeptPerIndex).Select(ToEntry)];
    }

    /// <inheritdoc />
    /// <remarks>
    /// Reads the whole table and picks the newest row per index in memory: pruning caps it at
    /// <see cref="IRebuildHistory.RunsKeptPerIndex"/> rows per index, and this avoids SQL that
    /// differs between SQL Server and SQLite.
    /// </remarks>
    public IReadOnlyList<RebuildHistoryEntry> GetLatestPerIndex()
    {
        using IScope scope = scopeProvider.CreateScope();
        List<RebuildHistoryDto> rows = scope.Database.Fetch<RebuildHistoryDto>(
            scope.SqlContext.Sql().SelectAll().From<RebuildHistoryDto>()
        );
        scope.Complete();
        return
        [
            .. rows.GroupBy(row => row.IndexName, StringComparer.OrdinalIgnoreCase)
                .Select(group => ToEntry(group.MaxBy(row => row.Id)!)),
        ];
    }

    /// <summary>Maps an entry to a row, storing times as UTC.</summary>
    /// <param name="entry">The entry.</param>
    /// <returns>The row.</returns>
    private static RebuildHistoryDto ToDto(RebuildHistoryEntry entry) =>
        new()
        {
            IndexName = entry.IndexName,
            Engine = entry.Engine,
            Server = entry.Server,
            Trigger = entry.Trigger.ToString(),
            TriggeredBy = entry.TriggeredBy,
            StartedAt = entry.StartedAt.UtcDateTime,
            EndedAt = entry.EndedAt.UtcDateTime,
            Outcome = entry.Outcome.ToString(),
            DocumentsBefore = entry.DocumentsBefore,
            DocumentsAfter = entry.DocumentsAfter,
            Error = entry.Error,
            IsMemberIndex = entry.IsMemberIndex,
        };

    /// <summary>Maps a row back to an entry; the database hands times back without a kind.</summary>
    /// <param name="row">The row.</param>
    /// <returns>The entry.</returns>
    private static RebuildHistoryEntry ToEntry(RebuildHistoryDto row) =>
        new(
            row.IndexName,
            row.Engine,
            row.Server,
            Enum.TryParse(row.Trigger, out RebuildTrigger trigger) ? trigger : RebuildTrigger.Other,
            row.TriggeredBy,
            AsUtc(row.StartedAt),
            AsUtc(row.EndedAt),
            Enum.TryParse(row.Outcome, out RebuildStatus outcome) ? outcome : RebuildStatus.Failed,
            row.DocumentsBefore,
            row.DocumentsAfter,
            row.Error,
            row.IsMemberIndex
        );

    /// <summary>Reads a stored time as UTC.</summary>
    /// <param name="value">The stored value.</param>
    /// <returns>The time.</returns>
    private static DateTimeOffset AsUtc(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
