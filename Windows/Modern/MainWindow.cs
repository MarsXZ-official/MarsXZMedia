using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Media; 
using Avalonia.Media.Imaging;
using CommunityToolkit.WinUI.Notifications;
using Windows.UI.Notifications;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Linq;
using System.Threading;
using System.Text;

namespace MarsXZMedia;

public partial class MainWindow : Window
{
    private double _inlineLastPercent = 0;
    private string _inlineLastMessage = "";
    private bool _isDownloadingAudio = false;
    private CancellationTokenSource? _fetchCts = null;
    private bool _isAnalyzing = false;
    private int _maxVideoHeight = 0;
    private string _lastDownloadedFile = "";

    // Флаги/данные аудио
    private bool _hasAudioFormats = false; // показывает аудио-дорожки (только оригинальные)
    private bool _hasExtractableAudio = false; // есть ли вообще аудио для извлечения (mp3)
    private int _maxAudioAbr = 0;
    private readonly List<AudioCandidate> _audioCandidates = new();

    private sealed class AudioCandidate
    {
        public string Id = "";
        public string Lang = "";
        public string Note = "";
        public string NoteKey = "";
        public bool IsOriginal;
        public int Abr;
        public int Height;
        public bool IsAudioOnly;
        public string Acodec = "";
        public string Ext = "";

        public string id { get => Id; set => Id = value; }
        public string lang { get => Lang; set => Lang = value; }
        public string note { get => Note; set => Note = value; }
        public string noteKey { get => NoteKey; set => NoteKey = value; }
        public bool isOriginal { get => IsOriginal; set => IsOriginal = value; }
        public int abr { get => Abr; set => Abr = value; }
        public int height { get => Height; set => Height = value; }
        public bool isAudioOnly { get => IsAudioOnly; set => IsAudioOnly = value; }
        public string acodec { get => Acodec; set => Acodec = value; }
        public string ext { get => Ext; set => Ext = value; }
    }

    // Настройки путей
    public static bool SeparatePaths { get; set; } = true;
    public static bool CreateSubfolders { get; set; } = true;
    public static bool UseDefaultPath { get; set; } = false;

    // Настройки логирования
    public static bool DisableLogs { get; set; } = false;
    public static bool LogAutoDeleteInfinite { get; set; } = true; // Если true — не удалять логи никогда (по умолчанию)
    public static int LogAutoDeleteMaxDays { get; set; } = 30; // Максимально дней хранения
    private DateTime _lastToastTime = DateTime.MinValue;
    // Путь к папке приложения (рядом с exe)
    private bool _toastCreated = false;
    // Константы для идентификации уведомления (чтобы обновлять одно и то же, а не плодить новые)
    private const string ToastTag = "download_job";
    private const string ToastGroup = "mars_downloader";
    private uint _toastSequence = 0; // Для правильной очереди обновлений
    // Добавьте это поле к остальным статическим свойствам (например, рядом с VideoPath)
    public static bool DisableOpenFile { get; set; } = false;

// УДАЛИТЬ эту строку:
// private readonly string _binPath = Path.Combine(
//     Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
//     "MarsXZ Media",
//     "Bin");

// ДОБАВИТЬ вместо него:
public static readonly string AppDirectory = AppPaths.AppDirectory;

public static string BinPath => AppDirectory;                    // все инструменты лежат здесь
public static string VideoPath = AppPaths.DownloadsRoot;
public static string LastCustomVideoPath = "";
public static string LastCustomMusicPath = "";
public static string MusicPath = AppPaths.DownloadsRoot;

    private static string GetOutputPath(string basePath, string subfolder)
    {
        if (!CreateSubfolders) return basePath;
        if (string.IsNullOrWhiteSpace(basePath)) return basePath;

        string trimmed = basePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string leaf = Path.GetFileName(trimmed);
        if (leaf.Equals(subfolder, StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        return Path.Combine(trimmed, subfolder);
    }

    private static void EnsureDirectorySafe(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        if (!Directory.Exists(path)) Directory.CreateDirectory(path);
    }


    public MainWindow()
    {
        InitializeComponent();
        SoundService.AttachClickSound(this);
        _downloadContent = MainContent.Content;
        InitSessionLog();

        // Сохраняем лог при падении приложения
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            try
            {
                Log("E", "Критическая ошибка", e.ExceptionObject as Exception); // <-- Замени SaveTextLog
            }
            catch { }
        };
    }

    private string? _sessionLogPath;

// Вызови это в конструкторе MainWindow() после InitializeComponent();
private void InitSessionLog()
{
    try
    {
        string fileName = $"Log_{DateTime.Now:yyyy-MMM-dd_HH'h'-mm'm'}.log";
        _sessionLogPath = Path.Combine(fileName);
        
        Log("I", "=== ПРИЛОЖЕНИЕ ЗАПУЩЕНО ===");

        // Выполняем очистку старых логов согласно настройкам (если авто-удаление включено)
        try
        {
            if (!LogAutoDeleteInfinite && !DisableLogs)
            {
                SharedLogService.PurgeOldLogs(LogAutoDeleteMaxDays, LogAutoDeleteInfinite);
            }
        }
        catch { }
    }
    catch { }
}
// Добавьте это поле в начало класса, если его еще нет
// Оно будет хранить интерфейс главной страницы (скачивания)
private object? _downloadContent;
private HistoryView? _historyView;

// В конструкторе или при первой загрузке сохраните текущий контент
// Например: _downloadContent = MainContent.Content;
private void OpenMain_Click(object? sender, RoutedEventArgs e)
{
    // Возвращаемся к интерфейсу скачивания
    if (_downloadContent != null)
    {
        MainContent.Content = _downloadContent;
    }
}
private void CreateWindowsToast()
{
    if (!OperatingSystem.IsWindows()) return;

    // Сначала удаляем старое, если оно висит, чтобы обновить структуру
    ToastNotificationManagerCompat.History.Remove("yt-download", "downloads");

    var builder = new ToastContentBuilder()
        // Строка 1: Название видео
        .AddText(new BindableString("videoTitle")) 
        // Строка 2: Процент скачивания
        .AddText(new BindableString("percentText"))
        // Прогресс-бар и его поля
        .AddVisualChild(new AdaptiveProgressBar()
        {
            Value = new BindableProgressBarValue("progressValue"),
            // Строка 3: Скорость (слева над баром)
            Title = new BindableString("speedText"),
            // Строка 4: Осталось (справа над баром)
            ValueStringOverride = new BindableString("etaText"),
            // Строка 5: Статус загрузки (под баром)
            Status = new BindableString("statusText")
        });

    builder.Show(toast =>
    {
        toast.Tag = "yt-download";
        toast.Group = "downloads";
        
        var data = new NotificationData();
        data.Values["videoTitle"] = "Загрузка...";
        data.Values["percentText"] = "Скачивание: 0%";
        data.Values["progressValue"] = "0";
        data.Values["speedText"] = "Скорость: 0 Кб/с";
        data.Values["etaText"] = "Осталось: --";
        data.Values["statusText"] = "Статус: 0 / 0 Мб";
        
        toast.Data = data;
    });

    _toastCreated = true;
}
private void UpdateWindowsToast(double percent, string speed, string eta, string sizeInfo)
{
    if (!OperatingSystem.IsWindows()) return;
    if (!_toastCreated) CreateWindowsToast();

    // Троттлинг (не чаще раза в 800мс)
    if (percent < 100 && (DateTime.Now - _lastToastTime).TotalMilliseconds < 800)
        return;

    _lastToastTime = DateTime.Now;

    var data = new NotificationData();
    data.Values["videoTitle"] = $"Загрузка: {_lastDownloadedFile}";
    data.Values["percentText"] = $"Скачивание: {percent:F1}%";
    data.Values["progressValue"] = (percent / 100.0).ToString(System.Globalization.CultureInfo.InvariantCulture);
    
    // Разбиваем Скорость и Осталось по разным углам для красоты
    data.Values["speedText"] = $"Скорость: {speed}";
    data.Values["etaText"] = $"Осталось: {FormatRussianEta(eta)}";
    data.Values["statusText"] = $"Статус: {sizeInfo}";

    try {
        ToastNotificationManagerCompat.CreateToastNotifier().Update(data, "yt-download", "downloads");
    } catch {
        _toastCreated = false; // Если упало, пересоздадим в следующий раз
    }
}
private string FormatRussianEta(string rawEta)
{
    if (string.IsNullOrEmpty(rawEta) || rawEta.Contains("Unknown")) return "расчет...";
    
    try {
        string[] parts = rawEta.Split(':');
        int len = parts.Length;

        // yt-dlp может вернуть H:M:S или M:S или даже D:H:M:S
        int s = len > 0 ? int.Parse(parts[len - 1]) : 0;
        int m = len > 1 ? int.Parse(parts[len - 2]) : 0;
        int h = len > 2 ? int.Parse(parts[len - 3]) : 0;
        int d = len > 3 ? int.Parse(parts[len - 4]) : 0;

        if (d > 0) return $"{d} {GetDecl(d, "день", "дня", "дней")} {h} ч.";
        if (h > 0) return $"{h} {GetDecl(h, "час", "часа", "часов")} {m} мин.";
        if (m > 0) return $"{m} {GetDecl(m, "минута", "минуты", "минут")} {s} сек.";
        
        return $"{s} {GetDecl(s, "секунда", "секунды", "секунд")}";
    } 
    catch { return rawEta; }
}
private string GetDecl(int n, string one, string two, string five)
{
    n = Math.Abs(n) % 100;
    int n1 = n % 10;
    if (n > 10 && n < 20) return five;
    if (n1 > 1 && n1 < 5) return two;
    if (n1 == 1) return one;
    return five;
}
private void OpenHistory_Click(object? sender, RoutedEventArgs e)
{
    if (_downloadContent == null)
    {
        _downloadContent = MainContent.Content;
    }

    if (_historyView == null)
    {
        _historyView = new HistoryView();
        _historyView.EntrySelected += OnHistoryEntrySelected;
    }

    _historyView.RefreshView();
    MainContent.Content = _historyView;
}

private void OnHistoryEntrySelected(HistoryEntry entry)
{
    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
    {
        if (_downloadContent != null)
        {
            MainContent.Content = _downloadContent;
        }

        UrlBox.Text = entry.Url;
        FetchInfo(null, null);
    });
}
private void OpenSettings_Click(object? sender, RoutedEventArgs e)
{
    // Сохраняем главный экран перед переходом в настройки, если еще не сохранили
    if (_downloadContent == null) 
    {
        _downloadContent = MainContent.Content;
    }

    var settingsView = new SettingsView();
    
    // Подписываемся на события сохранения или отмены, чтобы вернуться назад
    settingsView.SettingsSaved += (s, args) => MainContent.Content = _downloadContent;
    settingsView.SettingsCanceled += (s, args) => MainContent.Content = _downloadContent;

    MainContent.Content = settingsView;
}
    private void ShowSettingsInline()
    {
        try
        {
            var settings = new SettingsView();
            settings.BackRequested += () =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() => RestoreMainContent());
            };

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    if (MainContent != null)
                    {
                        MainContent.Content = settings;
                    }
                    else
                    {
                        // Fallback: open as separate window
                        var win = new Window { Title = "Настройки", Width = 800, Height = 600, Content = settings };
                        SoundService.AttachClickSound(win);
                        win.Show();
                    }
                }
                catch (Exception ex2)
                {
                    Log("E", "Не удалось встроить настройки, открываю в отдельном окне", ex2);
                    try
                    {
                        var win2 = new Window { Title = "Настройки", Width = 800, Height = 600, Content = settings };
                        SoundService.AttachClickSound(win2);
                        win2.Show();
                    }
                    catch (Exception ex3)
                    {
                        Log("E", "Провал при открытии настроек даже в отдельном окне", ex3);
                        ShowInlineNotification("Ошибка", "Не удалось открыть настройки. Проверьте логи.");
                    }
                }
            });
        }
        catch (Exception ex)
        {
            Log("E", "Не удалось открыть настройки встроенно", ex);
            // Попытка открыть в отдельном окне
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    var settings = new SettingsView();
                    settings.BackRequested += () => { Avalonia.Threading.Dispatcher.UIThread.Post(() => RestoreMainContent()); };
                    var win = new Window { Title = "Настройки", Width = 800, Height = 600, Content = settings };
                    SoundService.AttachClickSound(win);
                    win.Show();
                }
                catch (Exception ex2)
                {
                    Log("E", "Провал при открытии настроек в отдельном окне", ex2);
                    ShowInlineNotification("Ошибка", "Не удалось открыть настройки. Проверьте логи.");
                }
            });
        }
    }

    private void RestoreMainContent()
    {
        try
        {
        }
        catch (Exception ex)
        {
            Log("E", "Не удалось восстановить главное содержимое", ex);
        }
    }
