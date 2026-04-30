using System;
using System.IO;

namespace MarsXZMedia
{
    public static class LogService
    {
        // Путь к папке Logs и файлу
        private static readonly string LogDirectory = AppPaths.LogsDirectory;
        private static readonly string LogPath = Path.Combine(LogDirectory, "latest.log");
        private static readonly object _lock = new object();

        static LogService()
        {
            try
            {
                // Создаем папку при первом обращении к логу, если её нет
                if (!Directory.Exists(LogDirectory))
                    Directory.CreateDirectory(LogDirectory);
            }
            catch { }
        }

        // Универсальный метод, который принимает уровень, сообщение и (опционально) ошибку
        public static void Log(string level, string message, string windowName, Exception? ex = null)
        {
            lock (_lock)
            {
                try
                {
                    string time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                    string line = $"[{time}] [{windowName}] [{level.ToUpper()}] {message}";
                    
                    if (ex != null)
                    {
                        line += $"\n{new string('!', 20)}\n[ERROR]: {ex}\n{new string('!', 20)}";
                    }

                    File.AppendAllText(LogPath, line + Environment.NewLine);
                }
                catch { }
            }
        }
    }
}
