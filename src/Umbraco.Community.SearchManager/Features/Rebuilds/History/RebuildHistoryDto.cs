using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace Umbraco.Community.SearchManager.Features.Rebuilds.History;

/// <summary>
/// A row of the rebuild history table. The migration creates the table from this class, so a
/// change here needs a new migration step, never an edit of the shape an earlier step created.
/// </summary>
[TableName(TableName)]
[PrimaryKey("id", AutoIncrement = true)]
[ExplicitColumns]
internal sealed class RebuildHistoryDto
{
    /// <summary>The table name.</summary>
    public const string TableName = "searchManagerRebuildHistory";

    /// <summary>The row id; a higher id is a later entry.</summary>
    [Column("id")]
    [PrimaryKeyColumn(AutoIncrement = true)]
    public int Id { get; set; }

    /// <summary>The index.</summary>
    [Column("indexName")]
    [Length(255)]
    [Index(IndexTypes.NonClustered, Name = "IX_" + TableName + "_indexName")]
    public string IndexName { get; set; } = null!;

    /// <summary>The engine id.</summary>
    [Column("engine")]
    [Length(64)]
    public string Engine { get; set; } = null!;

    /// <summary>The machine the rebuild ran on.</summary>
    [Column("server")]
    [Length(255)]
    public string Server { get; set; } = null!;

    /// <summary>The <c>RebuildTrigger</c> name.</summary>
    [Column("trigger")]
    [Length(32)]
    public string Trigger { get; set; } = null!;

    /// <summary>The user who asked for it, when a user did.</summary>
    [Column("triggeredBy")]
    [Length(255)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? TriggeredBy { get; set; }

    /// <summary>When it started (UTC).</summary>
    [Column("startedAt")]
    public DateTime StartedAt { get; set; }

    /// <summary>When it ended (UTC).</summary>
    [Column("endedAt")]
    public DateTime EndedAt { get; set; }

    /// <summary>The <c>RebuildStatus</c> name: Succeeded or Failed.</summary>
    [Column("outcome")]
    [Length(32)]
    public string Outcome { get; set; } = null!;

    /// <summary>Documents before, when known.</summary>
    [Column("documentsBefore")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public long? DocumentsBefore { get; set; }

    /// <summary>Documents after, when known.</summary>
    [Column("documentsAfter")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public long? DocumentsAfter { get; set; }

    /// <summary>The error of a failed rebuild.</summary>
    [Column("error")]
    [SpecialDbType(SpecialDbTypes.NVARCHARMAX)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? Error { get; set; }

    /// <summary>Whether the index holds member data.</summary>
    [Column("isMemberIndex")]
    public bool IsMemberIndex { get; set; }
}
