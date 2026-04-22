using System.Collections;
using InfoCompareAssistant.Data;
using MiniExcelLibs;
using SqlSugar;

namespace InfoCompareAssistant.Services;

/// <param name="OccurrenceCount">该身份证号在电子表格中的出现次数（&gt;1 即重复）。</param>
public sealed record DuplicateIdEntry(string IdNumberNorm, int OccurrenceCount);

public sealed record CompareBatchListRow(CompareBatch Batch, int ProcessedMatchCount);

public sealed record ProcessedMatchHistoryRow(
    int MatchId,
    int BatchId,
    string BatchLabel,
    string? SourceDataUnit,
    string Name,
    string IdNumberNorm,
    string? SpecialCategory,
    int ProcessOutcome,
    string? ProcessNote,
    DateTime? ProcessedAt,
    string? DeathDateDisplay);

/// <summary>首页处理弹窗：死亡登记异常与历史预警处理提示。</summary>
public sealed record DeathHomeInsight(bool DeathDateAnomaly, bool HistoryProcessingHint);

public sealed class CompareFlowService(SqlSugarClient db)
{
    public const int DefaultPreviewMaxRows = 5;

    public static string FormatBatchDisplayName(CompareBatch? b) =>
        b == null
            ? "—"
            : (string.IsNullOrWhiteSpace(b.SourceFileName)
                ? (string.IsNullOrWhiteSpace(b.BatchNote) ? $"批次 #{b.Id}" : b.BatchNote!.Trim())
                : b.SourceFileName!.Trim());
    public List<string> ReadHeaders(string path)
    {
        var first = MiniExcel.Query(path, useHeaderRow: true).FirstOrDefault();
        return first == null ? new List<string>() : GetColumnNames(first);
    }

    public void FillPreview(string path, CompareSessionState state, int maxRows = DefaultPreviewMaxRows)
    {
        state.Headers.Clear();
        state.PreviewRows.Clear();
        var rows = MiniExcel.Query(path, useHeaderRow: true).Take(maxRows).ToList();
        if (rows.Count == 0)
            return;

        foreach (var key in GetColumnNames(rows[0]!))
            state.Headers.Add(key);

        foreach (var row in rows)
            state.PreviewRows.Add(ToRowDictionary(row!));
    }

    /// <summary>
    /// MiniExcel 行常为 ExpandoObject 等，作为 dynamic 访问时无 .Keys；以字典视图读取。
    /// </summary>
    private static List<string> GetColumnNames(object row)
    {
        if (row is IDictionary<string, object> d1)
            return d1.Keys.ToList();
        if (row is IDictionary<string, object?> d2)
            return d2.Keys.ToList();
        if (row is IDictionary d0)
        {
            var list = new List<string>();
            foreach (var k in d0.Keys)
            {
                if (k != null)
                    list.Add(k.ToString()!);
            }

            return list;
        }

        return new List<string>();
    }

    private static Dictionary<string, object> ToRowDictionary(object row)
    {
        if (row is IDictionary<string, object> d1)
            return new Dictionary<string, object>(d1, StringComparer.Ordinal);
        if (row is IDictionary<string, object?> d2)
        {
            var m = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var kv in d2)
                m[kv.Key] = kv.Value is { } o ? o : (object)string.Empty;
            return m;
        }