// Метод для вставки текста из буфера обмена
private async void OnPasteClick(object? sender, RoutedEventArgs e)
{
    var topLevel = TopLevel.GetTopLevel(this);
    var clipboard = topLevel?.Clipboard;
    if (clipboard == null) return;

    string? text = null;
    if (clipboard is IAsyncDataTransfer dt)
    {
        text = await dt.TryGetTextAsync();
    }
    else if (topLevel is IAsyncDataTransfer dt2)
    {
        text = await dt2.TryGetTextAsync();
    }
    else
    {
#pragma warning disable CS0618
        text = await clipboard.GetTextAsync();
#pragma warning restore CS0618
    }

    if (!string.IsNullOrEmpty(text))
    {
        UrlBox.Text = text;
        // Активируем кнопку поиска вручную после вставки
        FindButton.IsEnabled = true; 
    }
}

private void OnUrlTextChanged(object? sender, TextChangedEventArgs e)
{
    // Проверка на null важна при инициализации окна
    if (UrlBox == null || FindButton == null) return;

    string url = UrlBox.Text?.Trim() ?? "";

    if (string.IsNullOrWhiteSpace(url))
    {
        FindButton.IsEnabled = false;
        UrlBox.BorderBrush = null; // Стандартный цвет
        return;
    }

    // Проверка на YouTube (включая мобильную версию и короткие ссылки)
    bool isYoutube = url.Contains("youtube.com") || 
                     url.Contains("youtu.be") || 
                     url.Contains("m.youtube.com");

    if (isYoutube)
    {
        FindButton.IsEnabled = true;
        // Подсвечиваем рамку мягким зеленым цветом
        UrlBox.BorderBrush = Brush.Parse("#4CAF50"); 
    }
    else
    {
        FindButton.IsEnabled = false;
        // Подсвечиваем красным, если это не YouTube
        UrlBox.BorderBrush = Brushes.Red;
    }
}
// Универсальный метод: level (I, W, E, D), сообщение и ошибка (если есть)
// Замените существующий метод Log на этот:
// Замените старый метод Log(string level, string msg) на этот:// Замените существующий метод Log на этот:
private void Log(string level, string message, Exception? ex = null)
{
    SharedLogService.WriteLine(level, message, "Main", ex);
}
    private void ApplyDownloadTypeUI()
    {
        try
        {
            bool isAudioMode = (DownloadTypeList?.SelectedIndex == 1);
            bool hasAudioTracks = AudioList.Items.Count > 1;

            // УБИРАЕМ СТАРУЮ ЛОГИКУ СКРЫТИЯ АУДИО — ТЕПЕРЬ ВСЁ В UpdateAudioList()
            // bool hasAudioTracks = AudioList.Items.Count > 1;
            // AudioLabel.IsVisible = hasAudioTracks;
            // AudioList.IsVisible = hasAudioTracks;


            // На всякий случай показываем, если есть хотя бы одна дорожка
            if (AudioLabel != null && AudioList != null)
            {
                bool showAudio = AudioList.Items.Count > 1;
                AudioLabel.IsVisible = showAudio;
                AudioList.IsVisible = showAudio;
            }
            // Единый блок качества/битрейта
            QualityOrBitrateLabel.IsVisible = true;
            QualityOrBitrateList.IsVisible = true;

            if (isAudioMode)
            {
                QualityOrBitrateLabel.Content = "Выберите битрейт:";
                UpdateBitrateList(); // Заполняем битрейтами
                DownloadButton.IsEnabled = _hasExtractableAudio && QualityOrBitrateList.SelectedIndex >= 0;
            }
            else
            {
                QualityOrBitrateLabel.Content = "Выберите качество:";
                UpdateVideoQualityList(); // Заполняем качествами видео
                DownloadButton.IsEnabled = QualityOrBitrateList.SelectedIndex >= 0;
            }
        }
        catch { }
    }

    private void ShowSystemToast(string title, string message, bool silent = false)
{
    // Проверяем, что мы в Windows, чтобы избежать ошибок на Linux/Mac
    if (OperatingSystem.IsWindows())
    {
        try
        {
            // Очищаем предыдущие уведомления (опционально)
            // ToastNotificationManagerCompat.History.Clear(); 

            new ToastContentBuilder()
                .AddText(title)
                .AddText(message)
                // Можно добавить аргументы, если нужно обрабатывать клики
                .AddArgument("action", "viewDownload")
                .Show();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Ошибка системного уведомления: {ex.Message}");
        }
    }
}
    // Встроенные уведомления в приложении (сверху)
    public void ShowInlineNotification(string title, string message, int seconds = 3, bool silent = false)
{
    // Логируем
    Log("I", $"Уведомление: {title} - {message}");

    // Вместо показа внутри окна, шлем в Windows
    ShowSystemToast(title, message, silent);
}

public void HideInlineNotification(object? sender, Avalonia.Interactivity.RoutedEventArgs e) { }
public void HideInlineNotification() { }

    private async Task HideInlineNotificationAnimated() 
{ 
    await Task.CompletedTask; 
}

    // ВАЖНО: Обновление прогресс-бара
