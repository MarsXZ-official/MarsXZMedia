using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using System;
using System.Diagnostics;
using System.IO;
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
                var setupWin = new SetupWindow();
                desktop.MainWindow = setupWin;
                setupWin.Show();

                setupWin.Closed += (s, e) =>
                {
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
                        desktop.Shutdown();
                    }
                };

                return;
            }

            var mainWinDirect = new MainWindow();
            desktop.MainWindow = mainWinDirect;
            mainWinDirect.Show();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static bool IsJsRuntimeAvailable()
    {
        string appDir = AppPaths.AppDirectory;
        string qjsLocal = Path.Combine(appDir, "qjs.exe");

        if (File.Exists(qjsLocal))
            return true;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "qjs",
                Arguments = "--help",
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

        return false;
    }
}
