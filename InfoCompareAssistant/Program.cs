using InfoCompareAssistant.Components;
using InfoCompareAssistant.Services;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Photino.NET;

namespace InfoCompareAssistant;

public class Program
{
    [STAThread]
    public static void Main(string[] args) => MainAsync(args).GetAwaiter().GetResult();

    private static async Task MainAsync(string[] args)
    {
        if (args.Contains("--clear-compare-tables", StringComparer.OrdinalIgnoreCase))
        {
            ClearCompareTables();
            return;
        }

        var usePhotino = !args.Contains("--browser", StringComparer.OrdinalIgnoreCase);

        var builder = WebApplication.CreateBuilder(args);

        // 使用 0 端口由系统分配，避免 8765 被上次进程占用导致启动失败
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        builder.Services.AddSingleton<AppPaths>();
        builder.Services.AddHostedService<DatabaseInitializer>();
        builder.Services.AddHostedService<DatabaseBackupHostedService>();
        builder.Services.AddScoped<SqlSugar.SqlSugarClient>(sp =>
        {
            var paths = sp.GetRequiredService<AppPaths>();
            return SqlSugarFactory.CreateClient(paths.DatabasePath);
        });
        builder.Services.AddScoped<DashboardService>();
        builder.Services.AddScoped<CompareFlowService>();
        builder.Services.AddScoped<RegistryService>();
        builder.Services.AddScoped<CompareSessionState>();
        // 单机窗口：保证顶栏与页根两个 InteractiveServer 岛注入同一实例（Scoped 在部分组合下会各岛各一份）。
        builder.Services.AddSingleton<AboutDialogState>();

        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents();

        var app = builder.Build();

        if (!app.Environment.IsDevelopment())
            app.UseExceptionHandler("/Error", createScopeForErrors: true);

        app.UseStaticFiles();
        app.UseAntiforgery();

        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();

        app.MapGet("/api/export/{batchId:int}", (HttpContext ctx, int batchId) =>
        {
            var svc = ctx.RequestServices.GetRequiredService<CompareFlowService>();
            var bytes = svc.ExportBatch(batchId);
            return Results.File(bytes,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"比对结果_{batchId}.xlsx");
        });

        app.MapGet("/api/roster-import-template", (RegistryService registry) =>
        {
            var bytes = registry.CreateRosterImportTemplateBytes();
            return Results.File(bytes,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                "人员底册导入模板.xlsx");
        });

        app.MapGet("/api/death-import-template", (CompareFlowService compare) =>
        {
            var bytes = compare.CreateDeathImportTemplateBytes();
            return Results.File(bytes,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                "死亡名单导入模板.xlsx");
        });

        if (usePhotino)
        {
            await app.StartAsync();
            var url = ResolveListeningUrl(app);
            var env = app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>();
            var webRoot = env.WebRootPath;
            var icoPath = Path.GetFullPath(Path.Combine(webRoot, "logo.ico"));
            if (!File.Exists(icoPath))
                icoPath = Path.GetFullPath(Path.Combine(webRoot, "logo.png"));
            var window = new PhotinoWindow()
                .SetTitle("信息比对助手")
                .SetUseOsDefaultSize(true)
                .SetResizable(true);
            if (File.Exists(icoPath))
                window = window.SetIconFile(icoPath);
            window.Load(url);
            window.WaitForClose();
            await app.StopAsync();
        }
        else
        {
            var lifetime = app.Services.GetRequiredService<Microsoft.Extensions.Hosting.IHostApplicationLifetime>();
            lifetime.ApplicationStarted.Register(() =>
            {
                try
                {
                    var url = ResolveListeningUrl(app);
                    Console.WriteLine();
                    Console.WriteLine("============================================================");
                    Console.WriteLine($"  请在浏览器中打开: {url}");
                    Console.WriteLine("============================================================");
                    Console.WriteLine();
                }
                catch
                {
                    // ignore
                }
            });
            await app.RunAsync();
        }
    }

    /// <summary>
    /// 清空比对相关表（用于换逻辑后重测）。命令行示例：
    /// <c>dotnet run --no-launch-profile -- --clear-compare-tables</c>（避免 launchSettings 吞掉参数）；
    /// 或直接运行已编译的 <c>InfoCompareAssistant.exe --clear-compare-tables</c>。
    /// </summary>
    private static void ClearCompareTables()
    {
        var paths = new AppPaths();
        if (!File.Exists(paths.DatabasePath))
        {
            Console.WriteLine($"数据库不存在：{paths.DatabasePath}");
            Environment.ExitCode = 1;
            return;
        }

        using var db = SqlSugarFactory.CreateClient(paths.DatabasePath);
        db.Ado.BeginTran();
        try
        {
            db.Ado.ExecuteCommand("DELETE FROM CompareMatch;");
            db.Ado.ExecuteCommand("DELETE FROM DeathRecord;");
            db.Ado.ExecuteCommand("DELETE FROM CompareBatch;");
            db.Ado.ExecuteCommand(
                "DELETE FROM sqlite_sequence WHERE name IN ('CompareMatch','DeathRecord','CompareBatch');");
            db.Ado.CommitTran();
        }
        catch
        {
            db.Ado.RollbackTran();
            throw;
        }

        Console.WriteLine("已清空表：CompareMatch、DeathRecord、CompareBatch（并重置自增计数）。");
        Console.WriteLine(paths.DatabasePath);
    }

    private static string ResolveListeningUrl(WebApplication app)
    {
        var server = app.Services.GetRequiredService<IServer>();
        var addresses = server.Features.Get<IServerAddressesFeature>()?.Addresses;
        var first = addresses?.FirstOrDefault();
        if (string.IsNullOrEmpty(first))
            return "http://127.0.0.1:5000";

        return first.Contains("://", StringComparison.Ordinal)
            ? first
            : $"http://{first}";
    }
}