        throw new InvalidOperationException("无法将 Excel 行解析为列字典（类型不受支持）。");
    }

    public void InferColumns(CompareSessionState state)
    {
        if (state.Headers.Count == 0)
            return;

        state.NameColumn ??= state.Headers.FirstOrDefault(h => h.Contains("姓名", StringComparison.Ordinal))
                             ?? state.Headers[0];

        state.DeathDateColumn ??= state.Headers.FirstOrDefault(h =>
            h.Contains("死亡", StringComparison.Ordinal)
            || h.Contains("去世", StringComparison.Ordinal)
            || h.Contains("身故", StringComparison.Ordinal)
            || h.Contains("死亡日期", StringComparison.Ordinal));

        state.IdColumn ??= state.Headers.FirstOrDefault(h =>
                               h.Contains("身份证", StringComparison.Ordinal)
                               || h.Contains("证件", StringComparison.Ordinal))
                           ?? DetectIdColumn(state);
    }

    private static string? DetectIdColumn(CompareSessionState state)
    {
        foreach (var h in state.Headers)
        {
            var score = 0;
            foreach (var row in state.PreviewRows.Take(30))
            {
                if (!row.TryGetValue(h, out object? v))
                    continue;
                if (IdNumberNormalizer.LooksLikeIdNumber(v?.ToString()))
                    score++;
            }

            if (score >= 2)
                return h;
        }

        return state.Headers.FirstOrDefault();
    }

    /// <summary>检查导入表是否出现同一身份证号的多次行；若有则不可导入，需先清理。</summary>
    public (bool ok, string? error, List<DuplicateIdEntry> Duplicates) CheckDuplicateIdInImportFile(
        string path, string idColumn)
    {
        var rows = MiniExcel.Query(path, useHeaderRow: true).ToList();
        if (rows.Count == 0)
            return (true, null, new List<DuplicateIdEntry>());

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var rowObj in rows)
        {
            if (rowObj is not IDictionary<string, object> && rowObj is not IDictionary<string, object?>)
                continue;
            var row = ToRowDictionary(rowObj!);
            if (!row.TryGetValue(idColumn, out object? idObj))
                continue;
            var id = IdNumberNormalizer.Clean(idObj?.ToString());
            if (string.IsNullOrEmpty(id) || !IdNumberNormalizer.IsValidChecksum18(id))
                continue;
            counts[id] = counts.GetValueOrDefault(id) + 1;
        }

        var dups = counts
            .Where(kv => kv.Value > 1)
            .Select(kv => new DuplicateIdEntry(kv.Key, kv.Value))
            .OrderBy(x => x.IdNumberNorm, StringComparer.Ordinal)
            .ToList();

        if (dups.Count == 0)
            return (true, null, dups);
        return (false, "导入文件中存在重复的身份证号，请导出明细核对并修正后重新导入。", dups);
    }

    public byte[] BuildDuplicateIdsExport(IReadOnlyList<DuplicateIdEntry> entries)
    {
        var rows = entries.Select(d => (object)new
        {
            归一化身份证号 = d.IdNumberNorm,
            出现次数 = d.OccurrenceCount
        });
        using var ms = new MemoryStream();
        MiniExcel.SaveAs(ms, rows, sheetName: "重复身份证");
        return ms.ToArray();
    }

    public byte[] CreateDeathImportTemplateBytes()
    {
        var sheet = new List<Dictionary<string, object?>>
        {
            new()
            {
                ["姓名"] = "李四",
                ["身份证号"] = "420527199002021234",
                ["死亡日期"] = "2024-03-01"
            }
        };
        using var ms = new MemoryStream();
        MiniExcel.SaveAs(ms, sheet);
        return ms.ToArray();
    }

    public (bool ok, string message, int batchId, List<DuplicateIdEntry>? duplicateIds) ImportAndCompare(
        string path,
        string? originalName,
        string idCol,
        string nameCol,
        string deathCol,
        string? sourceDataUnit = null,
        string? batchNote = null)
    {
        var (dupOk, _, dupList) = CheckDuplicateIdInImportFile(path, idCol);
        if (!dupOk)
            return (false, "导入文件中存在重复的身份证号，请导出明细并修正后再试。", 0, dupList);

        var rows = MiniExcel.Query(path, useHeaderRow: true).ToList();
        if (rows.Count == 0)
            return (false, "Excel 中没有数据行。", batchId: 0, null);

        var checksumErrors = new List<string>();
        for (var i = 0; i < rows.Count; i++)
        {
            var rowObj = rows[i];
            if (rowObj is not IDictionary<string, object> && rowObj is not IDictionary<string, object?>)
                continue;
            var row = ToRowDictionary(rowObj!);
            if (!row.TryGetValue(idCol, out object? idObj))
                continue;
            var raw = idObj?.ToString();
            if (string.IsNullOrWhiteSpace(raw))
                continue;
            var idNorm = IdNumberNormalizer.Clean(raw);
            if (string.IsNullOrEmpty(idNorm))
                continue;
            if (!IdNumberNormalizer.IsValidChecksum18(idNorm))
            {
                var excelRow = i + 2;
                checksumErrors.Add($"第{excelRow}行({idNorm})");
            }
        }

        if (checksumErrors.Count > 0)
        {
            var sample = string.Join("；", checksumErrors.Take(5));
            return (false, $"存在身份证号格式或末位校验位错误，请修正后重新导入。示例：{sample}", batchId: 0, null);
        }

        var parsed = new List<(string IdNorm, string DeathKey, string Name, string DeathDisplay)>();
        foreach (var rowObj in rows)
        {
            if (rowObj is not IDictionary<string, object> && rowObj is not IDictionary<string, object?>)
                continue;
            var row = ToRowDictionary(rowObj!);
            if (!row.TryGetValue(idCol, out object? idObj))
                continue;
            var idNorm = IdNumberNormalizer.Clean(idObj?.ToString());
            if (string.IsNullOrEmpty(idNorm))
                continue;

            row.TryGetValue(nameCol, out object? nameObj);
            row.TryGetValue(deathCol, out object? deathObj);

            var deathKey = DeathDateNormalizer.ToKey(deathObj);
            if (string.IsNullOrWhiteSpace(deathKey))
                deathKey = "UNKNOWN";

            var deathDisplay = DeathDateNormalizer.ToDisplay(deathObj);
            var name = nameObj?.ToString()?.Trim() ?? "";
            parsed.Add((idNorm, deathKey, name, deathDisplay));
        }

        if (parsed.Count == 0)
            return (false, "未解析到有效的身份证号列数据。", batchId: 0, null);

        var dupIdInBatch = parsed.GroupBy(p => p.IdNorm).FirstOrDefault(g => g.Count() > 1);
        if (dupIdInBatch != null)
        {
            var sample = string.Join("；", parsed.GroupBy(p => p.IdNorm).Where(g => g.Count() > 1).Select(g => g.Key).Take(8));
            return (false, $"同一批次导入中身份证号不能重复。涉及：{sample}", batchId: 0, null);
        }

        var started = DateTime.UtcNow;
        var batch = new CompareBatch
        {
            SourceFileName = originalName,
            SourceDataUnit = sourceDataUnit,
            BatchNote = batchNote,
            StartedAt = started,
            FinishedAt = started,
            RowCount = parsed.Count,
            MatchCount = 0
        };

        try
        {
            db.Ado.BeginTran();

            var batchId = Convert.ToInt32(db.Insertable(batch).ExecuteReturnIdentity());

            var deathRows = parsed.Select(p => new DeathRecord
            {
                BatchId = batchId,
                IdNumberNorm = p.IdNorm,
                DeathDateKey = p.DeathKey,
                Name = p.Name,
                DeathDateDisplay = p.DeathDisplay,
                SourceFileName = originalName,
                ImportedAt = started
            }).ToList();

            db.Insertable(deathRows).ExecuteCommand();

            var idList = deathRows.Select(d => d.IdNumberNorm).Distinct().ToList();
            var people = db.Queryable<PersonRegistry>()
                .Where(p => idList.Contains(p.IdNumber) && p.Status == "有效")
                .ToList();

            var matches = new List<CompareMatch>();
            foreach (var d in deathRows)
            {
                foreach (var p in people.Where(x => x.IdNumber == d.IdNumberNorm))
                {
                    matches.Add(new CompareMatch
                    {
                        BatchId = batchId,
                        IdNumberNorm = d.IdNumberNorm,
                        Name = string.IsNullOrWhiteSpace(d.Name) ? p.Name : d.Name,
                        DeathDateDisplay = d.DeathDateDisplay,
                        RegistryStatus = p.Status,
                        GridCommunity = p.GridCommunity,
                        SpecialCategory = p.SpecialCategory,
                        Contact = p.Contact,
                        RegistryPersonId = p.Id,
                        Confirmed = false,
                        ProcessOutcome = 0,
                        CreatedAt = started
                    });
                }
            }

            if (matches.Count > 0)
                db.Insertable(matches).ExecuteCommand();

            db.Updateable<CompareBatch>()
                .SetColumns(b => new CompareBatch
                {
                    MatchCount = matches.Count,
                    FinishedAt = DateTime.UtcNow,
                    RowCount = parsed.Count
                })
                .Where(b => b.Id == batchId)
                .ExecuteCommand();

            db.Ado.CommitTran();
            return (true, "比对完成。", batchId, null);
        }
        catch (Exception ex)
        {
            db.Ado.RollbackTran();
            return (false, $"导入或比对失败：{ex.Message}", batchId: 0, null);
        }
    }

    public List<CompareMatch> GetMatches(int batchId) =>
        db.Queryable<CompareMatch>()
            .Where(m => m.BatchId == batchId)
            .OrderBy(m => m.IdNumberNorm)
            .ToList();

    public void ConfirmAllInBatch(int batchId)
    {
        var ids = db.Queryable<CompareMatch>()
            .Where(m => m.BatchId == batchId && m.ProcessOutcome == 0)
            .Select(m => m.Id)
            .ToList();
        Confirm(batchId, ids);
    }

    public void Confirm(int batchId, List<int> matchIds)
    {
        if (matchIds.Count == 0)
            return;

        var utc = DateTime.UtcNow;
        var today = DateTime.Today;

        db.Ado.BeginTran();
        try
        {
            var matches = db.Queryable<CompareMatch>()
                .Where(m => matchIds.Contains(m.Id) && m.BatchId == batchId && m.ProcessOutcome == 0)
                .ToList();

            foreach (var m in matches)
            {
                if (m.RegistryPersonId is int pid && pid > 0)
                {
                    db.Updateable<PersonRegistry>()
                        .SetColumns(p => new PersonRegistry
                        {
                            Status = "无效",
                            CancelDate = today,
                            UpdatedAt = utc
                        })
                        .Where(p => p.Id == pid)
                        .ExecuteCommand();
                }
                else
                {
                    db.Updateable<PersonRegistry>()
                        .SetColumns(p => new PersonRegistry
                        {
                            Status = "无效",
                            CancelDate = today,
                            UpdatedAt = utc
                        })
                        .Where(p => p.IdNumber == m.IdNumberNorm && p.Status == "有效")
                        .ExecuteCommand();
                }
            }

            db.Updateable<CompareMatch>()
                .SetColumns(m => new CompareMatch
                {
                    Confirmed = true,
                    ConfirmedAt = utc,
                    ProcessOutcome = 1,
                    ProcessedAt = utc,
                    ProcessNote = "用户已确认取消特殊身份，底册已标为无效"
                })
                .Where(m => matchIds.Contains(m.Id))
                .ExecuteCommand();

            db.Ado.CommitTran();
        }
        catch
        {
            db.Ado.RollbackTran();
            throw;
        }
    }

    public (bool ok, string? error) MarkDeathInfoWrongForMatch(int matchId)
    {
        var m = db.Queryable<CompareMatch>().InSingle(matchId);
        if (m == null)
            return (false, "预警记录不存在。");
        if (m.ProcessOutcome != 0)
            return (false, "该条预警已处理过，无需重复操作。");
        var utc = DateTime.UtcNow;
        db.Updateable<CompareMatch>()
            .SetColumns(x => new CompareMatch
            {
                ProcessOutcome = 2,
                ProcessedAt = utc,
                ProcessNote = "死亡信息有误，已按用户操作消除预警（不改编底册）",
                // 不将底册标为无效；不设置 Confirmed
            })
            .Where(x => x.Id == matchId)
            .ExecuteCommand();
        return (true, null);
    }

    public CompareMatch? GetMatchById(int id) => db.Queryable<CompareMatch>().InSingle(id);

    public CompareBatch? GetBatchById(int id) => db.Queryable<CompareBatch>().InSingle(id);

    public List<PersonRegistry> GetValidPersonsByIdNumber(string idNumberNorm) =>
        db.Queryable<PersonRegistry>()
            .Where(p => p.IdNumber == idNumberNorm && p.Status == "有效")
            .OrderBy(p => p.Id)
            .ToList();

    /// <summary>本条比对预警对应的底册「有效」行：优先 RegistryPersonId，否则按身份证+特殊类别匹配。</summary>
    public PersonRegistry? GetRegistryPersonForMatch(CompareMatch m)
    {
        if (m.RegistryPersonId is int pid && pid > 0)
        {
            var byId = db.Queryable<PersonRegistry>().InSingle(pid);
            if (byId != null && byId.Status == "有效")
                return byId;
        }

        var cat = m.SpecialCategory?.Trim() ?? "";
        return db.Queryable<PersonRegistry>()
            .Where(p => p.IdNumber == m.IdNumberNorm && p.Status == "有效")
            .Where(p => (p.SpecialCategory ?? "").Trim() == cat)
            .OrderBy(p => p.Id)
            .ToList()
            .FirstOrDefault();
    }

    public List<DeathRecord> GetDeathRowsForBatch(int batchId) =>
        db.Queryable<DeathRecord>()
            .Where(d => d.BatchId == batchId)
            .OrderBy(d => d.Id)
            .ToList();

    public List<DeathRecord> GetDeathRecordsInBatchForId(int batchId, string idNumberNorm) =>
        db.Queryable<DeathRecord>()
            .Where(d => d.BatchId == batchId && d.IdNumberNorm == idNumberNorm)
            .OrderBy(d => d.Id)
            .ToList();

    public (int total, List<CompareBatchListRow> items) GetBatchHistoryPage(int pageIndex, int pageSize, string? keyword = null)
    {
        var q = db.Queryable<CompareBatch>();
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var k = keyword.Trim();
            q = q.Where(b =>
                (b.SourceFileName != null && b.SourceFileName.Contains(k))
                || (b.BatchNote != null && b.BatchNote.Contains(k))
                || (b.SourceDataUnit != null && b.SourceDataUnit.Contains(k)));
        }

        var total = q.Count();
        var list = q.OrderByDescending(b => b.StartedAt)
            .Skip(pageIndex * pageSize)
            .Take(pageSize)
            .ToList();
        if (list.Count == 0)
            return (total, new List<CompareBatchListRow>());

        var batchIds = list.Select(b => b.Id).ToList();
        var processedCounts = db.Queryable<CompareMatch>()
            .Where(m => batchIds.Contains(m.BatchId) && m.ProcessOutcome != 0)
            .GroupBy(m => m.BatchId)
            .Select(g => new { g.BatchId, Cnt = SqlFunc.AggregateCount(g.Id) })
            .ToList();
        var map = processedCounts.ToDictionary(x => x.BatchId, x => x.Cnt);
        var rows = list.Select(b => new CompareBatchListRow(b, map.GetValueOrDefault(b.Id))).ToList();
        return (total, rows);
    }

    public (int, int) GetMatchStatsForBatch(int batchId) =>
    (
        db.Queryable<CompareMatch>().Where(m => m.BatchId == batchId).Count(),
        db.Queryable<CompareMatch>().Where(m => m.BatchId == batchId && m.ProcessOutcome == 0).Count()
    );

    public byte[] ExportBatch(int batchId, string sheetName = "比对结果") =>
        BuildMatchExportBytes(batchId, sheetName);

    public byte[] ExportMatchProcessDetails(int batchId) =>
        BuildMatchExportBytes(batchId, "预警处理明细");

    private byte[] BuildMatchExportBytes(int batchId, string sheetName)
    {
        var rows = db.Queryable<CompareMatch>()
            .Where(m => m.BatchId == batchId)
            .OrderBy(m => m.IdNumberNorm)
            .ToList();

        var export = rows.Select(m => new
        {
            m.Name,
            身份证号 = m.IdNumberNorm,
            死亡日期 = m.DeathDateDisplay,
            底册状态 = m.RegistryStatus,
            底册行Id = m.RegistryPersonId,
            管辖网格 = m.GridCommunity,
            特殊类别 = m.SpecialCategory,
            联系方式 = m.Contact,
            处理结果 = m.ProcessOutcome switch
            {
                0 => "待处理",
                1 => "已确认取消身份(底册无效)",
                2 => "已标记为死亡信息有误",
                _ => "—"
            },
            处理说明 = m.ProcessNote,
            已确认标记底册无效 = m.Confirmed ? "是" : "否"
        });

        using var ms = new MemoryStream();
        MiniExcel.SaveAs(ms, export, sheetName: sheetName);
        return ms.ToArray();
    }

    public byte[] ExportDeathImportDetails(int batchId)
    {
        var list = GetDeathRowsForBatch(batchId);
        var export = list.Select(d => new
        {
            d.Name,
            身份证号 = d.IdNumberNorm,
            死亡日期 = d.DeathDateDisplay,
            死亡日期键 = d.DeathDateKey,
            源文件名 = d.SourceFileName,
            导入时间 = d.ImportedAt
        });
        using var ms = new MemoryStream();
        MiniExcel.SaveAs(ms, export, sheetName: "导入明细");
        return ms.ToArray();
    }

    public bool HasDeathRecordsForIdNumber(string idNumberNorm)
    {
        var id = IdNumberNormalizer.Clean(idNumberNorm);
        if (string.IsNullOrEmpty(id))
            return false;
        return db.Queryable<DeathRecord>().Any(d => d.IdNumberNorm == id);
    }

    /// <summary>底册 Excel 导入失败说明：含数据来源与死亡时间。</summary>
    public string BuildDeathImportFailureNote(string idNumberNorm)
    {
        var id = IdNumberNormalizer.Clean(idNumberNorm);
        var list = db.Queryable<DeathRecord>().Where(d => d.IdNumberNorm == id).OrderBy(d => d.ImportedAt).ToList();
        if (list.Count == 0)
            return "人员存在死亡登记信息。";
        var batchIds = list.Select(x => x.BatchId).Distinct().ToList();
        var batches = db.Queryable<CompareBatch>().In(batchIds).ToList().ToDictionary(b => b.Id);
        var parts = new List<string>();
        foreach (var d in list.Take(20))
        {
            batches.TryGetValue(d.BatchId, out var b);
            var unit = string.IsNullOrWhiteSpace(b?.SourceDataUnit) ? "未知来源" : b!.SourceDataUnit!.Trim();
            var file = string.IsNullOrWhiteSpace(d.SourceFileName)
                ? (string.IsNullOrWhiteSpace(b?.SourceFileName) ? "—" : b!.SourceFileName!.Trim())
                : d.SourceFileName!.Trim();
            var dd = string.IsNullOrWhiteSpace(d.DeathDateDisplay) ? d.DeathDateKey : d.DeathDateDisplay;
            parts.Add($"数据来源单位：{unit}；文件：{file}；死亡日期：{dd}");
        }

        return "人员存在死亡登记信息，无法通过导入直接写入「有效」底册。" + string.Join(" | ", parts);
    }

    public DeathHomeInsight BuildDeathHomeInsight(CompareMatch current)
    {
        var id = current.IdNumberNorm;
        var deaths = db.Queryable<DeathRecord>().Where(d => d.IdNumberNorm == id).ToList();
        if (deaths.Count == 0)
            return new DeathHomeInsight(false, false);

        var meaningful = deaths.Where(d => d.DeathDateKey != "UNKNOWN").ToList();
        var distinctMeaningfulKeys = meaningful.Select(d => d.DeathDateKey).Distinct().Count();
        var deathDateAnomaly = distinctMeaningfulKeys > 1;
        if (!deathDateAnomaly)
        {
            var disp = deaths
                .Select(d => (d.DeathDateDisplay ?? "").Trim())
                .Where(s => s.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (disp.Count > 1)
                deathDateAnomaly = true;
        }

        var sameDeathMulti = deaths.Count > 1 &&
                             deaths.Select(d => d.DeathDateKey).Distinct().Count() == 1;
        var hasProcessedOther = db.Queryable<CompareMatch>()
            .Any(m => m.IdNumberNorm == id && m.ProcessOutcome != 0 && m.Id != current.Id);
        var historyProcessingHint = sameDeathMulti && hasProcessedOther;

        return new DeathHomeInsight(deathDateAnomaly, historyProcessingHint);
    }

    public List<ProcessedMatchHistoryRow> GetProcessedDeathAlertHistory(string idNorm, int excludeMatchId, int take = 40)
    {
        var rows = db.Queryable<CompareMatch>()
            .Where(m => m.IdNumberNorm == idNorm && m.ProcessOutcome != 0 && m.Id != excludeMatchId)
            .OrderByDescending(m => m.ProcessedAt ?? m.CreatedAt)
            .Take(take)
            .ToList();
        if (rows.Count == 0)
            return new List<ProcessedMatchHistoryRow>();
        var batchIds = rows.Select(m => m.BatchId).Distinct().ToList();
        var batchMap = batchIds.Count == 0
            ? new Dictionary<int, CompareBatch>()
            : db.Queryable<CompareBatch>().Where(b => batchIds.Contains(b.Id)).ToList().ToDictionary(b => b.Id);
        return rows.Select(m =>
        {
            batchMap.TryGetValue(m.BatchId, out var b);
            return new ProcessedMatchHistoryRow(
                m.Id,
                m.BatchId,
                FormatBatchDisplayName(b),
                b?.SourceDataUnit,
                m.Name,
                m.IdNumberNorm,
                m.SpecialCategory,
                m.ProcessOutcome,
                m.ProcessNote,
                m.ProcessedAt,
                m.DeathDateDisplay);
        }).ToList();
    }

    /// <summary>底册保存为「有效」且与死亡库并存（用户已确认）时，为每条死亡登记生成待处理比对命中。</summary>
    public void SyncRegistryDeathAlertMatches(PersonRegistry registryRow)
    {
        if (registryRow.Status != "有效")
            return;
        var id = IdNumberNormalizer.Clean(registryRow.IdNumber);
        if (string.IsNullOrEmpty(id))
            return;
        var deaths = db.Queryable<DeathRecord>().Where(d => d.IdNumberNorm == id).OrderBy(d => d.Id).ToList();
        if (deaths.Count == 0)
            return;

        var utc = DateTime.UtcNow;
        var batch = new CompareBatch
        {
            SourceFileName = null,
            SourceDataUnit = "底册维护",
            BatchNote = "有效身份与死亡登记比对（系统自动）",
            StartedAt = utc,
            FinishedAt = utc,
            RowCount = 0,
            MatchCount = 0
        };
        var batchId = Convert.ToInt32(db.Insertable(batch).ExecuteReturnIdentity());

        var matches = new List<CompareMatch>();
        foreach (var d in deaths)
        {
            var disp = (d.DeathDateDisplay ?? "").Trim();
            var existsPending = db.Queryable<CompareMatch>().Any(m =>
                m.RegistryPersonId == registryRow.Id
                && m.IdNumberNorm == id
                && m.ProcessOutcome == 0
                && ((m.DeathDateDisplay ?? "").Trim() == disp));
            if (existsPending)
                continue;

            matches.Add(new CompareMatch
            {
                BatchId = batchId,
                IdNumberNorm = id,
                Name = string.IsNullOrWhiteSpace(d.Name) ? registryRow.Name : d.Name,
                DeathDateDisplay = d.DeathDateDisplay,
                RegistryStatus = registryRow.Status,
                GridCommunity = registryRow.GridCommunity,
                SpecialCategory = registryRow.SpecialCategory,
                Contact = registryRow.Contact,
                RegistryPersonId = registryRow.Id,
                Confirmed = false,
                ProcessOutcome = 0,
                CreatedAt = utc
            });
        }

        if (matches.Count > 0)
            db.Insertable(matches).ExecuteCommand();

        db.Updateable<CompareBatch>()
            .SetColumns(b => new CompareBatch
            {
                MatchCount = matches.Count,
                RowCount = deaths.Count,
                FinishedAt = utc
            })
            .Where(b => b.Id == batchId)
            .ExecuteCommand();
    }
}
