using InfoCompareAssistant.Data;
using MiniExcelLibs;
using SqlSugar;

namespace InfoCompareAssistant.Services;

public sealed class DashboardService(SqlSugarClient db, CompareFlowService compare)
{
    public int GetRegistryTotal() =>
        db.Queryable<PersonRegistry>().Where(p => p.Status == "有效").Count();

    public int GetMonthlyAlertCount()
    {
        var monthStart = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        return db.Queryable<CompareMatch>()
            .Where(m => m.ProcessOutcome == 0 && m.CreatedAt >= monthStart)
            .Count();
    }

    public DateTime? GetLastCompareFinishedAt() =>
        db.Queryable<CompareBatch>()
            .OrderByDescending(b => b.FinishedAt)
            .Select(b => b.FinishedAt)
            .ToList()
            .FirstOrDefault();

    public record PendingAlertView(CompareMatch Match, CompareBatch? Batch);

    public (int Total, List<PendingAlertView> Items) GetPendingAlertsPage(int pageIndex, int pageSize = 10)
    {
        var q = db.Queryable<CompareMatch>().Where(m => m.ProcessOutcome == 0);
        var total = q.Count();
        var matches = q
            .OrderByDescending(m => m.CreatedAt)
            .Skip(pageIndex * pageSize)
            .Take(pageSize)
            .ToList();
        if (matches.Count == 0)
            return (total, new List<PendingAlertView>());

        var batchIds = matches.Select(m => m.BatchId).Distinct().ToList();
        var batches = db.Queryable<CompareBatch>().Where(b => batchIds.Contains(b.Id)).ToList();
        var map = batches.ToDictionary(b => b.Id);
        return (total, matches.Select(m =>
        {
            map.TryGetValue(m.BatchId, out var b);
            return new PendingAlertView(m, b);
        }).ToList());
    }

    /// <summary>导出全部待处理预警：批次信息、命中底册字段等。</summary>
    public byte[] BuildPendingAlertsExportBytes()
    {
        var matches = db.Queryable<CompareMatch>()
            .Where(m => m.ProcessOutcome == 0)
            .OrderByDescending(m => m.CreatedAt)
            .ToList();
        if (matches.Count == 0)
        {
            using var empty = new MemoryStream();
            MiniExcel.SaveAs(empty, new[] { new { 提示 = "当前无待处理预警" } }, sheetName: "预警待办");
            return empty.ToArray();
        }

        var batchIds = matches.Select(m => m.BatchId).Distinct().ToList();
        var batches = db.Queryable<CompareBatch>().Where(b => batchIds.Contains(b.Id)).ToList().ToDictionary(b => b.Id);
        var rows = new List<object>();
        foreach (var m in matches)
        {
            batches.TryGetValue(m.BatchId, out var b);
            rows.Add(new
            {
                数据来源单位 = b?.SourceDataUnit ?? "",
                批次说明 = b?.BatchNote ?? "",
                源文件名 = b?.SourceFileName ?? "",
                预警姓名 = m.Name,
                身份证号 = m.IdNumberNorm,
                匹配死亡日期 = m.DeathDateDisplay,
                命中底册特殊类别 = m.SpecialCategory,
                命中底册网格 = m.GridCommunity,
                底册联系方式 = m.Contact
            });
        }

        using var ms = new MemoryStream();
        MiniExcel.SaveAs(ms, rows, sheetName: "预警待办");
        return ms.ToArray();
    }

    public void ConfirmMatchRevokeFromHome(int matchId)
    {
        var m = compare.GetMatchById(matchId) ?? throw new InvalidOperationException("未找到预警记录。");
        compare.Confirm(m.BatchId, new List<int> { matchId });
    }
}
