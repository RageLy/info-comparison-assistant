using InfoCompareAssistant.Data;
using MiniExcelLibs;
using SqlSugar;

namespace InfoCompareAssistant.Services;

public sealed record RegistryImportProgress(int Processed, int Total, string Caption);

public sealed record RegistryImportFailure(int ExcelRow, string Message, string? IdCard, string? PersonName);

public sealed class RegistryImportResult
{
    public int Upserted { get; set; }
    public int TotalRows { get; set; }
    public int SkippedBlankId { get; set; }
    public List<RegistryImportFailure> Failures { get; } = new();
    public List<string> FatalMessages { get; } = new();
    public bool CanImportRows => FatalMessages.Count == 0;
}

public sealed record RegistryUpsertResult(bool Ok, string? Error, bool NeedsDeathCoexistConfirm);

public sealed class RegistryService(SqlSugarClient db, CompareFlowService compare)
{
    public const string DirCategory = "Category";
    public const string DirCommunity = "Community";

    public record PagedResult(int Total, List<PersonRegistry> Items);

    private ISugarQueryable<PersonRegistry> RegistryFilteredQuery(string? grid, string? category, string? nameOrIdKeyword, string? statusFilter)
    {
        var q = db.Queryable<PersonRegistry>();
        if (!string.IsNullOrWhiteSpace(grid))
            q = q.Where(p => p.GridCommunity != null && p.GridCommunity == grid);
        if (!string.IsNullOrWhiteSpace(category))
            q = q.Where(p => p.SpecialCategory != null && p.SpecialCategory == category);
        if (!string.IsNullOrWhiteSpace(statusFilter) && (statusFilter == "有效" || statusFilter == "无效"))
            q = q.Where(p => p.Status == statusFilter);
        if (!string.IsNullOrWhiteSpace(nameOrIdKeyword))
        {
            var k = nameOrIdKeyword.Trim();
            q = q.Where(p =>
                (p.Name != null && p.Name.Contains(k)) ||
                (p.IdNumber != null && p.IdNumber.Contains(k)));
        }

        return q;
    }

    public PagedResult Search(string? grid, string? category, string? nameOrIdKeyword, string? statusFilter, int pageIndex, int pageSize)
    {
        var q = RegistryFilteredQuery(grid, category, nameOrIdKeyword, statusFilter);
        var total = q.Clone().Count();
        var items = q.OrderBy("Id DESC")
            .Skip(pageIndex * pageSize)
            .Take(pageSize)
            .ToList();
        return new PagedResult(total, items);
    }

    /// <summary>按与列表相同的筛选条件导出底册（不分页）。</summary>
    public byte[] ExportRegistryFiltered(string? grid, string? category, string? nameOrIdKeyword, string? statusFilter)
    {
        var items = RegistryFilteredQuery(grid, category, nameOrIdKeyword, statusFilter)
            .OrderByDescending(p => p.Id)
            .ToList();
        var rows = items.Select(p => new
        {
            p.Id,
            身份证号 = p.IdNumber,
            姓名 = p.Name,
            联系方式 = p.Contact,
            管辖网格 = p.GridCommunity,
            特殊类别 = p.SpecialCategory,
            状态 = p.Status,
            备注 = p.Remark,
            注销日期 = p.CancelDate,
            创建时间 = p.CreatedAt,
            更新时间 = p.UpdatedAt
        });
        using var ms = new MemoryStream();
        MiniExcel.SaveAs(ms, rows, sheetName: "人员底册");
        return ms.ToArray();
    }

    public PersonRegistry? GetById(int id) =>
        db.Queryable<PersonRegistry>().InSingle(id);

    /// <summary>是否存在：身份证 + 特殊身份（空按同一空串）+ 状态均为「有效」。</summary>
    public bool ExistsDuplicateValid(string idNumber, string? specialCategory, int? excludeId)
    {
        var id = IdNumberNormalizer.Clean(idNumber);
        var cat = specialCategory?.Trim() ?? "";
        var q = db.Queryable<PersonRegistry>()
            .Where(p => p.IdNumber == id)
            .Where(p => (p.SpecialCategory ?? "") == cat)
            .Where(p => p.Status == "有效");
        if (excludeId is > 0)
            q = q.Where(p => p.Id != excludeId.Value);
        return q.Any();
    }

