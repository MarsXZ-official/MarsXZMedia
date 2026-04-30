using System;
using System.IO;

namespace MarsXZMedia;

public static class AppPaths
{
    public static readonly string AppDirectory = AppContext.BaseDirectory.TrimEnd('\\', '/');
    public static readonly string DataRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MarsXZMedia");
    public static readonly string LogsDirectory = Path.Combine(DataRoot, "Logs");
    public static readonly string DownloadsRoot = Path.Combine(DataRoot, "Downloads");
    public static readonly string SettingsPath = Path.Combine(DataRoot, "settings.json");
    public static readonly string HistoryPath = Path.Combine(DataRoot, "history.json");

    public static void EnsureDataDirectories()
    {
        EnsureDirectory(DataRoot);
        EnsureDirectory(LogsDirectory);
        EnsureDirectory(DownloadsRoot);
    }

    public static void MigrateLegacyData()
    {
        try
        {
            EnsureDataDirectories();

            string oldSettings = Path.Combine(AppDirectory, "settings.json");
            if (File.Exists(oldSettings) && !File.Exists(SettingsPath))
                File.Copy(oldSettings, SettingsPath, true);

            string oldHistory = Path.Combine(AppDirectory, "history.json");
            if (File.Exists(oldHistory) && !File.Exists(HistoryPath))
                File.Copy(oldHistory, HistoryPath, true);

            string oldCombined = Path.Combine(AppDirectory, "combined_app.log");
            string newCombined = Path.Combine(LogsDirectory, "combined_app.log");
            if (File.Exists(oldCombined) && !File.Exists(newCombined))
                File.Copy(oldCombined, newCombined, true);

            string oldLogsDir = Path.Combine(AppDirectory, "Logs");
            if (Directory.Exists(oldLogsDir))
            {
                foreach (var f in Directory.GetFiles(oldLogsDir, "*.log"))
                {
                    var dest = Path.Combine(LogsDirectory, Path.GetFileName(f));
                    if (!File.Exists(dest)) File.Copy(f, dest, true);
                }
            }

            foreach (var f in Directory.GetFiles(AppDirectory, "*.log"))
            {
                var dest = Path.Combine(LogsDirectory, Path.GetFileName(f));
                if (!File.Exists(dest)) File.Copy(f, dest, true);
            }
        }
        catch
        {
        }
    }

    private static void EnsureDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        if (!Directory.Exists(path))
        {
            try { Directory.CreateDirectory(path); } catch { }
        }
    }
}
