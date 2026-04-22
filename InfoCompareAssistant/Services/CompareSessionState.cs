namespace InfoCompareAssistant.Services;

public sealed class CompareSessionState
{
    public int Step { get; set; } = 1;

    /// <summary>数据来源单位（第一步填写）</summary>
    public string? SourceUnit { get; set; }

    /// <summary>本批次说明（第一步填写）</summary>
    public string? BatchDescription { get; set; }

    public string? TempPath { get; set; }

    public string? OriginalFileName { get; set; }

    public List<string> Headers { get; } = new();

    public List<Dictionary<string, object>> PreviewRows { get; } = new();

    public string? IdColumn { get; set; }

    public string? NameColumn { get; set; }

    public string? DeathDateColumn { get; set; }

    public int LastBatchId { get; set; }

    public string? LastError { get; set; }

    public int Progress { get; set; }

    public void Reset()
    {
        Step = 1;
        SourceUnit = null;
        BatchDescription = null;
        TempPath = null;
        OriginalFileName = null;
        Headers.Clear();
        PreviewRows.Clear();
        IdColumn = NameColumn = DeathDateColumn = null;
        LastBatchId = 0;
        LastError = null;
        Progress = 0;
    }
}
