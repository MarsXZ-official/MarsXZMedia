using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using CommunityToolkit.WinUI.Notifications;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace MarsXZMedia;

public partial class AboutWindow : Window
{
    private const string SupportEmail = "marsxz8656@gmail.com";

    public AboutWindow()
    {
        InitializeComponent();
        SoundService.AttachClickSound(this);
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void OpenSupportEmail(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;

        var gmail = BuildGmailComposeUri();
        if (TryOpenBrowser(gmail))
        {
            SetEmailStatus("Открываю браузер…");
            return;
        }

        await CopyEmailToClipboardAsync();
        SetEmailStatus("Адрес скопирован в буфер обмена.", isError: true);
        ShowSystemToast(
            "Адрес электронной почты скопирован буфер обмена",
            "Возможно браузер не установлен или не поддерживает открытие ссылок");
    }

    private static string BuildGmailComposeUri()
    {
        return $"https://mail.google.com/mail/?view=cm&fs=1&to={Uri.EscapeDataString(SupportEmail)}";
    }

    private static bool TryOpenBrowser(string uri)
    {
        if (TryOpenShell(uri)) return true;

        if (OperatingSystem.IsWindows())
        {
            if (TryOpenWithKnownBrowsers(uri)) return true;
        }

        return false;
    }

    private static bool TryOpenShell(string uri)
    {
        try
        {
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryOpenWithKnownBrowsers(string uri)
    {
        try
        {
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

            var candidates = new[]
            {
                Path.Combine(programFilesX86, "Microsoft", "Edge", "Application", "msedge.exe"),
                Path.Combine(programFiles, "Microsoft", "Edge", "Application", "msedge.exe"),
                Path.Combine(programFilesX86, "Google", "Chrome", "Application", "chrome.exe"),
                Path.Combine(programFiles, "Google", "Chrome", "Application", "chrome.exe"),
                Path.Combine(programFiles, "Mozilla Firefox", "firefox.exe"),
                Path.Combine(programFilesX86, "Mozilla Firefox", "firefox.exe")
            };

            foreach (var exe in candidates)
            {
                if (!File.Exists(exe)) continue;
                try
                {
                    var startInfo = new ProcessStartInfo(exe)
                    {
                        UseShellExecute = false
                    };
                    startInfo.ArgumentList.Add(uri);
                    Process.Start(startInfo);
                    return true;
                }
                catch
                {
                }
            }
        }
        catch
        {
        }

        return false;
    }

    private async Task CopyEmailToClipboardAsync()
    {
        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard != null)
            {
                await clipboard.SetTextAsync(SupportEmail);
            }
        }
        catch
        {
        }
    }

    private void ShowSystemToast(string title, string message)
    {
        if (!OperatingSystem.IsWindows()) return;

        try
        {
            new ToastContentBuilder()
                .AddText(title)
                .AddText(message)
                .Show();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Ошибка системного уведомления: {ex.Message}");
        }
    }

    private void SetEmailStatus(string text, bool isError = false)
    {
        if (EmailStatus == null) return;
        EmailStatus.Text = text;
        EmailStatus.Foreground = isError ? Brushes.IndianRed : Brushes.Gray;
        EmailStatus.IsVisible = true;
    }
}

