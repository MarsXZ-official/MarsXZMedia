using System;
using System.IO;
using System.Collections.Generic;

namespace MarsXZMedia
{
    public static class SharedLogService
    {
        // ИЗМЕНЕНО: Используем app directory для логов (доступно на запись в MSIX)
        // Лог лежит прямо рядом с exe
    private static readonly string LogPath = Path.Combine(AppPaths.LogsDirectory, "combined_app.log");

    private static readonly object _lock = new object();
            
        static SharedLogService()
    {
        try
        {
            if (MainWindow.DisableLogs) return;

            // Больше не создаём папку Logs — файл просто пишется в корень
            try { MergeKnownLogs(); } catch { }
        }
        catch { }
    }

        // Ищет в папке Logs известные файлы и добавляет их в объединённый лог (если ещё не добавлены)
        public static void MergeKnownLogs()
        {
            try
            {
                string dir = Path.GetDirectoryName(LogPath)!;
                var patterns = new Dictionary<string, string[]>
                {
                    { "yt-dlp", new[] { "yt-dlp-formats_*.log", "yt-dlp_run_*.log", "yt-dlp_bestaudio_*.log" } },
                    { "ffmpeg", new[] { "ffmpeg_version_*.log", "ffmpeg_merge_*.log", "ffmpeg_reenc_*.log", "ffprobe_*.log", "ffprobe_codec_*.log" } }
                };

                foreach (var kv in patterns)
                {
                    var component = kv.Key;
                    foreach (var pat in kv.Value)
                    {
                        var files = Directory.GetFiles(dir, pat);
                        foreach (var f in files)
                        {
                            try
                            {
                                var fileName = Path.GetFileName(f);
                                var combinedText = File.ReadAllText(LogPath);
                                if (combinedText.Contains(fileName)) continue;

                                AppendFileSection(f, component, fileName);
                            }
                            catch { }
                        }
                    }
                }
            }
            catch { }
        }

        public static string CombinedLogPath => LogPath;

        public static void WriteLine(string level, string message, string windowName, Exception? ex = null, string? component = null)
        {
            // Если логи отключены — ничего не пишем
            if (MainWindow.DisableLogs) return;

            lock (_lock)
            {
                try
                {
                    string time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                    // Форматируем строку с опциональной меткой компонента (например, yt-dlp, ffmpeg)
                    string comp = string.IsNullOrWhiteSpace(component) ? "" : $" [{component}]";
                    string line = $"[{time}] [{windowName}]{comp} [{level.ToUpper()}] {message}";
                    
                    if (ex != null)
                    {
                        line += $"\n{new string('!', 20)}\n[ERROR]: {ex}\n{new string('!', 20)}";
                    }

                    File.AppendAllText(LogPath, line + Environment.NewLine);
                }
                catch { }
            }
        }

        public static void AppendFileSection(string filePath, string component, string heading)
        {
            // Если логи отключены — ничего не делаем
            if (MainWindow.DisableLogs) return;

            lock (_lock)
            {
                try
                {
                    if (!File.Exists(filePath)) return;
                    string time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                    var header = $"[{time}] [Merged] [{component}] [I] === {heading} ({Path.GetFileName(filePath)}) START ===\n";
                    var footer = $"\n[{time}] [Merged] [{component}] [I] === {heading} ({Path.GetFileName(filePath)}) END ===\n";
                    File.AppendAllText(LogPath, header);
                    // Считываем поблочно, чтобы не перегружать память
                    using (var reader = new StreamReader(filePath))
                    {
                        string? line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            // Префиксируем каждую строку меткой компонента и уровнем для единообразия
                            string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                            var prefixed = $"[{now}] [Merged] [{component}] [D] {line}";
                            File.AppendAllText(LogPath, prefixed + Environment.NewLine);
                        }
                    }
                    File.AppendAllText(LogPath, footer);
                }
                catch { }
            }
        }

        // Добавляет произвольный текст как секцию в объединённый лог без создания промежуточного файла
        public static void AppendTextSection(string text, string component, string heading)
        {
            // Если логи отключены — ничего не делаем
            if (MainWindow.DisableLogs) return;

            lock (_lock)
            {
                try
                {
                    string time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                    var header = $"[{time}] [Merged] [{component}] [I] === {heading} START ===\n";
                    var footer = $"\n[{time}] [Merged] [{component}] [I] === {heading} END ===\n";
                    File.AppendAllText(LogPath, header);

                    using (var reader = new StringReader(text))
                    {
                        string? line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                            var prefixed = $"[{now}] [Merged] [{component}] [D] {line}";
                            File.AppendAllText(LogPath, prefixed + Environment.NewLine);
                        }
                    }

                    File.AppendAllText(LogPath, footer);
                }
                catch { }
            }
        }

        // Удаляет файлы логов старше maxDays, если не включён режим вечного хранения
        public static void PurgeOldLogs(int maxDays, bool infinite)
        {
            if (infinite || maxDays <= 0) return;

            try
            {
                string dir = Path.GetDirectoryName(LogPath)!;
                if (!Directory.Exists(dir)) return;

                var files = Directory.GetFiles(dir, "*.log");
                foreach (var f in files)
                {
                    try
                    {
                        var fi = new FileInfo(f);
                        if ((DateTime.Now - fi.LastWriteTime).TotalDays > maxDays)
                            File.Delete(f);
                    }
                    catch { }
                }
            }
            catch { }
        }
    }
}

