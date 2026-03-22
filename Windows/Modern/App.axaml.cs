using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using System;
using System.IO;
using System.Diagnostics;
using System.Threading;

namespace MarsXZMedia;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            AppSettingsStore.CleanupLegacyData();
            AppPaths.EnsureDataDirectories();
            AppPaths.MigrateLegacyData();
            AppSettingsStore.ApplyToMainWindow(AppSettingsStore.Load());

            string appDir = AppPaths.AppDirectory;

            // Проверяем наличие критических файлов
            bool toolsOk = File.Exists(Path.Combine(appDir, "yt-dlp.exe")) &&
                           File.Exists(Path.Combine(appDir, "ffmpeg.exe")) &&
                           IsJsRuntimeAvailable();

            bool ytDlpNeedsUpdate = false;
            if (toolsOk)
            {
                try
                {
                    var updateCheck = YtDlpUpdateHelper.CheckAsync(Path.Combine(appDir, "yt-dlp.exe"), CancellationToken.None)
                        .GetAwaiter().GetResult();
                    ytDlpNeedsUpdate = updateCheck.IsOutdated;
                }
                catch
                {
                    ytDlpNeedsUpdate = false;
                }
            }

            if (!toolsOk || ytDlpNeedsUpdate)
            {
                // НЕ показываем ошибку — сразу открываем SetupWindow
                var setupWin = new SetupWindow();
                desktop.MainWindow = setupWin;
                setupWin.Show();

                // После закрытия SetupWindow — проверяем снова
                setupWin.Closed += (s, e) =>
                {
                    // Повторная проверка
                    bool nowOk = File.Exists(Path.Combine(appDir, "yt-dlp.exe")) &&
                                 File.Exists(Path.Combine(appDir, "ffmpeg.exe")) &&
                                 IsJsRuntimeAvailable();

                    if (nowOk)
                    {
                        var mainWin = new MainWindow();
                        desktop.MainWindow = mainWin;
                        mainWin.Show();
                    }
                    else
                    {
                        // Если пользователь закрыл SetupWindow без установки — закрываем приложение
                        desktop.Shutdown();
                    }
                };

                return;
            }

            // Всё на месте — сразу главное окно
            var mainWinDirect = new MainWindow();
            desktop.MainWindow = mainWinDirect;
            mainWinDirect.Show();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static bool IsJsRuntimeAvailable()
    {
        string appDir = AppPaths.AppDirectory;

        string nodeLocal = Path.Combine(appDir, "node.exe");
        string denoLocal = Path.Combine(appDir, "deno.exe");

        if (File.Exists(nodeLocal) || File.Exists(denoLocal))
            return true;

        // Проверка в PATH (fallback)
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "node",
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p != null)
            {
                p.WaitForExit(1200);
                if (p.ExitCode == 0) return true;
            }
        }
        catch { }

        try
        {
            var psiDeno = new ProcessStartInfo
            {
                FileName = "deno",
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var pDeno = Process.Start(psiDeno);
            if (pDeno != null)
            {
                pDeno.WaitForExit(1200);
                if (pDeno.ExitCode == 0) return true;
            }
        }
        catch { }

        return false;
    }
}