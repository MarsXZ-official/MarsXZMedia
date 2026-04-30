using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using System;
using System.Globalization;
using System.Linq;

namespace MarsXZMedia;

public partial class HistoryView : UserControl
{
    public event Action<HistoryEntry>? EntrySelected;
    public HistoryView()
    {
        InitializeComponent();
        this.AttachedToVisualTree += (s, e) => RefreshView();
    }

    public void RefreshView()
    {
        BuildView();
    }

    private void BuildView()
    {
        GroupsPanel.Children.Clear();
        var entries = HistoryStore.LoadAll();
        if (entries.Count == 0)
        {
            GroupsPanel.Children.Add(new TextBlock
            {
                Text = "История пуста",
                FontSize = 16,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 32, 0, 0)
            });
            return;
        }

        var sections = entries.GroupBy(e => e.Timestamp.Date)
                              .OrderByDescending(g => g.Key);

        foreach (var section in sections)
        {
            GroupsPanel.Children.Add(new TextBlock
            {
                Text = FormatSectionTitle(section.Key),
                FontSize = 18,
                FontWeight = FontWeight.Bold,
                Margin = new Thickness(0, 8, 0, 0)
            });

            var byUrl = section.GroupBy(e => e.Url)
                               .OrderByDescending(g => g.Max(x => x.Timestamp));

            foreach (var group in byUrl)
            {
                var items = group.OrderByDescending(x => x.Timestamp).ToList();
                if (items.Count == 1)
                {
                    GroupsPanel.Children.Add(CreateEntryButton(items[0], indicator: "", compact: false));
                }
                else
                {
                    GroupsPanel.Children.Add(CreateGroupPanel(items));
                }
            }
        }
    }

    private Control CreateGroupPanel(System.Collections.Generic.List<HistoryEntry> items)
    {
        var latest = items[0];
        var indicatorText = new TextBlock
        {
            Text = "^",
            FontSize = 16,
            Foreground = Brushes.Gray,
            Width = 18,
            VerticalAlignment = VerticalAlignment.Center
        };

        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6
        };

        header.Children.Add(indicatorText);

        header.Children.Add(new TextBlock
        {
            Text = latest.Title,
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 420,
            VerticalAlignment = VerticalAlignment.Center
        });

        header.Children.Add(new TextBlock
        {
            Text = $"[{latest.Timestamp.ToLocalTime():HH:mm}]",
            FontSize = 14,
            Foreground = Brushes.Gray,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0)
        });

        var headerButton = new Button
        {
            Content = header,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(10),
            Tag = latest
        };

        var childrenPanel = new StackPanel
        {
            Spacing = 6,
            Margin = new Thickness(24, 6, 0, 8),
            IsVisible = false
        };

        foreach (var item in items)
        {
            childrenPanel.Children.Add(CreateEntryButton(item, indicator: "", compact: true));
        }

        headerButton.Click += (_, _) =>
        {
            childrenPanel.IsVisible = !childrenPanel.IsVisible;
            indicatorText.Text = childrenPanel.IsVisible ? "˅" : "^";
        };

        var groupPanel = new StackPanel { Spacing = 4 };
        groupPanel.Children.Add(headerButton);
        groupPanel.Children.Add(childrenPanel);
        return groupPanel;
    }

    private Button CreateEntryButton(HistoryEntry entry, string indicator, bool compact)
    {
        var timeText = entry.Timestamp.ToLocalTime().ToString("HH:mm");

        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6
        };

        header.Children.Add(new TextBlock
        {
            Text = indicator,
            FontSize = compact ? 14 : 16,
            Foreground = Brushes.Gray,
            Width = 18,
            VerticalAlignment = VerticalAlignment.Center
        });

        header.Children.Add(new TextBlock
        {
            Text = entry.Title,
            FontSize = compact ? 14 : 16,
            FontWeight = FontWeight.Normal,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 420,
            VerticalAlignment = VerticalAlignment.Center
        });

        header.Children.Add(new TextBlock
        {
            Text = $"[{timeText}]",
            FontSize = compact ? 12 : 14,
            Foreground = Brushes.Gray,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0)
        });

        var button = new Button
        {
            Content = header,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(10),
            Tag = entry
        };

        button.Click += (_, _) => EntrySelected?.Invoke(entry);
        return button;
    }

    private string FormatSectionTitle(DateTime date)
    {
        var today = DateTime.Today;
        if (date == today) return "Сегодня";
        if (date == today.AddDays(-1)) return "Вчера";
        return date.ToString("dd MMM yyyy", CultureInfo.CurrentCulture);
    }
}

