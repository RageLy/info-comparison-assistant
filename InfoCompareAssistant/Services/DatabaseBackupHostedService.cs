using Microsoft.Extensions.Hosting;

namespace InfoCompareAssistant.Services;

/// <summary>
/// 应用退出时备份 SQLite：Windows 且存在 D 盘时复制到 D:\数据备份，否则到应用数据目录\数据备份（统信/Linux 等同后者）。
/// </summary>
public sealed class DatabaseBackupHostedService(AppPaths paths, IHostApplicationLifetime lifetime)
    : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        lifetime.ApplicationStopping.Register(() =>
        {
            try
            {
                if (!File.Exists(paths.DatabasePath))
                    return;

                var destDir = ResolveBackupDirectory(paths);
                Directory.CreateDirectory(destDir);
                var dest = Path.Combine(destDir, $"app_{DateTime.Now:yyyyMMdd_HHmmss}.db");
                File.Copy(paths.DatabasePath, dest, overwrite: false);
            }
            catch
            {
                // 无权限、磁盘满、无 D 盘等时静默跳过
            }
        });
        return Task.CompletedTask;
    }

    private static string ResolveBackupDirectory(AppPaths paths)
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                var dRoot = "D:" + Path.DirectorySeparatorChar;
                if (Directory.Exists(dRoot))
                    return Path.Combine(dRoot, "数据备份");
            }
            catch
            {
                // 忽略，走下方
            }
        }

        return Path.Combine(paths.DataDirectory, "数据备份");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
