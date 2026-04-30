﻿using System;
using System.IO;
using System.Text.Json;

namespace MarsXZMedia;

public sealed class AppSettings
{
    public string VideoPath { get; set; } = "";
    public string MusicPath { get; set; } = "";
    public bool SeparatePaths { get; set; } = true;
    public bool UseDefaultPath { get; set; } = false;
    public bool CreateSubfolders { get; set; } = true;
    public bool DisableOpenFile { get; set; } = false;
    public bool DisableLogs { get; set; } = false;
    public bool LogAutoDeleteInfinite { get; set; } = true;
    public int LogAutoDeleteMaxDays { get; set; } = 30;
    public string LastCustomVideoPath { get; set; } = "";
    public string LastCustomMusicPath { get; set; } = "";
    // Новые поля для внешнего вида
    public string FontChoice { get; set; } = "Default"; // "Default" или "MonoCraft"
    public string SoundTheme { get; set; } = "None"; // "None" или "Minecraft"
}

public static class AppSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        WriteIndented = true
    };
    
    public static void CleanupLegacyData()
    {
        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var legacyRoots = new[]
            {
                Path.Combine(appData, "MarsDownloader"),
                Path.Combine(appData, "MarsDownloaderNew"),
                Path.Combine(appData, "MarsXZ Media")
            };

            foreach (var oldRoot in legacyRoots)
            {
                if (Directory.Exists(oldRoot))
                    Directory.Delete(oldRoot, true);
            }
        }
        catch
        {
        }
    }

    public static string SettingsPath => AppPaths.SettingsPath;
    private static AppSettings CreateDefaultSettings()
    {
        return new AppSettings
        {
            UseDefaultPath = true,
            SeparatePaths = false
        };
    }



    public static AppSettings Load()
    {
        AppPaths.EnsureDataDirectories();
        try
        {
            if (!File.Exists(SettingsPath)) return CreateDefaultSettings();
            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? CreateDefaultSettings();
        }
        catch
        {
            return CreateDefaultSettings();
        }
    }
    public static void Save(AppSettings settings)
    {
        AppPaths.EnsureDataDirectories();
        try
        {
            var dir = Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
        }
    }

    public static void ApplyToMainWindow(AppSettings s)
    {
        if (s.UseDefaultPath && s.SeparatePaths) s.SeparatePaths = false;

        if (!string.IsNullOrWhiteSpace(s.VideoPath))
            MainWindow.VideoPath = s.VideoPath;
        if (!string.IsNullOrWhiteSpace(s.MusicPath))
            MainWindow.MusicPath = s.MusicPath;

        MainWindow.SeparatePaths = s.SeparatePaths;
        MainWindow.UseDefaultPath = s.UseDefaultPath;
        MainWindow.CreateSubfolders = s.CreateSubfolders;
        MainWindow.DisableOpenFile = s.DisableOpenFile;
        MainWindow.DisableLogs = s.DisableLogs;
        MainWindow.LogAutoDeleteInfinite = s.LogAutoDeleteInfinite;
        if (s.LogAutoDeleteMaxDays > 0)
            MainWindow.LogAutoDeleteMaxDays = s.LogAutoDeleteMaxDays;

        if (!string.IsNullOrWhiteSpace(s.LastCustomVideoPath))
            MainWindow.LastCustomVideoPath = s.LastCustomVideoPath;
        if (!string.IsNullOrWhiteSpace(s.LastCustomMusicPath))
            MainWindow.LastCustomMusicPath = s.LastCustomMusicPath;

        if (MainWindow.UseDefaultPath)
        {
            var def = AppPaths.DownloadsRoot;
            if (string.IsNullOrWhiteSpace(MainWindow.VideoPath)) MainWindow.VideoPath = def;
            if (string.IsNullOrWhiteSpace(MainWindow.MusicPath)) MainWindow.MusicPath = def;
        }

        if (!MainWindow.SeparatePaths)
            MainWindow.MusicPath = MainWindow.VideoPath;

        // Применение внешнего вида
        MainWindow.FontChoice = s.FontChoice;
        MainWindow.SoundTheme = s.SoundTheme;
        SoundService.ApplyTheme(s.SoundTheme);
    }

    public static AppSettings FromMainWindow()
    {
        return new AppSettings
        {
            VideoPath = MainWindow.VideoPath,
            MusicPath = MainWindow.MusicPath,
            SeparatePaths = MainWindow.SeparatePaths,
            UseDefaultPath = MainWindow.UseDefaultPath,
            CreateSubfolders = MainWindow.CreateSubfolders,
            DisableOpenFile = MainWindow.DisableOpenFile,
            DisableLogs = MainWindow.DisableLogs,
            LogAutoDeleteInfinite = MainWindow.LogAutoDeleteInfinite,
            LogAutoDeleteMaxDays = MainWindow.LogAutoDeleteMaxDays,
            LastCustomVideoPath = MainWindow.LastCustomVideoPath,
            LastCustomMusicPath = MainWindow.LastCustomMusicPath,
            FontChoice = MainWindow.FontChoice,
            SoundTheme = MainWindow.SoundTheme
        };
    }
}
