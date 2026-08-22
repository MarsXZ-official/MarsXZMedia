﻿﻿﻿using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Microsoft.Win32;
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
        ApplyThemePalette();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            AppSettingsStore.CleanupLegacyData();
            AppPaths.EnsureDataDirectories();
            AppPaths.MigrateLegacyData();
            AppSettingsStore.ApplyToMainWindow(AppSettingsStore.Load());

            var app = Avalonia.Application.Current;
            if (app != null)
            {
                try
                {
                    string asmName = typeof(App).Assembly.GetName().Name ?? "MarsXZ Media";
                    string safeAsmName = Uri.EscapeDataString(asmName);
                    string fontRegularUri = $"avares://{safeAsmName}/Assets/Fonts/Monocraft.ttf#Monocraft";
                    string fontBoldUri = $"avares://{safeAsmName}/Assets/Fonts/Monocraft-Bold.ttf#Monocraft";

                    var fontRegular = MainWindow.FontChoice == "MonoCraft" 
                        ? new FontFamily(fontRegularUri) 
                        : FontFamily.Default;
                    var fontBold = MainWindow.FontChoice == "MonoCraft" 
                        ? new FontFamily(fontBoldUri) 
                        : FontFamily.Default;

                    app.Resources["AppFont"] = fontRegular;
                    app.Resources["AppFontBold"] = fontBold;
                }
                catch { }
            }

            string appDir = AppPaths.AppDirectory;
            bool toolsOk = File.Exists(Path.Combine(appDir, "yt-dlp.exe")) &&
                           File.Exists(Path.Combine(appDir, "ffmpeg.exe")) &&
                           IsJsRuntimeAvailable();
            bool ytDlpNeedsUpdate = false;
            
            if (toolsOk)
            {
                try
                {
                    string ytDlpPath = Path.Combine(appDir, "yt-dlp.exe");
                    if (!YtDlpUpdateHelper.TryGetLocalVersion(ytDlpPath, out _))
                    {
                        ytDlpNeedsUpdate = true;
                    }
                    else
                    {
                        var updateCheck = YtDlpUpdateHelper.CheckAsync(ytDlpPath, CancellationToken.None)
                        .GetAwaiter().GetResult();
                        ytDlpNeedsUpdate = updateCheck.IsOutdated;
                    }
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

    private static void ApplyThemePalette()
    {
        var app = Avalonia.Application.Current;
        if (app == null) return;

        bool isDark = IsWindowsDarkThemeEnabled();
        app.RequestedThemeVariant = isDark ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light;

        var light = new System.Collections.Generic.Dictionary<string, object>
        {
            ["AppWindowBackground"] = new SolidColorBrush(Color.FromRgb(255, 255, 255)),
            ["AppSurface"] = new SolidColorBrush(Color.FromRgb(255, 255, 255)),
            ["AppSurfaceAlt"] = new SolidColorBrush(Color.FromRgb(240, 240, 240)),
            ["AppTextPrimary"] = new SolidColorBrush(Color.FromRgb(17, 17, 17)),
            ["AppTextSecondary"] = new SolidColorBrush(Color.FromRgb(74, 74, 74)),
            ["AppTextMuted"] = new SolidColorBrush(Color.FromRgb(102, 102, 102)),
            ["AppBorder"] = new SolidColorBrush(Color.FromRgb(217, 217, 217)),
            ["AppInputBackground"] = new SolidColorBrush(Color.FromRgb(243, 243, 243)),
            ["AppInputBorder"] = new SolidColorBrush(Color.FromRgb(185, 185, 185)),
            ["AppActionButtonBackground"] = new SolidColorBrush(Color.FromRgb(217, 217, 217)),
            ["AppActionButtonHover"] = new SolidColorBrush(Color.FromRgb(190, 190, 190)),
            ["AppActionButtonBorder"] = new SolidColorBrush(Color.FromRgb(142, 142, 142)),
            ["AppActionButtonForeground"] = new SolidColorBrush(Color.FromRgb(17, 17, 17)),
            ["AppLinkColor"] = new SolidColorBrush(Color.FromRgb(26, 115, 232)),
            ["AppDivider"] = new SolidColorBrush(Color.FromRgb(220, 220, 220))
        };

        var dark = new System.Collections.Generic.Dictionary<string, object>
        {
            ["AppWindowBackground"] = new SolidColorBrush(Color.FromRgb(18, 18, 18)),
            ["AppSurface"] = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
            ["AppSurfaceAlt"] = new SolidColorBrush(Color.FromRgb(35, 35, 35)),
            ["AppTextPrimary"] = new SolidColorBrush(Color.FromRgb(255, 255, 255)),
            ["AppTextSecondary"] = new SolidColorBrush(Color.FromRgb(170, 170, 170)),
            ["AppTextMuted"] = new SolidColorBrush(Color.FromRgb(180, 180, 180)),
            ["AppBorder"] = new SolidColorBrush(Color.FromRgb(55, 55, 55)),
            ["AppInputBackground"] = new SolidColorBrush(Color.FromRgb(31, 31, 31)),
            ["AppInputBorder"] = new SolidColorBrush(Color.FromRgb(90, 90, 90)),
            ["AppActionButtonBackground"] = new SolidColorBrush(Color.FromRgb(53, 53, 53)),
            ["AppActionButtonHover"] = new SolidColorBrush(Color.FromRgb(75, 75, 75)),
            ["AppActionButtonBorder"] = new SolidColorBrush(Color.FromRgb(108, 108, 108)),
            ["AppActionButtonForeground"] = new SolidColorBrush(Color.FromRgb(255, 255, 255)),
            ["AppLinkColor"] = new SolidColorBrush(Color.FromRgb(100, 181, 246)),
            ["AppDivider"] = new SolidColorBrush(Color.FromRgb(51, 51, 51))
        };

        var palette = isDark ? dark : light;
        foreach (var item in palette)
        {
            app.Resources[item.Key] = item.Value;
        }
    }

    private static bool IsWindowsDarkThemeEnabled()
    {
        if (!OperatingSystem.IsWindows())
            return false;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key != null)
            {
                var value = key.GetValue("AppsUseLightTheme");
                if (value is int appsUseLightTheme)
                    return appsUseLightTheme == 0;
            }
        }
        catch { }

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key != null)
            {
                var value = key.GetValue("AppsUseLightTheme");
                if (value is int appsUseLightTheme)
                    return appsUseLightTheme == 0;
            }
        }
        catch { }

        return false;
    }

    private static bool IsJsRuntimeAvailable()
    {
        string appDir = AppPaths.AppDirectory;
        string nodeLocal = Path.Combine(appDir, "node.exe");
        string denoLocal = Path.Combine(appDir, "deno.exe");
        if (File.Exists(nodeLocal) || File.Exists(denoLocal))
            return true;
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