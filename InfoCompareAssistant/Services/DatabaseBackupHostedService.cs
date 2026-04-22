using Microsoft.Extensions.Hosting;

namespace InfoCompareAssistant.Services;

/// <summary>应用退出时将 SQLite 复制到 D:\数据备份（失败则忽略）。</summary>
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

                var destDir = @"D:\数据备份";
                Directory.CreateDirectory(destDir);
                var dest = Path.Combine(destDir, $"app_{DateTime.Now:yyyyMMdd_HHmmss}.db");
                File.Copy(paths.DatabasePath, dest, overwrite: false);
            }
            catch
            {
                // 离线环境或无 D 盘时静默跳过
            }
        });
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
