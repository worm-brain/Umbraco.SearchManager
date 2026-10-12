using Umbraco.Cms.Core.Packaging;
using Umbraco.Cms.Infrastructure.Migrations;

namespace Umbraco.Community.SearchManager.Features.Rebuilds.History;

/// <summary>
/// The package's database migrations. Umbraco discovers the plan and runs it at startup
/// (unattended package migrations are on by default).
/// </summary>
public sealed class SearchManagerMigrationPlan : PackageMigrationPlan
{
    /// <summary>The package name Umbraco records the plan's state under.</summary>
    public const string PlanName = "Umbraco.Community.SearchManager";

    /// <summary>The state after the rebuild history table is created.</summary>
    public const string RebuildHistoryState = "searchmanager-rebuild-history-v1";

    /// <summary>Creates the plan.</summary>
    public SearchManagerMigrationPlan()
        : base(PlanName, PlanName, PlanName) { }

    /// <inheritdoc />
    protected override void DefinePlan() => To<AddRebuildHistoryTable>(RebuildHistoryState);
}

/// <summary>
/// Creates the rebuild history table. Idempotent: Umbraco 17 reruns a package plan from its start
/// (<c>IgnoreCurrentState</c>), so the table may already be there.
/// </summary>
/// <param name="context">The migration context.</param>
public sealed class AddRebuildHistoryTable(IMigrationContext context) : AsyncMigrationBase(context)
{
    /// <inheritdoc />
    protected override Task MigrateAsync()
    {
        if (!TableExists(RebuildHistoryDto.TableName))
        {
            Create.Table<RebuildHistoryDto>().Do();
        }

        return Task.CompletedTask;
    }
}
