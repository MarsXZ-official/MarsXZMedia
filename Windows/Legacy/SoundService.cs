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
    private static SoundPlayer? _click;
    private static SoundPlayer? _apply;
    private static bool _initialized;

    public static void AttachClickSound(Window window)
    {
        if (window == null) return;
        EnsureInitialized();
        window.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
    }

    public static void PlayClick()
    {
        EnsureInitialized();
        try { _click?.Stop(); _click?.Play(); } catch { }
    }

    public static void PlayApply()
    {
        EnsureInitialized();
        try { _apply?.Stop(); _apply?.Play(); } catch { }
    }

    private static void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
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
        _initialized = true;

        var clickPath = ResolveSoundPath("click.wav");
        var applyPath = ResolveSoundPath("apply.wav");

        if (File.Exists(clickPath))
        {
            _click = new SoundPlayer(clickPath);
            try { _click.Load(); } catch { try { _click.LoadAsync(); } catch { } }
        }
        else
        {
            LogService.Log("W", $"Звук не найден: {clickPath}", "Sound");
        }

        if (File.Exists(applyPath))
        {
            _apply = new SoundPlayer(applyPath);
            try { _apply.Load(); } catch { try { _apply.LoadAsync(); } catch { } }
        }
        else
        {
            LogService.Log("W", $"Звук не найден: {applyPath}", "Sound");
        }
    }

    private static string ResolveSoundPath(string fileName)
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
                return candidate;
        }

        return candidates[0];
    }
}
