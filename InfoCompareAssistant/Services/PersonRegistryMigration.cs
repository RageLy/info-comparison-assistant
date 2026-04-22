using InfoCompareAssistant.Data;
using SqlSugar;

namespace InfoCompareAssistant.Services;

/// <summary>
/// 将旧版「身份证号为主键」底册表迁移为新版（自增 Id、有效/无效、备注），并补齐比对表字段与唯一索引。
/// </summary>
public static class PersonRegistryMigration
{
    public static bool IsLegacyPersonRegistry(SqlSugarClient db) =>
        TableExists(db, "PersonRegistry") && !HasIdentityIdPrimaryKey(db, "PersonRegistry");

    public static void MigrateLegacyPersonRegistry(SqlSugarClient db)
    {
        db.Ado.ExecuteCommand("ALTER TABLE PersonRegistry RENAME TO PersonRegistry_legacy;");
        db.CodeFirst.InitTables(typeof(PersonRegistry));
        db.Ado.ExecuteCommand(
            """
            INSERT INTO PersonRegistry (IdNumber, Name, Contact, GridCommunity, SpecialCategory, Status, Remark, CancelDate, CreatedAt, UpdatedAt)
            SELECT IdNumber, Name, Contact, GridCommunity, SpecialCategory,
                   CASE WHEN TRIM(COALESCE(Status,'')) IN ('已故','迁出','无效') THEN '无效' ELSE '有效' END,
                   NULL, CancelDate, CreatedAt, UpdatedAt
            FROM PersonRegistry_legacy;
            """);
        db.Ado.ExecuteCommand("DROP TABLE PersonRegistry_legacy;");
    }

    public static void AddExtendedColumnsIfMissing(SqlSugarClient db)
    {
        if (!ColumnExists(db, "CompareBatch", "SourceDataUnit"))
            db.Ado.ExecuteCommand("ALTER TABLE CompareBatch ADD COLUMN SourceDataUnit TEXT;");
        if (!ColumnExists(db, "CompareBatch", "BatchNote"))
            db.Ado.ExecuteCommand("ALTER TABLE CompareBatch ADD COLUMN BatchNote TEXT;");
        if (!ColumnExists(db, "CompareMatch", "RegistryPersonId"))
            db.Ado.ExecuteCommand("ALTER TABLE CompareMatch ADD COLUMN RegistryPersonId INTEGER;");
        if (!ColumnExists(db, "CompareMatch", "ProcessOutcome"))
            db.Ado.ExecuteCommand("ALTER TABLE CompareMatch ADD COLUMN ProcessOutcome INTEGER NOT NULL DEFAULT 0;");
        if (!ColumnExists(db, "CompareMatch", "ProcessNote"))
            db.Ado.ExecuteCommand("ALTER TABLE CompareMatch ADD COLUMN ProcessNote TEXT;");
        if (!ColumnExists(db, "CompareMatch", "ProcessedAt"))
            db.Ado.ExecuteCommand("ALTER TABLE CompareMatch ADD COLUMN ProcessedAt TEXT;");

        MigrateCompareMatchProcessOutcomeFromConfirmed(db);
    }

    /// <summary>将旧版仅「Confirmed」的标记迁移为 ProcessOutcome（只执行一次）。</summary>
    private static void MigrateCompareMatchProcessOutcomeFromConfirmed(SqlSugarClient db)
    {
        if (!ColumnExists(db, "CompareMatch", "ProcessOutcome"))
            return;
        var n = db.Ado.GetInt("SELECT COUNT(*) FROM CompareMatch WHERE ProcessOutcome = 0 AND Confirmed = 1");
        if (n == 0)
            return;
        db.Ado.ExecuteCommand("UPDATE CompareMatch SET ProcessOutcome = 1, ProcessedAt = ConfirmedAt, ProcessNote = '用户已确认取消身份' WHERE ProcessOutcome = 0 AND Confirmed = 1;");
    }

    public static void EnsureRegistryPartialUniqueIndex(SqlSugarClient db)
    {
        db.Ado.ExecuteCommand(
            """
            CREATE UNIQUE INDEX IF NOT EXISTS ux_registry_valid_id_cat
            ON PersonRegistry (IdNumber, ifnull(SpecialCategory,''))
            WHERE Status = '有效';
            """);
    }

    private static bool TableExists(SqlSugarClient db, string table) =>
        db.Ado.GetInt($"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{table}'") > 0;

    private static bool HasIdentityIdPrimaryKey(SqlSugarClient db, string table)
    {
        var dt = db.Ado.GetDataTable($"PRAGMA table_info('{table}')");
        foreach (System.Data.DataRow row in dt.Rows)
        {
            var name = row["name"]?.ToString();
            var pk = Convert.ToInt32(row["pk"]);
            if (name == "Id" && pk == 1)
                return true;
        }

        return false;
    }

    private static bool ColumnExists(SqlSugarClient db, string table, string column) =>
        db.Ado.GetInt($"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name = '{column}'") > 0;

    /// <summary>允许同一身份证+死亡日期多次导入（与历史重复亦允许）；保留非唯一索引便于查询。</summary>
    public static void RelaxDeathRecordUniqueIndex(SqlSugarClient db)
    {
        if (!TableExists(db, "DeathRecord"))
            return;
        db.Ado.ExecuteCommand("DROP INDEX IF EXISTS ux_death_id_date;");
        db.Ado.ExecuteCommand(
            "CREATE INDEX IF NOT EXISTS ix_death_id_date ON DeathRecord(IdNumberNorm, DeathDateKey);");
    }

    /// <summary>首次部署时从底册去重填充目录表；已有目录数据时不覆盖。</summary>
    public static void SeedRosterDirectoryIfEmpty(SqlSugarClient db)
    {
        if (!TableExists(db, "RosterDirectoryItem"))
            return;
        var n = db.Ado.GetInt("SELECT COUNT(*) FROM RosterDirectoryItem");
        if (n > 0)
            return;
        var utc = DateTime.UtcNow;
        var cats = db.Ado.SqlQuery<string>(
            """
            SELECT TRIM("SpecialCategory") AS v FROM "PersonRegistry"
            WHERE "SpecialCategory" IS NOT NULL AND TRIM("SpecialCategory") != ''
            GROUP BY TRIM("SpecialCategory")
            """);
        var grids = db.Ado.SqlQuery<string>(
            """
            SELECT TRIM("GridCommunity") AS v FROM "PersonRegistry"
            WHERE "GridCommunity" IS NOT NULL AND TRIM("GridCommunity") != ''
            GROUP BY TRIM("GridCommunity")
            """);
        var rows = new List<RosterDirectoryItem>();
        foreach (var c in cats)
            rows.Add(new RosterDirectoryItem { Kind = RegistryService.DirCategory, Name = c, CreatedAt = utc });
        foreach (var g in grids)
            rows.Add(new RosterDirectoryItem { Kind = RegistryService.DirCommunity, Name = g, CreatedAt = utc });
        if (rows.Count > 0)
            db.Insertable(rows).ExecuteCommand();
    }
}