    /// <summary>新增或更新底册行。重复规则：同一身份证+特殊身份不能同时存在两条「有效」。与死亡库并存时需用户确认后才会写入并同步生成待办。</summary>
    public RegistryUpsertResult Upsert(PersonRegistry row, bool confirmDeathCoexist = false)
    {
        row.IdNumber = IdNumberNormalizer.Clean(row.IdNumber);
        if (string.IsNullOrWhiteSpace(row.IdNumber))
            return new RegistryUpsertResult(false, "身份证号不能为空。", false);
        if (!IdNumberNormalizer.IsValidChecksum18(row.IdNumber))
            return new RegistryUpsertResult(false, "身份证号格式不正确或末位校验位错误。", false);
        var now = DateTime.UtcNow;
        row.UpdatedAt = now;
        if (string.IsNullOrWhiteSpace(row.Status))
            row.Status = "有效";
        if (row.Status is not ("有效" or "无效"))
            return new RegistryUpsertResult(false, "状态只能为「有效」或「无效」。", false);

        var catKey = row.SpecialCategory?.Trim() ?? "";
        var gridKey = row.GridCommunity?.Trim() ?? "";
        if (string.IsNullOrEmpty(catKey))
            return new RegistryUpsertResult(false, "请选择人员类别。", false);
        if (string.IsNullOrEmpty(gridKey))
            return new RegistryUpsertResult(false, "请选择所属网格。", false);
        if (!DirectoryNameExists(DirCategory, catKey))
            return new RegistryUpsertResult(false, "所选人员类别不在维护目录中，请先在左侧「身份与社区目录」页面维护后再选择。", false);
        if (!DirectoryNameExists(DirCommunity, gridKey))
            return new RegistryUpsertResult(false, "所选所属网格不在维护目录中，请先在左侧「身份与社区目录」页面维护后再选择。", false);

        row.SpecialCategory = catKey;
        row.GridCommunity = gridKey;

        if (row.Status == "有效" && compare.HasDeathRecordsForIdNumber(row.IdNumber) && !confirmDeathCoexist)
        {
            return new RegistryUpsertResult(false,
                "该人员在系统中已有死亡登记信息。继续保存将保留本条「有效」特殊身份，并在首页「最新预警待办」中生成与死亡登记相关的待处理条目。是否继续？",
                true);
        }

        if (row.Id == 0)
        {
            if (row.Status == "有效" && ExistsDuplicateValid(row.IdNumber, catKey, excludeId: null))
                return new RegistryUpsertResult(false, "已存在身份证号、特殊身份一致且均为「有效」的记录，不能重复新增。若曾有一条「无效」记录，可再增一条「有效」记录。", false);
            row.CreatedAt = now;
            var newId = Convert.ToInt32(db.Insertable(row).ExecuteReturnIdentity());
            row.Id = newId;
            if (row.Status == "有效" && confirmDeathCoexist && compare.HasDeathRecordsForIdNumber(row.IdNumber))
                compare.SyncRegistryDeathAlertMatches(row);
            return new RegistryUpsertResult(true, null, false);
        }

        if (row.Status == "有效" && ExistsDuplicateValid(row.IdNumber, catKey, excludeId: row.Id))
            return new RegistryUpsertResult(false, "修改为「有效」会与另一条在册记录冲突（身份证与特殊身份均相同）。", false);

        var existing = db.Queryable<PersonRegistry>().InSingle(row.Id);
        if (existing == null)
            return new RegistryUpsertResult(false, "记录不存在。", false);
        row.CreatedAt = existing.CreatedAt;
        db.Updateable(row).ExecuteCommand();
        if (row.Status == "有效" && confirmDeathCoexist && compare.HasDeathRecordsForIdNumber(row.IdNumber))
            compare.SyncRegistryDeathAlertMatches(row);
        return new RegistryUpsertResult(true, null, false);
    }

    public void DeleteById(int id) =>
        db.Deleteable<PersonRegistry>().Where(p => p.Id == id).ExecuteCommand();

