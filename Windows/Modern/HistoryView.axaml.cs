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

    private static IBrush ThemeBrush(string key, IBrush fallback)
    {
        return Avalonia.Application.Current?.Resources[key] as IBrush ?? fallback;
    }

    private static IBrush HistoryBorderBrush => Brushes.Black;

    public HistoryView()
    {
        InitializeComponent();
        this.AttachedToVisualTree += (s, e) => RefreshView();
    }

    public void RefreshView()
    {
        BuildView();
    }

    public void ApplyVisualMode()
    {
        var radius = MainWindow.SquareInterface ? 0 : 22;
        foreach (var control in GroupsPanel.Children)
        {
            ApplyRadius(control, radius);
        }
    }

    private static void ApplyRadius(Control control, double radius)
    {
        if (control is Border border)
        {
            border.CornerRadius = new CornerRadius(radius);
            if (border.Child != null)
                ApplyRadius(border.Child, 0);
            return;
        }

        if (control is Button button)
            button.CornerRadius = new CornerRadius(radius);

        if (control is Panel panel)
        {
            foreach (var child in panel.Children)
                ApplyRadius(child, radius);
        }
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
                    GroupsPanel.Children.Add(CreateEntryBlock(items[0]));
                }
                else
                {
                    GroupsPanel.Children.Add(CreateGroupPanel(items));
                }
            }
        }

        ApplyVisualMode();
    }

    private Control CreateGroupPanel(System.Collections.Generic.List<HistoryEntry> items)
    {
        var latest = items[0];
        var primaryText = ThemeBrush("AppTextPrimary", Brushes.Black);
        var secondaryText = ThemeBrush("AppTextSecondary", Brushes.Gray);
        var surface = ThemeBrush("AppSurface", Brushes.White);
        var indicatorText = new TextBlock
        {
            Text = "^",
            FontSize = 16,
            Foreground = secondaryText,
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
            Foreground = secondaryText,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0)
        });

        var headerButton = new Button
        {
            Content = header,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(10),
            Tag = latest,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            Foreground = primaryText
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

        var groupContent = new StackPanel { Spacing = 0 };
        groupContent.Children.Add(headerButton);
        groupContent.Children.Add(childrenPanel);

        return new Border
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = surface,
            BorderBrush = HistoryBorderBrush,
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(0),
            Child = groupContent
        };
    }

    private Button CreateEntryButton(HistoryEntry entry, string indicator, bool compact)
    {
        var timeText = entry.Timestamp.ToLocalTime().ToString("HH:mm");
        var primaryText = ThemeBrush("AppTextPrimary", Brushes.Black);
        var secondaryText = ThemeBrush("AppTextSecondary", Brushes.Gray);

        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6
        };

        header.Children.Add(new TextBlock
        {
            Text = indicator,
            FontSize = compact ? 14 : 16,
            Foreground = secondaryText,
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
            Foreground = secondaryText,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0)
        });

        var button = new Button
        {
            Content = header,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(10),
            Tag = entry,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            Foreground = primaryText
        };

        button.Click += (_, _) => EntrySelected?.Invoke(entry);
        return button;
    }

    private Border CreateEntryBlock(HistoryEntry entry)
    {
        return new Border
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = ThemeBrush("AppSurface", Brushes.White),
            BorderBrush = HistoryBorderBrush,
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(MainWindow.SquareInterface ? 0 : 22),
            Child = CreateEntryButton(entry, indicator: "", compact: false)
        };
    }

    private string FormatSectionTitle(DateTime date)
    {
        var today = DateTime.Today;
        if (date == today) return "Сегодня";
        if (date == today.AddDays(-1)) return "Вчера";
        return date.ToString("dd MMM yyyy", CultureInfo.CurrentCulture);
    }
}