// Если вы удалили прогресс-бар из уведомления, этот код вызывал ошибку.
public async Task UpdateInlineProgress(double percent, string message)
{
    _inlineLastPercent = percent;
    _inlineLastMessage = message;

    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
    {
        // Логика обновления вашего кастомного уведомления
        // Предполагаем, что у вас есть элементы управления в XAML:
        
        // NotificationProgressBar.Value = percent;
        // NotificationMessage.Text = message;
        
        // Для заголовка "Скачивание 50%" можно добавить логику прямо сюда:
        // NotificationTitle.Text = $"Скачивание {percent:0.0}%";
    });
    
    await Task.CompletedTask;
}


            private void SetVideoDescription(string raw)
    {
        var vd = VideoDescription1;
        if (vd == null) return;

        string text = BuildDescriptionPreview(raw, out int maxLines);
        vd.MaxLines = maxLines;
        vd.TextWrapping = TextWrapping.Wrap;
        vd.TextTrimming = TextTrimming.CharacterEllipsis;
        vd.Text = string.Empty;

        var inlines = vd.Inlines;
        if (inlines == null) return;
        inlines.Clear();
        AppendMarkupInlines(inlines, text);
    }

    private static string BuildDescriptionPreview(string raw, out int maxLines)
    {
        maxLines = 3;
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

        string text = raw.Replace("\r\n", "\n").Trim();

        int blankIndex = text.IndexOf("\n\n", StringComparison.Ordinal);
        if (blankIndex >= 0)
        {
            text = text.Substring(0, blankIndex).TrimEnd();
            maxLines = 2;
        }

        if (!text.Contains('\n'))
        {
            const int hardLimit = 220;
            if (text.Length > hardLimit)
            {
                int limit = Math.Min(hardLimit, text.Length - 1);
                int cut = text.LastIndexOfAny(new[] { '.', ',' }, limit);
                if (cut >= 0)
                {
                    text = text.Substring(0, cut + 1).TrimEnd();
                }
                else
                {
                    cut = text.LastIndexOf(' ', limit);
                    if (cut < 0) cut = limit;
                    text = text.Substring(0, cut).TrimEnd();
                }
            }
        }

        return text;
    }

    private static void AppendMarkupInlines(InlineCollection inlines, string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        int last = 0;
        int i = 0;
        while (i < text.Length)
        {
            char marker = text[i];
            if (marker == '*' || marker == '_')
            {
                int end = text.IndexOf(marker, i + 1);
                if (end > i + 1)
                {
                    AddPlainInline(inlines, text.Substring(last, i - last));
                    string inner = text.Substring(i + 1, end - i - 1);
                    if (marker == '*') AddStyledInline(inlines, inner, bold: true, italic: false);
                    else AddStyledInline(inlines, inner, bold: false, italic: true);
                    i = end + 1;
                    last = i;
                    continue;
                }
            }
            i++;
        }

        AddPlainInline(inlines, text.Substring(last));
    }

    private static void AddPlainInline(InlineCollection inlines, string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        inlines.Add(new Run(text));
    }

    private static void AddStyledInline(InlineCollection inlines, string text, bool bold, bool italic)
    {
        if (string.IsNullOrEmpty(text)) return;

        var span = new Span();
        if (bold) span.FontWeight = FontWeight.Bold;
        if (italic) span.FontStyle = FontStyle.Italic;
        span.Inlines.Add(new Run(text));
        inlines.Add(span);
    }

    private async void FetchInfo(object? sender, Avalonia.Interactivity.RoutedEventArgs? e) // переименовал аргумент, чтобы не путать с полем
{
    // 1. Проверяем наличие пути и текста
    if (string.IsNullOrWhiteSpace(UrlBox.Text)) return;
    
    string ytDlpFullPath = Path.Combine(AppDirectory, "yt-dlp.exe");
    
    if (!_isAnalyzing)
    {
        ShowInlineNotification("Анализ", "Получаю информацию о видео…", 0);
    }

    _fetchCts?.Cancel();
    _fetchCts = new CancellationTokenSource();
    var fetchToken = _fetchCts.Token;

    _isAnalyzing = true;
    string targetUrl = UrlBox.Text; // используем текст из бокса
    string? thumbUrl = null;

    // Блокируем кнопку на время анализа
    FindButton.IsEnabled = false;

    await Task.Run(async () =>
    {
        string lastOut = string.Empty, lastErr = string.Empty;
        try
        {
            // 2. Настраиваем ОДИН правильный ProcessStartInfo
            string jsArg = GetPreferredJsRuntimeArg();
            string infoArgs = string.IsNullOrWhiteSpace(jsArg)
                ? $"--dump-json --no-warnings \"{targetUrl}\""
                : $"{jsArg} --dump-json --no-warnings \"{targetUrl}\"";

            var psi = new ProcessStartInfo
            {
                FileName = ytDlpFullPath,               // ПОЛНЫЙ ПУТЬ
                // В ProcessStartInfo:
                WorkingDirectory = AppDirectory,
                Arguments = infoArgs,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            // 3. Запускаем именно настроенный psi
            using var process = Process.Start(psi);
            if (process == null) return;

            var sbOut = new StringBuilder();
            var sbErr = new StringBuilder();
            var outReader = process.StandardOutput;
            var errReader = process.StandardError;

            DateTime lastOutput = DateTime.UtcNow;
            var stopwatch = Stopwatch.StartNew();
            string? foundJsonText = null;

            // Задача для чтения stdout
            var readOutTaskWithHeartbeat = Task.Run(async () =>
            {
                try
                {
                    string? line;
                    while (!fetchToken.IsCancellationRequested && (line = await outReader.ReadLineAsync()) != null)
                    {
                        sbOut.AppendLine(line);
                        lastOutput = DateTime.UtcNow;

                        var trimmed = line.Trim();
                        if (trimmed.StartsWith("{") || trimmed.Contains("\"title\""))
                        {
                            try
                            {
                                using var doc = JsonDocument.Parse(trimmed);
                                if (doc.RootElement.TryGetProperty("formats", out var f))
                                {
                                    foundJsonText = trimmed;
                                    process.Kill(true); // Нашли JSON — закрываем процесс для скорости
                                    break;
                                }
                            }
                            catch { /* не полный JSON */ }
                        }
                    }
                }
                catch (Exception ex) { Log("E", "Ошибка чтения stdout", ex); }
            });

            // Задача для чтения stderr
            var readErrTaskWithHeartbeat = Task.Run(async () =>
            {
                try
                {
                    string? line;
                    while ((line = await errReader.ReadLineAsync()) != null)
                    {
                        sbErr.AppendLine(line);
                    }
                }
                catch { }
            });

            // Цикл ожидания завершения
            int noOutputTimeout = 60; 
            while (!process.HasExited)
            {
                if (fetchToken.IsCancellationRequested)
                {
                    process.Kill(true);
                    return;
                }

                if ((DateTime.UtcNow - lastOutput).TotalSeconds > noOutputTimeout)
                {
                    process.Kill(true);
                    ShowInlineNotification("Ошибка", "Таймаут: yt-dlp не отвечает.", 6);
                    return;
                }
                await Task.Delay(500);
            }

            await Task.WhenAll(readOutTaskWithHeartbeat, readErrTaskWithHeartbeat);

            lastOut = foundJsonText ?? sbOut.ToString();
            lastErr = sbErr.ToString();

            if (string.IsNullOrWhiteSpace(lastOut))
            {
                Log("E", $"Пустой ответ. Stderr: {lastErr}");
                ShowInlineNotification("Ошибка", "Видео не найдено или доступ ограничен.", 8);
                return;
            }

            // 4. Парсинг результата
            using var finalDoc = JsonDocument.Parse(lastOut);
            var data = finalDoc.RootElement;

            string title = data.GetProperty("title").GetString() ?? "Без названия";
            string duration = data.TryGetProperty("duration_string", out var d) ? d.GetString() ?? "" : "";
            thumbUrl = data.TryGetProperty("thumbnail", out var t) ? t.GetString() : null;
            // ДОБАВЬ ЭТУ СТРОКУ:
            // Получаем описание и ограничиваем его длину, чтобы не перегружать UI
            string description = data.TryGetProperty("description", out var desc) ? desc.GetString() ?? "" : "";
            string author = data.TryGetProperty("uploader", out var u) ? u.GetString() ?? "" : "";

            if (data.TryGetProperty("formats", out var formats))
{
    int maxHeight = 0;
    foreach (var fmt in formats.EnumerateArray())
    {
        // ПРОВЕРКА ТУТ: Аналогично, защищаемся от Null в поле height
        if (fmt.TryGetProperty("height", out var hProp) && hProp.ValueKind == JsonValueKind.Number)
        {
            int h = hProp.GetInt32();
            if (h > maxHeight) maxHeight = h;
        }
    }
    _maxVideoHeight = maxHeight;

    Avalonia.Threading.Dispatcher.UIThread.Post(() => 
    {
        UpdateAudioList(formats);
        UpdateVideoQualityList(formats);
    });
}
            // Загрузка превью и обновление UI
            Bitmap? bitmap = null;
            if (!string.IsNullOrEmpty(thumbUrl))
            {
                try {
                    using var client = new HttpClient();
                    var bytes = await client.GetByteArrayAsync(thumbUrl);
                    bitmap = new Bitmap(new MemoryStream(bytes));
                } catch { }
            }

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                VideoTitle.Text = title;
                // ДОБАВЬ ЭТУ СТРОКУ:
                SetVideoDescription(description);
                VideoAuthor.Text = "Автор: " + author;
                VideoDuration.Text = "Время: " + duration;
                PreviewImage.Source = bitmap;
                InfoPanel.IsVisible = true;
                ApplyDownloadTypeUI();
                try
                {
                    HistoryStore.Add(title, targetUrl);
                }
                catch { }
                ShowInlineNotification("Готово", $"Найдено: {title}");
            });
        }
        catch (Exception ex)
        {
            Log("E", $"Критическая ошибка FetchInfo: {ex.Message}");
            ShowInlineNotification("Ошибка", "Не удалось проанализировать ссылку.", 8);
        }
        finally
        {
            _isAnalyzing = false;
            Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                FindButton.IsEnabled = true;
                HideInlineNotification();
            });
        }
    });
}
    private void UpdateAudioList(JsonElement formats)
    {
        AudioList.Items.Clear();
        _hasAudioFormats = false;
        _maxAudioAbr = 0;
        _audioCandidates.Clear();

        var candidates = new System.Collections.Generic.List<AudioCandidate>();
        var noLangCandidates = new System.Collections.Generic.List<AudioCandidate>();

        var langMap = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            {"en","Английский"}, {"en-GB","Английский (Великобритания)"}, {"en-US","Английский (США)"},
            {"ru","Русский"}, {"uk","Украинский"}, {"be","Белорусский"},
            {"kk","Казахский"}, {"kz","Казахский"},
            {"es","Испанский"}, {"es-419","Испанский (Лат. Америка)"},
            {"pt","Португальский"}, {"pt-BR","Португальский (Бразилия)"},
            {"fr","Французский"}, {"fr-CA","Французский (Канада)"}, {"de","Немецкий"}, {"it","Итальянский"}, {"nl","Нидерландский"},
            {"sv","Шведский"}, {"no","Норвежский"}, {"da","Датский"}, {"fi","Финский"},
            {"zh","Китайский"}, {"zh-CN","Китайский (упрощ.)"}, {"zh-TW","Китайский (Тайвань)"}, {"zh-HK","Китайский (Гонконг)"}, {"ja","Японский"}, {"ko","Корейский"},
            {"hi","Хинди"}, {"bn","Бенгальский"}, {"pa","Пенджаби"}, {"ur","Урду"}, {"fa","Персидский"}, {"gu","Гуджарати"}, {"mr","Маратхи"},
            {"te","Телугу"}, {"ta","Тамильский"}, {"kn","Каннада"}, {"ml","Малаялам"}, {"si","Сингальский"},
            {"my","Бирманский"}, {"km","Кхмерский"}, {"lo","Лаосский"},
            {"th","Тайский"}, {"vi","Вьетнамский"}, {"id","Индонезийский"}, {"ms","Малайский"}, {"tl","Филиппинский"},
            {"ar","Арабский"}, {"he","Иврит"}, {"tr","Турецкий"}, {"el","Греческий"}, {"pl","Польский"}, {"cs","Чешский"}, {"sk","Словацкий"}, {"sl","Словенский"}, {"hr","Хорватский"}, {"sr","Сербский"}, {"bg","Болгарский"}, {"ro","Румынский"}, {"hu","Венгерский"}, {"lt","Литовский"}, {"lv","Латышский"}, {"et","Эстонский"}, {"is","Исландский"}, {"mk","Македонский"},
            {"sq","Албанский"}, {"bs","Боснийский"}, {"ga","Ирландский"}, {"cy","Валлийский"}, {"mt","Мальтийский"}, {"af","Африкаанс"},
            {"sw","Суахили"}, {"yo","Йоруба"}, {"ig","Игбо"}, {"ha","Хауса"}, {"zu","Зулу"}, {"xh","Коса"},
            {"su","Сунданский"}, {"jw","Яванский"}, {"mg","Малагасийский"}, {"eu","Баскский"}, {"ca","Каталанский"}, {"gl","Галицийский"}, {"la","Латинский"}, {"st","Сесото"}, {"tg","Таджикский"}, {"uz","Узбекский"}, {"ky","Киргизский"}, {"az","Азербайджанский"}, {"hy","Армянский"}, {"ka","Грузинский"}, {"ps","Пушту"}, {"ne","Непальский"}, {"mn","Монгольский"}, {"am","Амхарский"}, {"so","Сомали"}, {"sd","Синдхи"}, {"tt","Татарский"}
        };

        foreach (var fmt in formats.EnumerateArray())
        {
            string vcodec = fmt.TryGetProperty("vcodec", out var vc) ? (vc.GetString() ?? "") : "";
            string acodec = fmt.TryGetProperty("acodec", out var ac) ? (ac.GetString() ?? "") : "";
            string ext = fmt.TryGetProperty("ext", out var ex) ? (ex.GetString() ?? "") : "";

            bool isAudioOnly = vcodec == "none" || vcodec == "null";
            bool hasAudio = !string.IsNullOrWhiteSpace(acodec) && acodec != "none" && acodec != "null";
            if (!hasAudio) continue;
            string id = fmt.GetProperty("format_id").GetString() ?? "";
            if (id.StartsWith("sb")) continue;

            string? lang = fmt.TryGetProperty("language", out var langElem) ? langElem.GetString() : null;
            string langKey = (lang ?? "").Trim().ToLowerInvariant();
            if (langKey.StartsWith("track") || langKey.StartsWith("audio"))
            {
                continue;
            }

            bool hasLang = !string.IsNullOrWhiteSpace(langKey) && langKey != "und" && langKey != "unknown";
            if (hasLang)
            {
                lang = lang!.Trim();
            }
            else
            {
                lang = "";
            }

            string note = fmt.TryGetProperty("format_note", out var fn) ? fn.GetString() ?? "" : "";
            string noteLower = note.Trim().ToLowerInvariant();
            string roleKey = "";
            if (!string.IsNullOrEmpty(noteLower))
            {
                if (noteLower.Contains("original") || noteLower.Contains("orig") || noteLower.Contains("ориг")) roleKey = "original";
                else if (noteLower.Contains("default")) roleKey = "default";
                else if (noteLower.Contains("dub")) roleKey = "dub";
                else if (noteLower.Contains("descriptive") || noteLower.Contains("description") || noteLower.Contains("commentary")) roleKey = "descriptive";
            }

            int langPref = 0;
            if (fmt.TryGetProperty("language_preference", out var lp) && lp.ValueKind == JsonValueKind.Number)
            {
                langPref = lp.GetInt32();
            }
            if (string.IsNullOrEmpty(roleKey) && langPref > 0)
            {
                roleKey = "default";
            }

            bool isOriginal = roleKey.Equals("original", StringComparison.OrdinalIgnoreCase);

            int abr = 0;
            if (fmt.TryGetProperty("abr", out var abrElem) && abrElem.ValueKind == JsonValueKind.Number)
            {
                abr = (int)Math.Round(abrElem.GetDouble());
            }

            int height = 0;
            if (fmt.TryGetProperty("height", out var hProp) && hProp.ValueKind == JsonValueKind.Number)
            {
                height = hProp.GetInt32();
            }

            var candidate = new AudioCandidate
            {
                Id = id,
                Lang = lang,
                Note = isAudioOnly ? "" : note,
                NoteKey = roleKey,
                IsOriginal = isOriginal,
                Abr = abr,
                Height = height,
                IsAudioOnly = isAudioOnly,
                Acodec = acodec,
                Ext = ext
            };

            candidates.Add(candidate);
            if (!hasLang) noLangCandidates.Add(candidate);
        }

        _audioCandidates.AddRange(candidates);

        int RoleRank(AudioCandidate c)
        {
            string nk = (c.noteKey ?? "").ToLowerInvariant();
            if (nk == "original") return 3;
            if (nk == "default") return 2;
            if (nk == "dub") return 1;
            if (nk == "descriptive") return 0;
            return 0;
        }

        int CodecRank(AudioCandidate c)
        {
            string ac = (c.acodec ?? "").ToLowerInvariant();
            string ex2 = (c.ext ?? "").ToLowerInvariant();
            if (ac.StartsWith("mp4a") || ac == "aac" || ex2 == "m4a") return 3;
            if (ac.Contains("opus") || ex2 == "webm") return 2;
            return 1;
        }

        var filtered = candidates
            .Where(c => !string.IsNullOrWhiteSpace(c.lang))
            .GroupBy(c => (c.lang ?? "").Trim().ToLowerInvariant())
            .Select(g => g
                .OrderByDescending(c => RoleRank(c))
                .ThenByDescending(c => c.IsAudioOnly)
                .ThenByDescending(c => CodecRank(c))
                .ThenByDescending(c => c.abr)
                .First())
            .OrderByDescending(c => RoleRank(c))
            .ThenBy(c => c.lang)
            .ToList();

        if (filtered.Count > 0)
        {
            foreach (var c in filtered)
            {
                if (c.abr > _maxAudioAbr) _maxAudioAbr = c.abr;

                string langName = "";
                if (!string.IsNullOrEmpty(c.lang))
                {
                    if (langMap.TryGetValue(c.lang, out var mapped))
                    {
                        langName = mapped;
                    }
                    else
                    {
                        var baseLang = c.lang.Split('-', StringSplitOptions.RemoveEmptyEntries)[0];
                        if (!string.IsNullOrEmpty(baseLang) && langMap.TryGetValue(baseLang, out var baseMapped))
                        {
                            langName = baseMapped;
                        }
                    }
                }
                if (string.IsNullOrEmpty(langName))
                {
                    langName = c.lang.ToUpperInvariant();
                }

                string noteLabel = "";
                string nk = c.noteKey?.ToLowerInvariant() ?? "";
                if (nk == "original") noteLabel = "(Оригинальная)";
                else if (nk == "default") noteLabel = "";
                string displayName = string.IsNullOrEmpty(noteLabel) ? langName : $"{langName} {noteLabel}";

                var item = new ListBoxItem
                {
                    Content = displayName,
                    Tag = $"{c.id};{c.lang};{c.noteKey};{(c.isAudioOnly ? "audio" : "muxed")}"
                };
                AudioList.Items.Add(item);
            }
        }
        else if (noLangCandidates.Count > 0)
        {
            var best = noLangCandidates
                .OrderByDescending(c => RoleRank(c))
                .ThenByDescending(c => c.IsAudioOnly)
                .ThenByDescending(c => CodecRank(c))
                .ThenByDescending(c => c.abr)
                .First();

            if (best.abr > _maxAudioAbr) _maxAudioAbr = best.abr;

            var item = new ListBoxItem
            {
                Content = "Оригинальная",
                Tag = $"{best.id};;{best.noteKey};{(best.isAudioOnly ? "audio" : "muxed")}"
            };
            AudioList.Items.Add(item);
        }

        _hasExtractableAudio = AudioList.Items.Count > 0;
        _hasAudioFormats = filtered.Any(c => c.isOriginal);

        if (AudioList.Items.Count > 0)
        {
            AudioList.SelectedIndex = 0;
        }

        bool showAudioSelector = AudioList.Items.Count > 1;

        AudioLabel.IsVisible = showAudioSelector;
        AudioList.IsVisible = showAudioSelector;

        ApplyDownloadTypeUI();
    }
    private string? PickMuxedFormatId(string lang, string noteKey, int maxHeight)
    {
        if (string.IsNullOrWhiteSpace(lang)) return null;

    var query = _audioCandidates.Where(c =>
        !c.IsAudioOnly &&
        string.Equals(c.Lang, lang, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(c.NoteKey, noteKey ?? "", StringComparison.OrdinalIgnoreCase));

    if (!query.Any()) return null;

    AudioCandidate? best = null;
    if (maxHeight > 0)
    {
        best = query.Where(c => c.Height > 0 && c.Height <= maxHeight)
            .OrderByDescending(c => c.Height)
            .FirstOrDefault();
    }

    if (best == null)
    {
        best = query.OrderByDescending(c => c.Height).FirstOrDefault();
    }

    return best?.Id;
}
    private int AudioCodecRank(AudioCandidate c)
    {
        string ac = (c.Acodec ?? "").ToLowerInvariant();
        string ex2 = (c.Ext ?? "").ToLowerInvariant();
        if (ac.StartsWith("mp4a") || ac == "aac" || ex2 == "m4a") return 3;
        if (ac.Contains("opus") || ex2 == "webm") return 2;
        if (ex2 == "mp3") return 2;
        return 1;
    }

    private string PickPreferredAudioOnlyFormatId(string currentAudioId, string lang, string noteKey, int targetAbr = 0)
    {
        var direct = _audioCandidates.FirstOrDefault(a => a.IsAudioOnly && string.Equals(a.Id, currentAudioId, StringComparison.OrdinalIgnoreCase));
        if (direct != null)
            return direct.Id;

        IEnumerable<AudioCandidate> allAudioOnly = _audioCandidates.Where(a => a.IsAudioOnly);
        IEnumerable<AudioCandidate> query = allAudioOnly;

        bool hasRequestedLang = !string.IsNullOrWhiteSpace(lang);
        if (hasRequestedLang)
        {
            var langMatches = allAudioOnly.Where(a => string.Equals(a.Lang, lang, StringComparison.OrdinalIgnoreCase));
            if (!langMatches.Any())
                return string.Empty;

            query = langMatches;
        }

        bool hasRequestedNote = !string.IsNullOrWhiteSpace(noteKey);
        if (hasRequestedNote)
        {
            var noteMatches = query.Where(a => string.Equals(a.NoteKey, noteKey, StringComparison.OrdinalIgnoreCase));
            if (noteMatches.Any())
            {
                query = noteMatches;
            }
            else if (hasRequestedLang)
            {
                // Для выбранного языка не подменяем дорожку на другой язык/роль.
                // Иначе пользователь выбирает, например, English, а скачивается Russian original.
                return string.Empty;
            }
        }

        if (!query.Any())
            return string.Empty;

        if (targetAbr > 0)
        {
            var closeToTarget = query
                .Where(a => a.Abr >= targetAbr)
                .OrderBy(a => Math.Abs(a.Abr - targetAbr))
                .ThenByDescending(a => AudioCodecRank(a))
                .ThenByDescending(a => a.Abr)
                .FirstOrDefault();
            if (closeToTarget != null)
                return closeToTarget.Id;
        }

        return query
            .OrderByDescending(a => AudioCodecRank(a))
            .ThenByDescending(a => a.Abr)
            .First().Id;
    }
    private void UpdateVideoQualityList(JsonElement? formats = null)
{
    QualityOrBitrateList.Items.Clear();

    // 1. Если данные formats переданы, обновляем _maxVideoHeight
    if (formats.HasValue) // Проверяем, не пустой ли аргумент
    {
        _maxVideoHeight = 0;
        // Используем .Value, чтобы получить доступ к методам JsonElement
        foreach (var fmt in formats.Value.EnumerateArray())
{
    // ПРОВЕРКА ТУТ: Проверяем, что height существует и это число
            if (fmt.TryGetProperty("height", out var hProp) && hProp.ValueKind == JsonValueKind.Number)
            {
                // Также проверяем, что это видео-поток (vcodec не "none")
                if (fmt.TryGetProperty("vcodec", out var vc) && vc.GetString() != "none")
                {
                    int h = hProp.GetInt32();
                    if (h > _maxVideoHeight) _maxVideoHeight = h;
                }
            }
}
    }
    // 2. Список доступных пресетов
    var qualities = new (int height, string label)[]
    {
        (2160, "2160p (4К)"),
        (1440, "1440p (2К)"),
        (1080, "1080p (FHD)"),
        (720,  "720p (HD)"),
        (480,  "480p (SD)"),
        (360,  "360p (SD)"),
        (240,  "240p (SD)"),
        (144,  "144p (SD)")
    };

    // 3. Добавляем в список только то, что не выше фактического максимума
    foreach (var q in qualities)
    {
        if (_maxVideoHeight >= q.height)
        {
            QualityOrBitrateList.Items.Add(q.label);
        }
    }

    // Добавляем минимальное качество, если список пуст
    if (QualityOrBitrateList.Items.Count == 0 && _maxVideoHeight > 0)
    {
        QualityOrBitrateList.Items.Add($"{_maxVideoHeight}p (SD)");
    }

    // Выбираем лучшее из доступного
    if (QualityOrBitrateList.Items.Count > 0)
        QualityOrBitrateList.SelectedIndex = 0;
}
    private static int ParseRequestedVideoHeight(string? qualityLabel)
    {
        if (string.IsNullOrWhiteSpace(qualityLabel))
            return 1080;

        string label = qualityLabel.Trim();

        if (label.Contains("2160", StringComparison.OrdinalIgnoreCase) ||
            label.Contains("4K", StringComparison.OrdinalIgnoreCase) ||
            label.Contains("4К", StringComparison.OrdinalIgnoreCase))
            return 2160;

        if (label.Contains("1440", StringComparison.OrdinalIgnoreCase) ||
            label.Contains("2K", StringComparison.OrdinalIgnoreCase) ||
            label.Contains("2К", StringComparison.OrdinalIgnoreCase))
            return 1440;

        var match = Regex.Match(label, @"\d+");
        if (match.Success && int.TryParse(match.Value, out int parsed) && parsed > 0)
            return parsed;

        return 1080;
    }

    private void UpdateBitrateList()
    {
        if (!_hasExtractableAudio) return;

        var bitrates = new int[] { 64, 96, 128, 160, 192, 224, 256, 320 };
        QualityOrBitrateList.Items.Clear();
        foreach (var b in bitrates) QualityOrBitrateList.Items.Add($"{b} kbps");

        int defIdx = Array.IndexOf(bitrates, 320);
        QualityOrBitrateList.SelectedIndex = defIdx >= 0 ? defIdx : 2;
    }

    private void DownloadTypeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DownloadTypeList == null) return;

        ApplyDownloadTypeUI();
    }

    private void OnQualityOrBitrateChanged(object? sender, SelectionChangedEventArgs e)
    {
        try
        {
            DownloadButton.IsEnabled = QualityOrBitrateList.SelectedIndex >= 0;
        }
        catch
        {
            DownloadButton.IsEnabled = false;
        }
    }

    private async Task<string> TranslateTitleAsync(string text, string targetLang)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(targetLang)) return text;
            string langCode = targetLang.Split('-')[0].ToLower();
            string url = $"https://translate.googleapis.com/translate_a/single?client=gtx&sl=auto&tl={langCode}&dt=t&q={Uri.EscapeDataString(text)}";
            using var client = new HttpClient();
            var json = await client.GetStringAsync(url);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0)
            {
                var sb = new StringBuilder();
                foreach (var part in doc.RootElement[0].EnumerateArray())
                {
                    if (part.ValueKind == JsonValueKind.Array && part.GetArrayLength() > 0)
                    {
                        var seg = part[0].GetString();
                        if (!string.IsNullOrEmpty(seg)) sb.Append(seg);
                    }
                }
                var merged = sb.ToString();
                if (!string.IsNullOrWhiteSpace(merged)) return merged;
            }
            return text;
        }
        catch { return text; }
    }

    private bool IsFileLocked(string? path)
{
    if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;

    try
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        return false;
    }
    catch (IOException)
    {
        // Файл заблокирован (код ошибки 32 или 33)
        return true;
    }
    catch
    {
        return false;
    }
}
private void ShowDownloadStartToast(string filename)
{
    if (!OperatingSystem.IsWindows()) return;

    ToastNotificationManagerCompat.History.Remove(ToastTag, ToastGroup);

    var builder = new ToastContentBuilder()
        .AddText($"{(_isDownloadingAudio ? "Скачивание аудио" : "Скачивание видео")}: {filename.Trim()}")  // единственная строка сверху

        .AddVisualChild(new AdaptiveProgressBar()
        {
            Value               = new BindableProgressBarValue("progressValue"),
            Title               = new BindableString("speedEtaText"),   // слева + справа над баром
            Status              = new BindableString("statusText")      // под баром — процент + размер
        });

    builder.Show(toast =>
    {
        toast.Tag          = ToastTag;
        toast.Group        = ToastGroup;
        toast.ExpirationTime = DateTime.Now.AddHours(2);
    });

    // Начальное состояние
    var initial = new NotificationData { SequenceNumber = 0 };
    initial.Values["progressValue"]   = "0";
    initial.Values["speedEtaText"]    = "Скорость: --          Осталось: расчёт...";
    initial.Values["statusText"]      = "Завершено: 0%    Статус: 0 / ? МБ";

    try { ToastNotificationManagerCompat.CreateToastNotifier().Update(initial, ToastTag, ToastGroup); }
    catch { }
}

