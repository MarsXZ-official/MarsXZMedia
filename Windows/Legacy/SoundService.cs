using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using System;
using System.IO;
using System.Media;

namespace MarsXZMedia;

internal static class SoundService
{
    public static string CurrentTheme { get; private set; } = "None";

    public static void ApplyTheme(string theme)
    {
        CurrentTheme = theme;
        // Можно добавить логику смены путей к звукам, если потребуется
    }

    private static readonly object _lock = new();
    private static bool _initialized;
    private static SoundPlayer? _clickPlayer;
    private static SoundPlayer? _applyPlayer;
    private static string? _clickResolvedPath;
    private static string? _applyResolvedPath;

    public static void AttachClickSound(Window window)
    {
        if (window == null) return;
        EnsureInitialized();
        window.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
    }


    public static void PlayClick()
    {
        if (CurrentTheme == "None") return;
        EnsureInitialized();
        PlayMedia(ref _clickPlayer, ref _clickResolvedPath, "click");
    }


    public static void PlayApply()
    {
        if (CurrentTheme == "None") return;
        EnsureInitialized();
        PlayMedia(ref _applyPlayer, ref _applyResolvedPath, "apply");
    }

    private static void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!OperatingSystem.IsWindows()) return;
        if (sender is not Control root) return;

        var props = e.GetCurrentPoint(root).Properties;
        if (!props.IsLeftButtonPressed) return;

        if (e.Source is not Control source) return;
        if (!IsInteractive(source)) return;

        PlayClick();
    }

    private static bool IsInteractive(Control source)
    {
        for (Control? c = source; c != null; c = c.Parent as Control)
        {
            if (!c.IsEnabled) return false;

            if (c is Button ||
                c is CheckBox ||
                c is RadioButton ||
                c is ToggleButton ||
                c is ToggleSwitch ||
                c is ListBoxItem ||
                c is MenuItem)
            {
                return true;
            }
        }

        return false;
    }

    private static void EnsureInitialized()
    {
        if (_initialized) return;
        lock (_lock)
        {
            if (_initialized) return;
            _initialized = true;

            _clickResolvedPath = ResolveSoundPath("click");
            _applyResolvedPath = ResolveSoundPath("apply");

            WarmupMedia(ref _clickPlayer, _clickResolvedPath);
            WarmupMedia(ref _applyPlayer, _applyResolvedPath);
        }
    }

    private static void WarmupMedia(ref SoundPlayer? player, string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return;

        try
        {
            player = new SoundPlayer(path);
            player.LoadAsync();
        }
        catch (Exception ex)
        {
            LogService.Log("W", $"Не удалось подготовить звук: {path}", "Sound", ex);
        }
    }

    private static void PlayMedia(ref SoundPlayer? player, ref string? cachedPath, string baseName)
    {
        if (!OperatingSystem.IsWindows()) return;

        try
        {
            lock (_lock)
            {
                var path = cachedPath;
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    path = ResolveSoundPath(baseName);
                    cachedPath = path;
                }

                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    LogService.Log("W", $"Звук не найден: {baseName}", "Sound");
                    return;
                }

                if (player == null)
                    WarmupMedia(ref player, path);
                if (player == null)
                    return;

                player.Play();
            }
        }
        catch (Exception ex)
        {
            LogService.Log("E", $"Ошибка воспроизведения звука {baseName}", "Sound", ex);
        }
    }

    private static string? ResolveSoundPath(string baseName)
    {
        var baseDir = AppContext.BaseDirectory;
        var currentDir = Environment.CurrentDirectory;

        var candidates = new[]
        {
            Path.Combine(baseDir, "Assets", "Sounds", baseName + ".wav"),
            Path.Combine(baseDir, "Assets", "Sounds", baseName + ".mp3"),
            Path.Combine(baseDir, "Sounds", baseName + ".wav"),
            Path.Combine(baseDir, "Sounds", baseName + ".mp3"),
            Path.Combine(baseDir, baseName + ".wav"),
            Path.Combine(baseDir, baseName + ".mp3"),
            Path.Combine(currentDir, "Assets", "Sounds", baseName + ".wav"),
            Path.Combine(currentDir, "Assets", "Sounds", baseName + ".mp3"),
            Path.Combine(currentDir, "Sounds", baseName + ".wav"),
            Path.Combine(currentDir, "Sounds", baseName + ".mp3"),
            Path.Combine(currentDir, baseName + ".wav"),
            Path.Combine(currentDir, baseName + ".mp3")
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }
}
