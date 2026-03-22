using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Windows.UI.Notifications
{
    public sealed class NotificationData
    {
        public uint SequenceNumber { get; set; }
        public IDictionary<string, string> Values { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class ToastNotification
    {
        public string? Tag { get; set; }
        public string? Group { get; set; }
        public DateTimeOffset? ExpirationTime { get; set; }
        public NotificationData? Data { get; set; }
    }
}

namespace CommunityToolkit.WinUI.Notifications
{
    using Windows.UI.Notifications;

    public sealed class BindableString
    {
        public BindableString(string key) => Key = key;
        public string Key { get; }
    }

    public sealed class BindableProgressBarValue
    {
        public BindableProgressBarValue(string key) => Key = key;
        public string Key { get; }
    }

    public sealed class AdaptiveProgressBar
    {
        public object? Value { get; set; }
        public object? Title { get; set; }
        public object? ValueStringOverride { get; set; }
        public object? Status { get; set; }
    }

    internal sealed class ToastTemplate
    {
        public List<object> Texts { get; } = new();
        public AdaptiveProgressBar? ProgressBar { get; set; }
        public string? HeaderTitle { get; set; }
    }

    internal sealed class ToastState
    {
        public ToastTemplate Template { get; }
        public Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
        public uint SequenceNumber { get; set; }
        public DateTime LastShownUtc { get; set; } = DateTime.MinValue;
        public int LastShownPercentBucket { get; set; } = -1;

        public ToastState(ToastTemplate template)
        {
            Template = template;
        }
    }

    internal static class LegacyBalloonNotificationService
    {
        private static readonly object Sync = new();
        private static readonly Dictionary<string, ToastState> States = new(StringComparer.OrdinalIgnoreCase);
        private static NotifyIcon? _notifyIcon;

        private static string MakeKey(string? tag, string? group)
            => $"{group ?? string.Empty}::{tag ?? string.Empty}";

        public static void RegisterTemplate(ToastTemplate template, ToastNotification toast)
        {
            lock (Sync)
            {
                var key = MakeKey(toast.Tag, toast.Group);
                var state = new ToastState(template);
                if (toast.Data != null)
                {
                    MergeValues(state, toast.Data);
                }

                States[key] = state;
                ShowState(toast.Tag, toast.Group, force: true);
            }
        }

        public static void Update(NotificationData data, string tag, string group)
        {
            lock (Sync)
            {
                var key = MakeKey(tag, group);
                if (!States.TryGetValue(key, out var state))
                {
                    return;
                }

                if (data.SequenceNumber > 0 && data.SequenceNumber < state.SequenceNumber)
                {
                    return;
                }

                MergeValues(state, data);
                state.SequenceNumber = data.SequenceNumber;
                ShowState(tag, group, force: false);
            }
        }

        public static void Remove(string tag, string group)
        {
            lock (Sync)
            {
                States.Remove(MakeKey(tag, group));
            }
        }

        public static void Clear()
        {
            lock (Sync)
            {
                States.Clear();
            }
        }

        public static void ShowPlain(string title, string message)
        {
            lock (Sync)
            {
                ShowBalloon(title, message, ToolTipIcon.Info, 4000);
            }
        }

        private static void MergeValues(ToastState state, NotificationData data)
        {
            foreach (var pair in data.Values)
            {
                state.Values[pair.Key] = pair.Value;
            }
        }

        private static void ShowState(string? tag, string? group, bool force)
        {
            var key = MakeKey(tag, group);
            if (!States.TryGetValue(key, out var state))
            {
                return;
            }

            var (title, message, percentBucket) = Render(state);
            if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            var now = DateTime.UtcNow;
            var enoughTimePassed = (now - state.LastShownUtc).TotalSeconds >= 8;
            var bucketChanged = percentBucket >= 0 && percentBucket != state.LastShownPercentBucket;

            if (!force && !enoughTimePassed && !bucketChanged)
            {
                return;
            }

            state.LastShownUtc = now;
            if (percentBucket >= 0)
            {
                state.LastShownPercentBucket = percentBucket;
            }

            ShowBalloon(title, message, ToolTipIcon.Info, 5000);
        }

        private static (string title, string message, int percentBucket) Render(ToastState state)
        {
            string Resolve(object? value)
            {
                return value switch
                {
                    null => string.Empty,
                    string s => s,
                    BindableString b => state.Values.TryGetValue(b.Key, out var resolved) ? resolved : string.Empty,
                    BindableProgressBarValue p => state.Values.TryGetValue(p.Key, out var progress) ? progress : string.Empty,
                    _ => value.ToString() ?? string.Empty
                };
            }

            var lines = new List<string>();

            if (!string.IsNullOrWhiteSpace(state.Template.HeaderTitle))
            {
                lines.Add(state.Template.HeaderTitle!);
            }

            foreach (var text in state.Template.Texts)
            {
                var resolved = Resolve(text).Trim();
                if (!string.IsNullOrWhiteSpace(resolved))
                {
                    lines.Add(resolved);
                }
            }

            int percentBucket = -1;
            if (state.Template.ProgressBar != null)
            {
                var pb = state.Template.ProgressBar;
                var progressTitle = Resolve(pb.Title).Trim();
                var valueText = Resolve(pb.ValueStringOverride).Trim();
                var status = Resolve(pb.Status).Trim();
                var value = Resolve(pb.Value).Trim();

                if (!string.IsNullOrWhiteSpace(progressTitle)) lines.Add(progressTitle);
                if (!string.IsNullOrWhiteSpace(valueText)) lines.Add(valueText);
                if (!string.IsNullOrWhiteSpace(status)) lines.Add(status);

                if (double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var progress))
                {
                    var percent = (int)Math.Round(progress * 100.0);
                    percentBucket = Math.Clamp(percent / 10, 0, 10);
                    lines.Add($"Прогресс: {percent}%");
                }
            }

            string title;
            string message;

            if (lines.Count == 0)
            {
                title = "MarsXZ Media";
                message = string.Empty;
            }
            else if (lines.Count == 1)
            {
                title = lines[0];
                message = string.Empty;
            }
            else
            {
                title = lines[0];
                message = string.Join(Environment.NewLine, lines.Skip(1).Take(6));
            }

            return (Trim(title, 63), Trim(message, 255), percentBucket);
        }

        private static string Trim(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            {
                return value;
            }

            return value.Substring(0, Math.Max(0, maxLength - 1)) + "…";
        }

        private static NotifyIcon EnsureNotifyIcon()
        {
            if (_notifyIcon != null)
            {
                return _notifyIcon;
            }

            Icon icon;
            try
            {
                var exePath = Process.GetCurrentProcess().MainModule?.FileName;
                icon = !string.IsNullOrWhiteSpace(exePath)
                    ? Icon.ExtractAssociatedIcon(exePath!) ?? SystemIcons.Application
                    : SystemIcons.Application;
            }
            catch
            {
                icon = SystemIcons.Application;
            }

            _notifyIcon = new NotifyIcon
            {
                Icon = icon,
                Visible = true,
                Text = "MarsXZ Media"
            };

            return _notifyIcon;
        }

        private static void ShowBalloon(string title, string message, ToolTipIcon icon, int timeout)
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            try
            {
                var notifyIcon = EnsureNotifyIcon();
                notifyIcon.BalloonTipIcon = icon;
                notifyIcon.BalloonTipTitle = string.IsNullOrWhiteSpace(title) ? "MarsXZ Media" : title;
                notifyIcon.BalloonTipText = string.IsNullOrWhiteSpace(message) ? " " : message;
                notifyIcon.ShowBalloonTip(timeout);
            }
            catch
            {
                // Не валим приложение из-за недоступных уведомлений.
            }
        }
    }

    public sealed class ToastContentBuilder
    {
        private readonly ToastTemplate _template = new();

        public ToastContentBuilder AddText(string text)
        {
            _template.Texts.Add(text);
            return this;
        }

        public ToastContentBuilder AddText(BindableString text)
        {
            _template.Texts.Add(text);
            return this;
        }

        public ToastContentBuilder AddVisualChild(AdaptiveProgressBar progressBar)
        {
            _template.ProgressBar = progressBar;
            return this;
        }

        public ToastContentBuilder AddArgument(string key, string value) => this;
        public ToastContentBuilder AddAudio(Uri uri) => this;

        public ToastContentBuilder AddHeader(string id, string title, string arguments)
        {
            _template.HeaderTitle = title;
            return this;
        }

        public void Show()
        {
            var title = _template.HeaderTitle;
            var textLines = _template.Texts.OfType<string>().Where(s => !string.IsNullOrWhiteSpace(s)).ToList();

            if (string.IsNullOrWhiteSpace(title) && textLines.Count > 0)
            {
                title = textLines[0];
                textLines.RemoveAt(0);
            }

            var message = string.Join(Environment.NewLine, textLines.Take(5));
            LegacyBalloonNotificationService.ShowPlain(title ?? "MarsXZ Media", message);
        }

        public void Show(Action<ToastNotification> configureToast)
        {
            var toast = new ToastNotification();
            configureToast?.Invoke(toast);
            LegacyBalloonNotificationService.RegisterTemplate(_template, toast);
        }
    }

    public sealed class ToastNotifierCompat
    {
        public void Update(NotificationData data, string tag, string group)
            => LegacyBalloonNotificationService.Update(data, tag, group);
    }

    public sealed class ToastHistoryCompat
    {
        public void Remove(string tag, string group)
            => LegacyBalloonNotificationService.Remove(tag, group);

        public void Clear()
            => LegacyBalloonNotificationService.Clear();
    }

    public static class ToastNotificationManagerCompat
    {
        public static ToastHistoryCompat History { get; } = new ToastHistoryCompat();
        public static ToastNotifierCompat CreateToastNotifier() => new ToastNotifierCompat();
    }
}