    public byte[] CreateRosterImportTemplateBytes()
    {
        var sheet = new List<Dictionary<string, object?>>
        {
            new()
            {
                ["身份证号"] = "420527199001011234",
                ["姓名"] = "张三",
                ["联系方式"] = "13800000000",
                ["管辖网格"] = "茅坪镇一社区",
                ["特殊类别"] = "低保",
                ["状态"] = "有效",
                ["备注"] = "示例说明，可删除本行后填写"
            }
        };
        using var ms = new MemoryStream();
        MiniExcel.SaveAs(ms, sheet);
        return ms.ToArray();
    }

    public static byte[] BuildFailureReportBytes(IReadOnlyList<RegistryImportFailure> failures)
    {
        var rows = failures.Select(f => new Dictionary<string, object?>
        {
            ["Excel行号"] = f.ExcelRow,
            ["失败说明"] = f.Message,
            ["身份证号"] = f.IdCard ?? "",
            ["姓名"] = f.PersonName ?? ""
        }).ToList();
        using var ms = new MemoryStream();
        MiniExcel.SaveAs(ms, rows);
        return ms.ToArray();
    }

    public RegistryImportResult ImportFromExcel(Stream stream, IProgress<RegistryImportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new RegistryImportResult();
        progress?.Report(new RegistryImportProgress(0, 0, "正在读取 Excel…"));

        List<dynamic> rows;
        try
        {
            rows = MiniExcel.Query(stream, useHeaderRow: true).ToList();
        }
        catch (Exception ex)
        {
            result.FatalMessages.Add($"读取 Excel 失败：{ex.Message}");
            return result;
        }

        if (rows.Count == 0)
        {
            result.FatalMessages.Add("Excel 无数据行。");
            return result;
        }

        if (rows[0] is not IDictionary<string, object> headerRow)
        {
            result.FatalMessages.Add("Excel 行格式无法识别。");
            return result;
        }

        var headers = headerRow.Keys.ToList();
        var idCol = FindColumn(headers, "身份证", "证件号", "证件号码", "身份证号");
        var nameCol = FindColumn(headers, "姓名", "名字");
        var contactCol = FindColumn(headers, "联系", "电话", "手机");
        var gridCol = FindColumn(headers, "网格", "社区", "管辖");
        var catCol = FindColumn(headers, "类别", "特殊");
        var statusCol = FindColumn(headers, "状态");
        var remarkCol = FindColumn(headers, "备注", "说明");

        if (idCol == null || nameCol == null)
        {
            result.FatalMessages.Add("未识别到「身份证号」或「姓名」列，请确保表头包含关键词：身份证、姓名。亦可下载模板对照填写。");
            return result;
        }

        result.TotalRows = rows.Count;
        progress?.Report(new RegistryImportProgress(0, result.TotalRows, $"共 {result.TotalRows} 行，开始导入…"));

        var index = 0;
        foreach (var rowObj in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            index++;
            var excelRow = index + 1;
            progress?.Report(new RegistryImportProgress(index, result.TotalRows, $"正在处理第 {index} / {result.TotalRows} 行…"));

            if (rowObj is not IDictionary<string, object> dict)
                continue;

            string? rawIdHint = null;
            string? nameHint = null;
            try
            {
                if (!dict.TryGetValue(idCol, out object? idObj))
                {
                    result.SkippedBlankId++;
                    continue;
                }

                rawIdHint = idObj?.ToString();
                dict.TryGetValue(nameCol, out object? nObj);
                nameHint = nObj?.ToString()?.Trim();
                var id = IdNumberNormalizer.Clean(rawIdHint);
                if (string.IsNullOrEmpty(id))
                {
                    result.SkippedBlankId++;
                    continue;
                }

                if (!IdNumberNormalizer.IsValidChecksum18(id))
                {
                    result.Failures.Add(new RegistryImportFailure(excelRow, "身份证号格式或末位校验位错误", rawIdHint, nameHint));
                    continue;
                }

                var name = nameHint ?? "";
                var statusRaw = Read(dict, statusCol);
                var row = new PersonRegistry
                {
                    IdNumber = id,
                    Name = name,
                    Contact = Read(dict, contactCol),
                    GridCommunity = Read(dict, gridCol),
                    SpecialCategory = Read(dict, catCol),
                    Status = MapImportStatus(statusRaw),
                    Remark = Read(dict, remarkCol)
                };
                EnsureDirectoryContains(DirCommunity, row.GridCommunity);
                EnsureDirectoryContains(DirCategory, row.SpecialCategory);
                if (row.Status == "有效" && compare.HasDeathRecordsForIdNumber(row.IdNumber))
                {
                    result.Failures.Add(new RegistryImportFailure(excelRow, compare.BuildDeathImportFailureNote(row.IdNumber),
                        rawIdHint, nameHint));
                    continue;
                }

                var ur = Upsert(row);
                if (!ur.Ok)
                {
                    result.Failures.Add(new RegistryImportFailure(excelRow, ur.Error ?? "导入失败", rawIdHint, nameHint));
                    continue;
                }

                result.Upserted++;
            }
            catch (Exception ex)
            {
                result.Failures.Add(new RegistryImportFailure(excelRow, ex.Message, rawIdHint, nameHint));
            }
        }

        progress?.Report(new RegistryImportProgress(result.TotalRows, result.TotalRows, "导入完成"));
        return result;
    }

