using SqlSugar;

namespace InfoCompareAssistant.Data;

[SugarTable("PersonRegistry")]
public sealed class PersonRegistry
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public int Id { get; set; }

    [SugarColumn(Length = 32)]
    public string IdNumber { get; set; } = "";

    [SugarColumn(Length = 64)]
    public string Name { get; set; } = "";

    [SugarColumn(Length = 64, IsNullable = true)]
    public string? Contact { get; set; }

    [SugarColumn(Length = 128, IsNullable = true)]
    public string? GridCommunity { get; set; }

    [SugarColumn(Length = 64, IsNullable = true)]
    public string? SpecialCategory { get; set; }

    /// <summary>特殊身份状态：有效 / 无效</summary>
    [SugarColumn(Length = 16)]
    public string Status { get; set; } = "有效";

    [SugarColumn(Length = 512, IsNullable = true)]
    public string? Remark { get; set; }

    [SugarColumn(IsNullable = true)]
    public DateTime? CancelDate { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}

[SugarTable("DeathRecord")]
public sealed class DeathRecord
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public int Id { get; set; }

    public int BatchId { get; set; }

    [SugarColumn(Length = 32)]
    public string IdNumberNorm { get; set; } = "";

    [SugarColumn(Length = 32)]
    public string DeathDateKey { get; set; } = "";

    [SugarColumn(Length = 64)]
    public string Name { get; set; } = "";

    [SugarColumn(Length = 64, IsNullable = true)]
    public string? DeathDateDisplay { get; set; }

    [SugarColumn(Length = 256, IsNullable = true)]
    public string? SourceFileName { get; set; }

    public DateTime ImportedAt { get; set; }
}

[SugarTable("CompareBatch")]
public sealed class CompareBatch
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public int Id { get; set; }

    [SugarColumn(Length = 256, IsNullable = true)]
    public string? SourceFileName { get; set; }

    /// <summary>数据来源单位（如民政局、卫健委）</summary>
    [SugarColumn(Length = 128, IsNullable = true)]
    public string? SourceDataUnit { get; set; }

    /// <summary>本批次说明</summary>
    [SugarColumn(Length = 512, IsNullable = true)]
    public string? BatchNote { get; set; }

    public DateTime StartedAt { get; set; }

    public DateTime FinishedAt { get; set; }

    public int RowCount { get; set; }

    public int MatchCount { get; set; }
}

[SugarTable("CompareMatch")]
public sealed class CompareMatch
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public int Id { get; set; }

    public int BatchId { get; set; }

    [SugarColumn(Length = 32)]
    public string IdNumberNorm { get; set; } = "";

    [SugarColumn(Length = 64)]
    public string Name { get; set; } = "";

    [SugarColumn(Length = 64, IsNullable = true)]
    public string? DeathDateDisplay { get; set; }

    /// <summary>命中时底册行的特殊身份状态（有效/无效）</summary>
    [SugarColumn(Length = 16)]
    public string RegistryStatus { get; set; } = "";

    [SugarColumn(Length = 128, IsNullable = true)]
    public string? GridCommunity { get; set; }

    [SugarColumn(Length = 64, IsNullable = true)]
    public string? SpecialCategory { get; set; }

    [SugarColumn(Length = 64, IsNullable = true)]
    public string? Contact { get; set; }

    /// <summary>命中的底册主键；确认时按此行更新为无效。</summary>
    [SugarColumn(IsNullable = true)]
    public int? RegistryPersonId { get; set; }

    public bool Confirmed { get; set; }

    [SugarColumn(IsNullable = true)]
    public DateTime? ConfirmedAt { get; set; }

    /// <summary>0=待处理 1=已确认取消特殊身份(底册无效) 2=已标记为死亡信息有误(不改编底册)</summary>
    public int ProcessOutcome { get; set; }

    [SugarColumn(Length = 512, IsNullable = true)]
    public string? ProcessNote { get; set; }

    [SugarColumn(IsNullable = true)]
    public DateTime? ProcessedAt { get; set; }

    public DateTime CreatedAt { get; set; }
}

/// <summary>底册编辑用：人员分类 / 所属网格（社区）白名单，仅允许下拉选择。</summary>
[SugarTable("RosterDirectoryItem")]
public sealed class RosterDirectoryItem
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public int Id { get; set; }

    /// <summary>Category = 人员分类；Community = 所属网格/社区</summary>
    [SugarColumn(Length = 16)]
    public string Kind { get; set; } = "";

    [SugarColumn(Length = 128)]
    public string Name { get; set; } = "";

    public DateTime CreatedAt { get; set; }
}
