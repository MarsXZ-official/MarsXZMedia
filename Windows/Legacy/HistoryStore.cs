using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MarsXZMedia;

public sealed class HistoryEntry
{
    public DateTime Timestamp { get; set; }
    public string Title { get; set; } = "";
    public string Url { get; set; } = "";
}

public static class HistoryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        WriteIndented = true
    };

    private static readonly string HistoryPath = AppPaths.HistoryPath;

    public static IReadOnlyList<HistoryEntry> LoadAll()
    {
        try
        {
            if (!File.Exists(HistoryPath))
                return Array.Empty<HistoryEntry>();

            var json = File.ReadAllText(HistoryPath);
            var entries = JsonSerializer.Deserialize<List<HistoryEntry>>(json);
            return entries?.OrderByDescending(e => e.Timestamp).ToArray() ?? Array.Empty<HistoryEntry>();
        }
        catch
        {
            return Array.Empty<HistoryEntry>();
        }
    }

    public static void Add(string title, string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;

        try
        {
            var trimmedUrl = url.Trim();
            var trimmedTitle = string.IsNullOrWhiteSpace(title) ? "Без названия" : title.Trim();
            var entries = LoadAll().ToList();
            entries.Insert(0, new HistoryEntry
            {
                Title = trimmedTitle,
                Url = trimmedUrl,
                Timestamp = DateTime.Now
            });

            const int MaxEntries = 200;
            if (entries.Count > MaxEntries)
            {
                entries = entries.Take(MaxEntries).ToList();
            }

            Save(entries);
        }
        catch
        {
        }
    }

    private static void Save(IEnumerable<HistoryEntry> entries)
    {
        try
        {
            var dir = Path.GetDirectoryName(HistoryPath);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(entries, JsonOptions);
            File.WriteAllText(HistoryPath, json);
        }
        catch
        {
        }
    }
}