    public List<string> GetDistinctGridCommunities() => ListDirectoryNames(DirCommunity);

    public List<string> GetDistinctSpecialCategories() => ListDirectoryNames(DirCategory);

    public List<RosterDirectoryItem> ListDirectoryItems(string kind) =>
        db.Queryable<RosterDirectoryItem>()
            .Where(x => x.Kind == kind)
            .OrderBy(x => x.Name)
            .ToList();

    public List<string> ListDirectoryNames(string kind) =>
        db.Queryable<RosterDirectoryItem>()
            .Where(x => x.Kind == kind)
            .OrderBy(x => x.Name)
            .Select(x => x.Name)
            .ToList();

    public bool DirectoryNameExists(string kind, string name) =>
        db.Queryable<RosterDirectoryItem>().Any(x => x.Kind == kind && x.Name == name);

    public void EnsureDirectoryContains(string kind, string? value)
    {
        var name = value?.Trim() ?? "";
        if (string.IsNullOrEmpty(name))
            return;
        if (DirectoryNameExists(kind, name))
            return;
        db.Insertable(new RosterDirectoryItem { Kind = kind, Name = name, CreatedAt = DateTime.UtcNow }).ExecuteCommand();
    }

    public (bool Ok, string? Error) AddDirectoryItem(string kind, string? name)
    {
        name = name?.Trim() ?? "";
        if (string.IsNullOrEmpty(name))
            return (false, "名称不能为空。");
        if (kind is not (DirCategory or DirCommunity))
            return (false, "目录类型无效。");
        if (DirectoryNameExists(kind, name))
            return (false, "已存在同名项。");
        db.Insertable(new RosterDirectoryItem { Kind = kind, Name = name, CreatedAt = DateTime.UtcNow }).ExecuteCommand();
        return (true, null);
    }

    public (bool Ok, string? Error) DeleteDirectoryItem(int id)
    {
        var row = db.Queryable<RosterDirectoryItem>().InSingle(id);
        if (row == null)
            return (false, "记录不存在。");
        if (row.Kind == DirCategory)
        {
            if (db.Queryable<PersonRegistry>().Any(p => (p.SpecialCategory ?? "").Trim() == row.Name))
                return (false, "底册中仍有人员使用该类别，无法删除。");
        }
        else if (row.Kind == DirCommunity)
        {
            if (db.Queryable<PersonRegistry>().Any(p => (p.GridCommunity ?? "").Trim() == row.Name))
                return (false, "底册中仍有人员使用该网格，无法删除。");
        }

        db.Deleteable<RosterDirectoryItem>().Where(x => x.Id == id).ExecuteCommand();
        return (true, null);
    }

    private static string MapImportStatus(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "有效";
        var s = raw.Trim();
        if (s is "无效" or "已故" or "迁出")
            return "无效";
        if (s is "有效" or "正常")
            return "有效";
        return "有效";
    }

    private static string? Read(IDictionary<string, object> dict, string? col)
    {
        if (col == null)
            return null;
        return dict.TryGetValue(col, out object? v) ? v?.ToString()?.Trim() : null;
    }

    private static string? FindColumn(List<string> headers, params string[] keywords)
    {
        foreach (var h in headers)
        {
            foreach (var k in keywords)
            {
                if (h.Contains(k, StringComparison.OrdinalIgnoreCase))
                    return h;
            }
        }

        return null;
    }
}
