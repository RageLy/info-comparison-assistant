using InfoCompareAssistant.Data;
using Microsoft.Extensions.Hosting;

namespace InfoCompareAssistant.Services;

public sealed class DatabaseInitializer(AppPaths paths) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        using var db = SqlSugarFactory.CreateClient(paths.DatabasePath);
        if (!File.Exists(paths.DatabasePath))
            db.DbMaintenance.CreateDatabase();

        if (PersonRegistryMigration.IsLegacyPersonRegistry(db))
            PersonRegistryMigration.MigrateLegacyPersonRegistry(db);

        db.CodeFirst.InitTables(typeof(PersonRegistry), typeof(DeathRecord), typeof(CompareBatch), typeof(CompareMatch),
            typeof(RosterDirectoryItem));
        PersonRegistryMigration.AddExtendedColumnsIfMissing(db);
        PersonRegistryMigration.EnsureRegistryPartialUniqueIndex(db);
        PersonRegistryMigration.SeedRosterDirectoryIfEmpty(db);
        PersonRegistryMigration.RelaxDeathRecordUniqueIndex(db);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