private void UpdateDownloadProgressToast(double percent, string speed, string eta, string sizeInfo)
{
    if (!OperatingSystem.IsWindows()) return;

    var now = DateTime.Now;
    if (percent > 0.5 && percent < 99.5 && (now - _lastToastTime).TotalMilliseconds < 900)
        return;

    _lastToastTime = now;
    _toastSequence = (_toastSequence + 1) % 100000;

    var data = new NotificationData { SequenceNumber = _toastSequence };

    data.Values["progressValue"]   = (percent / 100.0).ToString(System.Globalization.CultureInfo.InvariantCulture);
    data.Values["speedEtaText"]    = $"Скорость: {speed}          Осталось: {FormatRussianEta(eta)}";
    data.Values["statusText"]      = $"Завершено: {percent:F1}%    Статус: {sizeInfo}";

    try
    {
        ToastNotificationManagerCompat.CreateToastNotifier().Update(data, ToastTag, ToastGroup);
    }
    catch
    {
        ShowDownloadStartToast(_lastDownloadedFile ?? "Файл");
    }
}
private string GetDeclension(int n, string one, string two, string five)
{
    n = Math.Abs(n) % 100;
    int n1 = n % 10;
    if (n > 10 && n < 20) return five;
    if (n1 > 1 && n1 < 5) return two;
    if (n1 == 1) return one;
    return five;
}
private void ShowDownloadCompleteToast(string filename, bool success, string message = "")
{
    if (!OperatingSystem.IsWindows()) return;

    var builder = new ToastContentBuilder();
    builder.AddAudio(new Uri("ms-winsoundevent:Notification.Silent"));

    if (success)
    {
        SoundService.PlayApply();
    }

    if (success)
    {
        builder.AddText(_isDownloadingAudio ? "✅ Аудио скачано" : "✅ Видео скачано")
               .AddText(filename);
    }
    else
    {
        builder.AddText(_isDownloadingAudio ? "❌ Ошибка скачивания аудио" : "❌ Ошибка скачивания видео")
               .AddText(message);
    }

    // Используем те же Tag и Group. 
    // Это заставит Windows ЗАМЕНИТЬ прогресс-бар на финальное сообщение.
    builder.Show(toast =>
    {
        toast.Tag = ToastTag;
        toast.Group = ToastGroup;
    });
}
    // ================== ЗАГРУЗКА С ПРОГРЕССОМ ==================
 public async void Download(object? sender, RoutedEventArgs e)
{
    if (string.IsNullOrEmpty(UrlBox.Text)) return;

    // Сброс состояния прогресса
    _lastToastTime = DateTime.MinValue;
    _inlineLastPercent = 0;
    _inlineLastMessage = "";

    bool isAudioMode = (DownloadTypeList?.SelectedIndex == 1);
    _isDownloadingAudio = isAudioMode;
    string videoOutputPath = GetOutputPath(VideoPath, "Video");
    string audioOutputPath = GetOutputPath(MusicPath, "Audio");
    string targetPath = isAudioMode ? audioOutputPath : videoOutputPath;
    string fullOutputPath = targetPath;

    string finalFileName = SanitizeFileName(VideoTitle.Text?.Trim() ?? "Video");

    try 
    {
        if (CreateSubfolders)
        {
            EnsureDirectorySafe(videoOutputPath);
            EnsureDirectorySafe(audioOutputPath);
        }
        else
        {
            EnsureDirectorySafe(targetPath);
        }
    }
    catch (Exception ex)
    {
        Log("E", $"Ошибка создания папки: {targetPath}", ex);
        return; 
    }

     string audioId = "bestaudio";
     string targetLang = "";
     string audioNoteKey = "";
     string audioSource = "audio";

     // 1. Получаем данные о выбранной дорожке
     if (AudioList.IsVisible && AudioList.SelectedItem is ListBoxItem selectedItem)
     {
         string tagData = selectedItem.Tag?.ToString() ?? "";
         var parts = tagData.Split(';');
         if (parts.Length > 0) audioId = parts[0].Trim();
         if (parts.Length > 1) targetLang = parts[1].Trim();
         if (parts.Length > 2) audioNoteKey = parts[2].Trim();
         if (parts.Length > 3) audioSource = parts[3].Trim();
     }

     // 2. ПОДГОТОВКА ИМЕНИ ФАЙЛА (ПЕРЕВОД В НАЧАЛЕ)
     string rawTitle = VideoTitle.Text?.Trim() ?? "Video";

     // Если есть язык для перевода и он не "неопределен"
     if (!string.IsNullOrWhiteSpace(targetLang) && targetLang != "und")
     {
         ShowInlineNotification("Перевод", isAudioMode ? "Перевожу имя выходного аудиофайла..." : "Перевожу имя выходного видеофайла...", 0);
         string tl = targetLang.Split('-')[0].ToLowerInvariant();

         string translated = await TranslateTitleAsync(rawTitle, tl);
         finalFileName = SanitizeFileName(translated);
     }

     // 3. НАСТРОЙКА YT-DLP
     // Передаем полный путь через -o. %(ext)s позволит yt-dlp самому поставить .mp4 или .mp3
     string outputOptions = $"-o \"{Path.Combine(fullOutputPath, finalFileName)}.%(ext)s\" --force-overwrites --no-part --no-cache-dir";

     Avalonia.Threading.Dispatcher.UIThread.Post(() =>
     {
         ShowDownloadStartToast(finalFileName);
         UpdateDownloadProgressToast(0, "Подключение...", "Расчёт...", "0 / ? МБ");
     });

         AudioCandidate? selectedAudio = _audioCandidates.FirstOrDefault(a => a.Id == audioId);
     if (selectedAudio != null && selectedAudio.IsAudioOnly)
     {
         string acSel = (selectedAudio.Acodec ?? "").ToLowerInvariant();
         string exSel = (selectedAudio.Ext ?? "").ToLowerInvariant();
         bool isOpus = acSel.Contains("opus") || exSel == "webm";
         if (isOpus && !string.IsNullOrEmpty(selectedAudio.Lang))
         {
             var prefer = _audioCandidates
                 .Where(a => a.IsAudioOnly && a.Lang.Equals(selectedAudio.Lang, StringComparison.OrdinalIgnoreCase))
                 .Where(a => ((a.Acodec ?? "").ToLowerInvariant().StartsWith("mp4a") || (a.Acodec ?? "").ToLowerInvariant() == "aac" || (a.Ext ?? "").ToLowerInvariant() == "m4a"))
                 .OrderByDescending(a => a.Abr)
                 .FirstOrDefault();
             if (prefer != null) audioId = prefer.Id;
         }
     }

string langHeader = "";
    string formatArg = "";
    string conversionArgs = "";

    if (isAudioMode)
    {
        string bitrateStr = QualityOrBitrateList.SelectedItem?.ToString()?.Replace(" kbps", "") ?? "320";
        int targetAudioBitrate = 0;
        int.TryParse(bitrateStr, out targetAudioBitrate);

        // Для режима аудио сначала пытаемся найти audio-only дорожку именно выбранного языка/роли.
        // Если такой дорожки нет, но пользователь выбрал muxed-вариант другого языка,
        // нужно извлекать звук именно из него, а не падать обратно на русскую/original дорожку.
        string preferredAudioOnlyId = PickPreferredAudioOnlyFormatId(audioId, targetLang, audioNoteKey, targetAudioBitrate);
        string effectiveAudioId = preferredAudioOnlyId;

        if (string.IsNullOrWhiteSpace(effectiveAudioId))
        {
            if (string.Equals(audioSource, "muxed", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(audioId))
            {
                effectiveAudioId = audioId;
                audioSource = "muxed-fallback";
            }
            else
            {
                effectiveAudioId = "bestaudio";
                audioSource = "fallback";
            }
        }
        else
        {
            audioSource = "audio-only";
        }

        audioId = effectiveAudioId;
        var safeAudioId = (audioId ?? "").Replace("\"", "").Replace("'", "");

        formatArg = string.IsNullOrWhiteSpace(safeAudioId) || string.Equals(safeAudioId, "bestaudio", StringComparison.OrdinalIgnoreCase)
            ? "-f \"bestaudio/best\""
            : $"-f \"{safeAudioId}/bestaudio/best\"";
        conversionArgs = $"-x --audio-format mp3 --audio-quality {bitrateStr}K --postprocessor-args \"ExtractAudio+ffmpeg_o:-b:a {bitrateStr}k\"";
    }
    else // Режим видео
    {
        string? qualityLabel = QualityOrBitrateList.SelectedItem?.ToString() ?? "";

        int heightValue = ParseRequestedVideoHeight(qualityLabel);
        if (heightValue <= 0) heightValue = 1080;
        string height = heightValue.ToString(System.Globalization.CultureInfo.InvariantCulture);

        string ffmpegPath = Path.Combine(AppDirectory, "ffmpeg.exe");
        bool hasFfmpeg = File.Exists(ffmpegPath);

        string? muxedId = null;
        if (audioSource == "muxed")
        {
            muxedId = PickMuxedFormatId(targetLang, audioNoteKey, heightValue);
        }

        var safeAudioId = (audioId ?? "").Replace("\"", "").Replace("'", "");
        var preferredAudioOnlyId = PickPreferredAudioOnlyFormatId(safeAudioId, targetLang, audioNoteKey);
        bool hasPreferredAudioOnly = !string.IsNullOrWhiteSpace(preferredAudioOnlyId);

        if (hasFfmpeg)
        {
            if (hasPreferredAudioOnly)
            {
                formatArg = $"-f \"bv*[height<={height}]+{preferredAudioOnlyId}/bv*[height<={height}]+ba/b[height<={height}]\"";
                conversionArgs = "--merge-output-format mp4";
            }
            else if (!string.IsNullOrWhiteSpace(muxedId))
            {
                formatArg = $"-f \"{muxedId}/b[height<={height}]\"";
                conversionArgs = "--recode-video mp4";
            }
            else
            {
                formatArg = $"-f \"bv*[height<={height}]+ba/b[height<={height}]\"";
                conversionArgs = "--merge-output-format mp4";
            }
        }
        else
        {
            Log("W", "ffmpeg не найден — качество ограничено встроенными форматами");
            if (!string.IsNullOrWhiteSpace(muxedId))
            {
                formatArg = $"-f \"{muxedId}/b[height<={height}]\"";
            }
            else
            {
                formatArg = $"-f \"b[height<={height}]/best[height<={height}]/best\"";
            }
            conversionArgs = "--recode-video mp4";
        }
    }

    string jsArg = GetPreferredJsRuntimeArg();

    Log("D", $"Выбранная аудиодорожка: id={audioId}, lang={targetLang}, note={audioNoteKey}, source={audioSource}, mode={(isAudioMode ? "audio" : "video")}");

    var argsList = new List<string>();
    if (!string.IsNullOrWhiteSpace(langHeader)) argsList.Add(langHeader);
    if (!string.IsNullOrWhiteSpace(jsArg)) argsList.Add(jsArg);
    if (!string.IsNullOrWhiteSpace(formatArg)) argsList.Add(formatArg);
    if (!string.IsNullOrWhiteSpace(conversionArgs)) argsList.Add(conversionArgs);
    if (!string.IsNullOrWhiteSpace(outputOptions)) argsList.Add(outputOptions);
    argsList.Add("--newline");
    argsList.Add($"\"{UrlBox.Text}\"");

    var proc = new ProcessStartInfo
{
    // Используем полный путь к yt-dlp из вашей переменной _binPath
    FileName = Path.Combine(AppDirectory, "yt-dlp.exe"),
    Arguments = string.Join(" ", argsList),
    RedirectStandardOutput = true,
    RedirectStandardError = true,
    UseShellExecute = false,
    CreateNoWindow = true,
    StandardOutputEncoding = Encoding.UTF8,
    StandardErrorEncoding = Encoding.UTF8,
    
    // Рабочая папка — там, где лежат бинарники
    WorkingDirectory = AppDirectory
};

    // 4. ЗАПУСК
    try
    {
        // Перед скачиванием — запросим список доступных форматов и запишем в отдельный лог для диагностики
        try
        {
            string formatsLog = Path.Combine(AppPaths.LogsDirectory, $"yt-dlp-formats_{DateTime.Now:yyyyMMdd_HHmmss}.log");
            string listArgs = string.IsNullOrWhiteSpace(jsArg)
                ? $"--list-formats --no-warnings --no-playlist --no-cache-dir \"{UrlBox.Text}\""
                : $"{jsArg} --list-formats --no-warnings --no-playlist --no-cache-dir \"{UrlBox.Text}\"";
            var listProc = new ProcessStartInfo
            {
                FileName = Path.Combine(AppDirectory, "yt-dlp.exe"),
                Arguments = listArgs,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = AppDirectory
            };

            using (var pList = Process.Start(listProc))
            {
                if (pList != null)
                {
                    string outp = pList.StandardOutput.ReadToEnd();
                    string errp = pList.StandardError.ReadToEnd();
                    pList.WaitForExit(5000);
                    // Добавляем полную секцию форматов в объединённый лог без создания отдельного файла
                    SharedLogService.AppendTextSection(outp + Environment.NewLine + errp, "yt-dlp", "yt-dlp --list-formats output");
                    Log("D", $"Список форматов добавлен в {SharedLogService.CombinedLogPath}");

                    // Записываем краткую часть в общий лог для удобства (первые 30 строк)
                    var lines = outp.Split(new[] { '\r','\n' }, StringSplitOptions.RemoveEmptyEntries).Take(30);
                    foreach (var l in lines) Log("D", "Format: " + l);

                    // Анализируем наличие MP4-совместимого аудио (m4a/mp4/mp3/aac)
                    bool hasMp4Audio = outp.IndexOf(" m4a ", StringComparison.OrdinalIgnoreCase) >= 0
                        || outp.IndexOf("mp4a", StringComparison.OrdinalIgnoreCase) >= 0
                        || outp.IndexOf(" mp3 ", StringComparison.OrdinalIgnoreCase) >= 0
                        || outp.IndexOf(" aac ", StringComparison.OrdinalIgnoreCase) >= 0;

                    if (!hasMp4Audio)
                    {
                        Log("D", "Нет MP4-совместимых аудиоформатов — добавляю перекодирование аудио при слиянии (AAC)");
                        try
                        {
                            // Перед запуском yt-dlp добавляем аргументы postprocessor для принудительного перекодирования аудио в AAC
                            proc.Arguments += " --postprocessor-args \"-c:a aac -b:a 160k\"";
                            Log("D", $"Обновлённые аргументы yt-dlp: {proc.Arguments}");
                        }
                        catch (Exception ex)
                        {
                            Log("W", "Не удалось добавить postprocessor-args к вызову yt-dlp", ex);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log("W", "Не удалось получить список форматов yt-dlp", ex);
        }

        // Логируем ffmpeg/yt-dlp версии (полезно для диагностики мерджа)
        try
        {
            string ffmpegExe = Path.Combine(AppDirectory, "ffmpeg.exe");
            if (File.Exists(ffmpegExe))
            {
                var psi = new ProcessStartInfo { FileName = ffmpegExe, Arguments = "-version", RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
                using var p = Process.Start(psi);
                if (p != null)
                {
                    string fv = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                    p.WaitForExit(2000);
                    Log("D", "ffmpeg version: " + fv.Split(new[] { '\r','\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault());
                    // Добавляем содержимое ffmpeg -version в объединённый лог без создания отдельного файла
                    SharedLogService.AppendTextSection(fv, "ffmpeg", "ffmpeg -version output");
                }
            }
            else
            {
                Log("W", "ffmpeg.exe не найден рядом с приложением — слияние аудиодорожек может не выполняться");
            }
        }
        catch (Exception ex)
        {
            Log("W", "Ошибка при получении версии ffmpeg", ex);
        }

        Log("D", $"Запуск yt-dlp: {proc.FileName} {proc.Arguments}");
        // Отмечаем, что внешняя программа запущена
        SharedLogService.WriteLine("I", "=== APPLICATION yt-dlp STARTED ===", "Main", null, "yt-dlp");

        // Собираем stdout также полностью в отдельный лог (для отладки)
        string ytLogPath = Path.Combine(AppPaths.LogsDirectory, $"yt-dlp_run_{DateTime.Now:yyyyMMdd_HHmmss}.log");
        var process = Process.Start(proc);
        if (process == null) return;
        
        // Собираем stderr в буфер, чтобы логировать и анализировать
        var errSb = new System.Text.StringBuilder();
        _ = Task.Run(async () =>
        {
            try
            {
                using var errReader = process.StandardError;
                string? errLine;
                while ((errLine = await errReader.ReadLineAsync()) != null)
                {
                    errSb.AppendLine(errLine);
                    Log("D", $"yt-dlp stderr: {errLine}");
                }
            }
            catch (Exception ex)
            {
                Log("E", "Ошибка чтения stderr yt-dlp", ex);
            }
        });

        _ = Task.Run(async () =>
        {
            var allOut = new System.Text.StringBuilder();
            try
            {
                using var reader = process.StandardOutput;
                string? line;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    allOut.AppendLine(line);
                    if (line.Contains("%")) ParseProgress(line); // <--- Тут теперь вызывается новый ParseProgress
                }

                await process.WaitForExitAsync();

                // Сохраняем полный stdout в объединённый лог (будем дополнять stderr в finally)
                try { SharedLogService.AppendTextSection(allOut.ToString(), "yt-dlp", "yt-dlp stdout"); } catch (Exception ex) { Log("W", "Не удалось записать yt-dlp stdout в объединённый лог", ex); }

                // После завершения пытаемся найти сохранённый файл
                string[] matchedFiles = Array.Empty<string>();
                try
                {
                    matchedFiles = Directory.GetFiles(fullOutputPath, finalFileName + ".*");
                }
                catch { }



                string mergedFile = "";
                try
                {
                    mergedFile = matchedFiles.FirstOrDefault(f => !Regex.IsMatch(Path.GetFileName(f), @"\.f\d+\.", RegexOptions.IgnoreCase)) ?? "";
                }
                catch { }

                string downloadedFile = !string.IsNullOrEmpty(mergedFile)
                    ? mergedFile
                    : (matchedFiles.Length > 0 ? matchedFiles.OrderByDescending(f => new FileInfo(f).Length).First() : "");

                if (process.ExitCode == 0)
                {
                    if (!string.IsNullOrEmpty(mergedFile))
                    {
                        foreach (var f in matchedFiles)
                        {
                            if (Regex.IsMatch(Path.GetFileName(f), @"\.f\d+\.", RegexOptions.IgnoreCase))
                            {
                                try { File.Delete(f); } catch { }
                            }
                        }
                    }


                    // Если это режим видео — проверим, есть ли аудио дорожка в результатном файле
                    if (!isAudioMode && !string.IsNullOrEmpty(downloadedFile))
                    {
                        bool ffmpegExists = File.Exists(Path.Combine(AppDirectory, "ffmpeg.exe"));
                        bool hasAudio = true; // по умолчанию считаем, что есть

                        if (ffmpegExists)
                        {
                            hasAudio = HasAudioTrack(downloadedFile);
                        }
                        else
                        {
                            // Если ffmpeg нет — логируем рекомендацию
                            Log("W", "ffmpeg не найден, невозможно проверить наличие аудио. Рекомендуется установить ffmpeg для корректного слияния дорожек.");
                        }

                        if (!hasAudio)
                        {
                            Log("W", $"Скачанный файл {downloadedFile} не содержит аудио");
                            Log("D", $"Форматы можно проверить в файлах yt-dlp-formats_*.log в папке Logs. Полный вывод yt-dlp сохранён в {ytLogPath}");

                            // Если есть ffmpeg — попытаемся дозагрузить аудио отдельно и склеить
                            if (ffmpegExists)
                            {
                                _ = Task.Run(async () =>
                                {
                                    try
                                    {
                                        Log("I", "Попытка дозагрузить аудио (bestaudio) и склеить через ffmpeg...");
                                        bool fixedOk = await TryDownloadAndMergeAudioAsync(downloadedFile, UrlBox.Text);
                                        if (fixedOk)
                                        {
                                            Log("I", $"Аудио успешно добавлено к {downloadedFile} (файл обновлён)");
                                            Avalonia.Threading.Dispatcher.UIThread.Post(() => ShowInlineNotification("Готово", "Аудио добавлено и файл обновлён", 5));
                                        }
                                        else
                                        {
                                            Log("W", "Автоматическая попытка добавить аудио не удалась");
                                            Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                                                new ToastContentBuilder()
                                                    .AddHeader("warning", "Внимание", "")
                                                    .AddText("Скачанный файл не содержит аудио, и автоматическая попытка дозагрузить аудио не удалась. Проверьте логи.")
                                                    .Show();
                                            });
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        Log("E", "Ошибка при автоматической дозагрузке/склеивании аудио", ex);
                                    }
                                });
                            }
                            else
                            {
                                Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                                    new ToastContentBuilder()
                                        .AddHeader("warning", "Внимание", "")
                                        .AddText("Скачанный файл не содержит аудио. Проверьте логи yt-dlp (yt-dlp-formats_*.log) для списка доступных форматов.")
                                        .Show();
                                });
                            }
                        }
                        else
                        {
                            // Если аудио есть, но оно может быть в неподходящем кодеке (например opus в mp4), проверим
                            var codec = GetAudioCodec(downloadedFile);
                            var allowed = new[] { "aac", "mp4a", "mp3", "ac3" };
                            if (codec != null && !allowed.Contains(codec))
                            {
                                Log("W", $"Найден аудиокодек '{codec}', который может быть не совместим с MP4-игроками. Попытка перекодировать аудио в AAC.");
                                _ = Task.Run(async () =>
                                {
                                    try
                                    {
                                        bool ok = await ReencodeAudioToAacAsync(downloadedFile);
                                        if (ok)
                                        {
                                            Log("I", $"Успешно перекодировал аудио в AAC для {downloadedFile}");
                                            Avalonia.Threading.Dispatcher.UIThread.Post(() => ShowInlineNotification("Готово", "Аудио перекодировано в AAC", 5));
                                        }
                                        else
                                        {
                                            Log("W", "Не удалось перекодировать аудио в AAC");
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        Log("E", "Ошибка при перекодировании аудио", ex);
                                    }
                                });
                            }
                        }
                    }

                    // СКРЫВАЕМ внутренний прогресс и ШЛЕМ системное уведомление
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                        // Открываем папку с файлом
                        if (!DisableOpenFile) 
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{fullOutputPath}\"",
                UseShellExecute = true
            });
        }
        catch { }
    }
                        if (process.ExitCode == 0)
{
    // 2. ЗАМЕНЯЕМ ПРОГРЕСС БАР НА "ГОТОВО" (Fix двойных уведомлений)
             Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                 ShowDownloadCompleteToast(finalFileName, true);
                 Log("I", $"Загрузка завершена: {finalFileName}");
             });
        }
        else
        {
             Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                 ShowDownloadCompleteToast(finalFileName, false, "Код ошибки: " + process.ExitCode);
             });
        }
                    });

                    // Логируем успешное завершение
                    Log("I", $"yt-dlp успешно завершился. Выходной файл: {downloadedFile}");
                }
                else
                {
                    // СИСТЕМНАЯ ОШИБКА
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                        
                        new ToastContentBuilder()
                            .AddHeader("error", "Ошибка загрузки", "")
                            .AddText("❌ Что-то пошло не так при скачивании")
                            .AddText($"Код ошибки: {process.ExitCode}")
                            .Show();
                    });

                    // Логируем stderr для диагностики
                    Log("E", "yt-dlp завершился с кодом ошибки. stderr:\n" + errSb.ToString());
                }
            }
            catch (Exception ex)
            {
                // ОШИБКА КОДА
                Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                    new ToastContentBuilder()
                        .AddText("⚠ Критическая ошибка")
                        .AddText(ex.Message)
                        .Show();
                });
            }
            finally
            {
                // Всегда сохраняем stderr буфер в объединённый лог для диагностики
                try { SharedLogService.AppendTextSection("--- STDERR ---\n" + errSb.ToString(), "yt-dlp", "yt-dlp stderr"); } catch { }

            }
        });
    }
    catch (Exception ex)
    {
        Log("E", "Ошибка запуска yt-dlp", ex);
        ShowInlineNotification("Ошибка", ex.Message, 5);
    }
    }
    // Метод для очистки имени файла от запрещенных символов
    private string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Video";
        // Удаляем символы: \ / : * ? " < > |
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c.ToString(), "");
        }
        return name.Trim();
    }

    private bool HasAudioTrack(string filePath)
    {
        try
        {
            var ffmpeg = Path.Combine(AppDirectory, "ffmpeg.exe");
            if (!File.Exists(ffmpeg)) return false;

            var psi = new ProcessStartInfo
            {
                FileName = ffmpeg,
                Arguments = $"-i \"{filePath}\"",
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var p = Process.Start(psi);
            if (p == null) return false;
            string stderr = p.StandardError.ReadToEnd();
            p.WaitForExit();

            // Записываем в общий лог вывод ffmpeg -i для диагностики
            try
            {
                SharedLogService.AppendTextSection(stderr, "ffmpeg", "ffmpeg -i probe output");
            }
            catch { }

            return stderr.Contains("Audio:");
        }
        catch (Exception ex)
        {
            Log("E", "Ошибка проверки аудио в файле", ex);
            return false;
        }
    }

    private string? GetAudioCodec(string filePath)
    {
        try
        {
            var ffmpeg = Path.Combine(AppDirectory, "ffmpeg.exe");
            if (!File.Exists(ffmpeg)) return null;

            var psi = new ProcessStartInfo
            {
                FileName = ffmpeg,
                Arguments = $"-i \"{filePath}\"",
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var p = Process.Start(psi);
            if (p == null) return null;
            string stderr = p.StandardError.ReadToEnd();
            p.WaitForExit();

            try
            {
                SharedLogService.AppendTextSection(stderr, "ffmpeg", "ffmpeg -i probe output (codec)");
            }
            catch { }

            var m = System.Text.RegularExpressions.Regex.Match(stderr, @"Audio:\s*([^,\s]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (m.Success) return m.Groups[1].Value.ToLowerInvariant();
            return null;
        }
        catch (Exception ex)
        {
            Log("E", "Ошибка при определении кодека аудио", ex);
            return null;
        }
    }

    private async Task<bool> TryDownloadAndMergeAudioAsync(string videoFilePath, string url)
    {
        if (_isDownloadingAudio) return false;
        _isDownloadingAudio = true;
        try
        {
            string dir = Path.GetDirectoryName(videoFilePath) ?? AppPaths.DownloadsRoot;
            string baseName = Path.GetFileNameWithoutExtension(videoFilePath);

            // 1) Скачиваем bestaudio
            string audioLog = Path.Combine(AppPaths.LogsDirectory, $"yt-dlp_bestaudio_{DateTime.Now:yyyyMMdd_HHmmss}.log");
            string ytDlpFullPath = Path.Combine(AppDirectory, "yt-dlp.exe");
            string jsArg = GetPreferredJsRuntimeArg();
            string audioArgs = string.IsNullOrWhiteSpace(jsArg)
                ? $"-f \"bestaudio\" -o \"{Path.Combine(dir, baseName + ".bestaudio.%(ext)s")}\" --no-part --no-cache-dir \"{url}\""
                : $"{jsArg} -f \"bestaudio\" -o \"{Path.Combine(dir, baseName + ".bestaudio.%(ext)s")}\" --no-part --no-cache-dir \"{url}\"";
            var psiAudio = new ProcessStartInfo
            {
                FileName = Path.Combine(AppDirectory, "yt-dlp.exe"),
                WorkingDirectory = AppDirectory,
                Arguments = audioArgs,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };

            Log("D", $"Запуск yt-dlp для загрузки аудио: {psiAudio.FileName} {psiAudio.Arguments}");
            SharedLogService.WriteLine("I", "=== APPLICATION yt-dlp (bestaudio) STARTED ===", "Main", null, "yt-dlp");

            using (var p = Process.Start(psiAudio))
            {
                if (p == null) return false;
                string outp = p.StandardOutput.ReadToEnd();
                string errp = p.StandardError.ReadToEnd();
                await p.WaitForExitAsync();
                SharedLogService.AppendTextSection(outp + Environment.NewLine + errp, "yt-dlp", "yt-dlp bestaudio output");
            }

            // Найдём скачанный аудиофайл
            var audCandidates = Directory.GetFiles(dir, baseName + ".bestaudio.*");
            if (audCandidates.Length == 0)
            {
                Log("W", "Не найден файл аудио после загрузки bestaudio");
                return false;
            }

            var audioFile = audCandidates.OrderByDescending(f => new FileInfo(f).Length).First();

            // 2) Склеиваем через ffmpeg: копируем видео поток, перекодируем аудио в AAC
            string mergedTmp = Path.Combine(dir, baseName + ".merged.tmp.mp4");
            string ffLog = Path.Combine(AppPaths.LogsDirectory, $"ffmpeg_merge_{DateTime.Now:yyyyMMdd_HHmmss}.log");

            var psiMerge = new ProcessStartInfo
            {
                FileName = Path.Combine(AppDirectory, "ffmpeg.exe"),
                Arguments = $"-y -i \"{videoFilePath}\" -i \"{audioFile}\" -map 0:v:0 -map 1:a:0 -c:v copy -c:a aac -b:a 160k \"{mergedTmp}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            Log("D", $"Запуск ffmpeg для склеивания: {psiMerge.FileName} {psiMerge.Arguments}");
            using (var p = Process.Start(psiMerge))
            {
                if (p == null) return false;
                string sout = p.StandardOutput.ReadToEnd();
                string serr = p.StandardError.ReadToEnd();
                p.WaitForExit();
                SharedLogService.AppendTextSection(sout + Environment.NewLine + serr, "ffmpeg", "ffmpeg merge output");

                if (!File.Exists(mergedTmp))
                {
                    Log("W", "ffmpeg не создал объединённый файл при попытке добавить аудио");
                    return false;
                }

                // Проверяем, есть ли в mergedTmp аудио
                bool has = HasAudioTrack(mergedTmp);
                if (!has)
                {
                    Log("W", "Склеенный файл всё ещё не содержит аудио");
                    return false;
                }

                // Переименуем временный файл и заменим оригинал
                try
                {
                    File.Delete(videoFilePath);
                    File.Move(mergedTmp, videoFilePath);
                }
                catch (Exception ex)
                {
                    Log("E", "Не удалось заменить исходный файл объединённым", ex);
                }

                // Удалим временный аудиофайл
                try { File.Delete(audioFile); } catch { }

                return true;
            }
        }
        catch (Exception ex)
        {
            Log("E", "Ошибка в TryDownloadAndMergeAudioAsync", ex);
            return false;
        }
        finally
        {
            _isDownloadingAudio = false;
        }
    }

    private async Task<bool> ReencodeAudioToAacAsync(string filePath)
    {
        try
        {
            string dir = Path.GetDirectoryName(filePath) ?? AppPaths.DownloadsRoot;
            string baseName = Path.GetFileNameWithoutExtension(filePath);
            string tmp = Path.Combine(dir, baseName + ".reenc.tmp.mp4");
            string ffLog = Path.Combine(AppPaths.LogsDirectory, $"ffmpeg_reenc_{DateTime.Now:yyyyMMdd_HHmmss}.log");

            var psi = new ProcessStartInfo
            {
                FileName = Path.Combine(AppDirectory, "ffmpeg.exe"),
                Arguments = $"-y -i \"{filePath}\" -c:v copy -c:a aac -b:a 160k \"{tmp}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            Log("D", $"Запуск ffmpeg для перекодирования аудио: {psi.FileName} {psi.Arguments}");
            using (var p = Process.Start(psi))
            {
                if (p == null) return false;
                string sout = p.StandardOutput.ReadToEnd();
                string serr = p.StandardError.ReadToEnd();
                p.WaitForExit();
                SharedLogService.AppendTextSection(sout + Environment.NewLine + serr, "ffmpeg", "ffmpeg reencode audio output");

                if (!File.Exists(tmp))
                {
                    Log("W", "ffmpeg не создал временный файл при перекодировании аудио");
                    return false;
                }

                // проверка на аудио
                if (!HasAudioTrack(tmp))
                {
                    Log("W", "Перекодированный файл не содержит аудио");
                    return false;
                }

                Exception? replaceEx = null;
                string backup = Path.Combine(dir, baseName + ".reenc.bak.mp4");
                for (int attempt = 0; attempt < 20; attempt++)
                {
                    try
                    {
                        if (IsFileLocked(filePath))
                        {
                            await Task.Delay(300);
                            continue;
                        }

                        if (!File.Exists(filePath))
                        {
                            File.Move(tmp, filePath, true);
                            return true;
                        }

                        if (File.Exists(backup)) File.Delete(backup);
                        File.Replace(tmp, filePath, backup, true);
                        if (File.Exists(backup)) File.Delete(backup);
                        return true;
                    }
                    catch (Exception ex)
                    {
                        replaceEx = ex;
                        await Task.Delay(300);
                    }
                }

                Log("E", "Failed to replace file after audio re-encode", replaceEx);
                return false;
            }
        }
        catch (Exception ex)
        {
            Log("E", "Ошибка при перекодировании аудио", ex);
            return false;
        }
    }

    private string GetPreferredJsRuntimeArg()
    {
        try
        {
            string nodeLocal = Path.Combine(AppDirectory, "node.exe");
            string denoLocal = Path.Combine(AppDirectory, "deno.exe");

            if (File.Exists(nodeLocal))
                return $"--js-runtimes \"node:{NormalizeJsRuntimePath(nodeLocal)}\"";
            if (File.Exists(denoLocal))
                return $"--js-runtimes \"deno:{NormalizeJsRuntimePath(denoLocal)}\"";

            if (TryRunProcessCheck("node", "--version")) return "--js-runtimes node";
            if (TryRunProcessCheck("deno", "--version")) return "--js-runtimes deno";
        }
        catch { }
        return "";
    }

    private static string NormalizeJsRuntimePath(string path)
    {
        return path.Replace("\\", "/");
    }
    private bool TryRunProcessCheck(string fileName, string args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p == null) return false;
            string outp = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit(2000);
            return !string.IsNullOrWhiteSpace(outp);
        }
        catch { return false; }
    }
    private void ParseProgress(string line)
{
    try
    {
        // 1. Извлекаем процент
        var pctMatch = Regex.Match(line, @"(\d+(?:[.,]\d+)?)%");
        if (!pctMatch.Success) return;
        double percent = double.Parse(pctMatch.Groups[1].Value.Replace(",", "."), System.Globalization.CultureInfo.InvariantCulture);

        // 2. Извлекаем Скорость
        string speed = "---";
        var speedMatch = Regex.Match(line, @"at\s+([\d.]+[KMG]iB/s)");
        if (speedMatch.Success) 
            speed = speedMatch.Groups[1].Value.Replace("MiB/s", "МБ/с").Replace("KiB/s", "КБ/с");

        // 3. Извлекаем ETA (Время)
        string eta = "--:--";
        var etaMatch = Regex.Match(line, @"ETA\s+([\d:]+|Unknown)");
        if (etaMatch.Success)
        {
            eta = etaMatch.Groups[1].Value;
            if (eta == "Unknown") eta = "Расчет...";
        }

        // 4. Извлекаем Размер (исправленная логика)
        string sizeInfo = "";
        var sizeMatch = Regex.Match(line, @"of\s+~?([\d.]+)([KMG]iB)", RegexOptions.IgnoreCase);
        if (sizeMatch.Success)
        {
            double totalVal = double.Parse(sizeMatch.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            string unit = sizeMatch.Groups[2].Value.Replace("MiB", "МБ").Replace("GiB", "ГБ");
            // Считаем сколько скачано на основе процента
            double currentVal = (percent / 100.0) * totalVal;
            sizeInfo = $"{currentVal:F1} / {totalVal:F2} {unit}";
        }

        // 5. Обновляем UI внутри программы
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _ = UpdateInlineProgress(percent, $"Скорость: {speed} | Осталось: {eta}\n{sizeInfo}");
        });

        // 6. Обновляем Windows Toast (вызываем новый метод)
        UpdateDownloadProgressToast(percent, speed, eta, sizeInfo);
    }
    catch { }
}
}


















