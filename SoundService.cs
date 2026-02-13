using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using System;
using System.IO;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace MarsXZMedia;

internal static class SoundService
{
    private static readonly object _lock = new();
    private static MediaPlayer? _clickPlayer;
    private static MediaPlayer? _applyPlayer;
    private static string? _clickResolvedPath;
    private static string? _applyResolvedPath;

    public static void AttachClickSound(Window window)
    {
        if (window == null) return;
        window.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
    }

    public static void PlayClick() => Play(ref _clickPlayer, ref _clickResolvedPath, "click.mp3");
    public static void PlayApply() => Play(ref _applyPlayer, ref _applyResolvedPath, "apply.mp3");

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

    private static void Play(ref MediaPlayer? player, ref string? cachedPath, string fileName)
    {
        if (!OperatingSystem.IsWindows()) return;

        try
        {
            lock (_lock)
            {
                var path = cachedPath;
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    path = ResolveSoundPath(fileName);
                    cachedPath = path;
                }

                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    LogService.Log("W", $"Звук не найден: {fileName}", "Sound");
                    return;
                }

                if (player == null)
                {
                    player = new MediaPlayer
                    {
                        AudioCategory = MediaPlayerAudioCategory.SoundEffects,
                        IsLoopingEnabled = false,
                        Volume = 1.0
                    };
                }

                // Обновляем Source каждый раз, чтобы не залипать на невалидном источнике.
                var uri = new Uri(Path.GetFullPath(path), UriKind.Absolute);
                player.Source = MediaSource.CreateFromUri(uri);

                try { player.Pause(); } catch { }
                try { player.PlaybackSession.Position = TimeSpan.Zero; } catch { }
                player.Play();
            }
        }
        catch (Exception ex)
        {
            LogService.Log("E", $"Ошибка воспроизведения звука {fileName}", "Sound", ex);
        }
    }

    private static string? ResolveSoundPath(string fileName)
    {
        var baseDir = AppContext.BaseDirectory;
        var currentDir = Environment.CurrentDirectory;

        var candidates = new[]
        {
            Path.Combine(baseDir, "Assets", "Sounds", fileName),
            Path.Combine(baseDir, "Sounds", fileName),
            Path.Combine(baseDir, fileName),
            Path.Combine(currentDir, "Assets", "Sounds", fileName),
            Path.Combine(currentDir, "Sounds", fileName),
            Path.Combine(currentDir, fileName)
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                LogService.Log("D", $"Найден звук {fileName}: {candidate}", "Sound");
                return candidate;
            }
        }

        return null;
    }
}
