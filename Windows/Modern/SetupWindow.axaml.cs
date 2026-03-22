using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.IO.Compression; // Для ZipArchive и ZipFile
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Collections.Generic;
using Avalonia.Layout;
using Avalonia.Media;

namespace MarsXZMedia;

public partial class SetupWindow : Window
{
    public event Action? SettingsRequested;
        private readonly SettingsView _settingsView;
        public static string SessionLogPath { get; private set; } = string.Empty;
        public bool IsInstallationSuccessful { get; private set; }

        private CancellationTokenSource? _cts;
        private bool _canCloseWindow = false; 
        private bool _isDownloadFinished = false; 
        private bool _windowAlive = true;
        private ProgressBar? MainProgressBarControl => this.FindControl<ProgressBar>("MainProgressBar");
        private bool _isInstalling = false;
        private bool _temporarilyPausedByUser = false;
        private string? _currentDownloadingName = null;
        private (string Name, string Url, string Path)? _pendingYtDlpUpdateTarget;
        // Добавь эти свойства в класс (лучше всего в начало, после других полей)
        private TextBlock? StatusTextCtrl => this.FindControl<TextBlock>("StatusText");
        private ProgressBar? ProgressBarCtrl => this.FindControl<ProgressBar>("MainProgressBar");
        private TextBlock? PercentTextCtrl => this.FindControl<TextBlock>("PercentText");
        private TextBlock? SpeedTextCtrl => this.FindControl<TextBlock>("SpeedText");
        private TextBlock? EtaTextCtrl => this.FindControl<TextBlock>("EtaText");
        private string _savedStatusText = "";
        private double _savedProgressValue = 0;
        private bool _savedIsIndeterminate = false;
        private string _savedPercentText = "";
        private string _savedSpeedText = "";
        private string _savedEtaText = "";
        private TextBlock? HeaderTitleCtrl => this.FindControl<TextBlock>("HeaderTitle");
        // Внутри класса SetupWindow добавьте статические пути:
public static string AppDirectory => AppPaths.AppDirectory;
public static string LogsFolder => AppPaths.LogsDirectory;

// --- ИЗМЕНЕНИЕ 1: Список файлов для загрузки ---
// Теперь качаем в BinFolder
private readonly List<(string Name, string Url, string Path)> _filesToDownload = new()
{
    ("yt-dlp.exe", "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp_x86.exe",
        Path.Combine(AppDirectory, "yt-dlp.exe")),

    ("ffmpeg.exe", "https://github.com/ffbinaries/ffbinaries-prebuilt/releases/download/v4.2.1/ffmpeg-4.2.1-win-32.zip",
        Path.Combine(AppDirectory, "ffmpeg.exe"))
};
public SetupWindow()
    {
        
    InitializeComponent();
        SoundService.AttachClickSound(this);
    InitSessionLog();

        try
        {
            _settingsView = new SettingsView();
            _settingsView.BackRequested += ShowSetup;

            // Подписываемся на локальное событие, чтобы показывать настройки
            this.SettingsRequested += ShowSettings;

            // Показать встроенный экран установки (Grid с x:Name="SetupGrid")
            var mc = this.FindControl<ContentControl>("MainContent");
            var sg = this.FindControl<Grid>("SetupGrid");
            if (mc != null && sg != null)
            {
                mc.Content = sg;
            }
            else
            {
                Log("W", "MainContent или SetupGrid не найдены в XAML");
            }
        }
        catch (Exception ex)
        {
            // Логируем и пробрасываем, чтобы было видно причину при старте
            Log("E", "Ошибка в конструкторе SetupWindow", ex);
            // Показываем окно сообщения, чтобы пользователь видел причину
            try { ShowErrorMessageBox("Ошибка", "Не удалось инициализировать окно установки: " + ex.Message); } catch { }
            throw;
        }



    // 1. ВАЖНО: Добавляем запуск цикла загрузки при открытии окна
        this.Loaded += async (s, e) => 
    {
        await Task.Delay(200);
        if (!EnsureElevatedForTargets(GetPendingInstallTargets().ToList(), "Для установки компонентов нужен доступ к папке рядом с программой."))
        {
            // Если запуск не повышен — закрываемся, повышенная копия продолжит
            _canCloseWindow = true;
            Close();
            return;
        }
        if (!_isInstalling) _ = Task.Run(StartDownloadLoop);
    };

    // Ваш обработчик Closing (оставляем как есть, он верный)
    this.Closing += (s, e) => { /* ... ваш код из предыдущего шага ... */ };

    // Обновлённый Closing: пауза/возобновление при попытке закрыть
    this.Closing += (s, e) =>
{
    // Если всё скачано или мы уже разрешили закрытие — не мешаем
    if (_isDownloadFinished || _canCloseWindow) return;

    // Отменяем стандартное закрытие, чтобы показать диалоги
    e.Cancel = true;

    Dispatcher.UIThread.Post(async () =>
    {
        // Сохраняем состояние UI
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _savedStatusText = StatusTextCtrl?.Text ?? "";
            _savedProgressValue = ProgressBarCtrl?.Value ?? 0;
            _savedIsIndeterminate = ProgressBarCtrl?.IsIndeterminate ?? false;
            _savedPercentText = PercentTextCtrl?.Text ?? "";
            _savedSpeedText = SpeedTextCtrl?.Text ?? "";
            _savedEtaText = EtaTextCtrl?.Text ?? "";

            // Визуально уведомляем о паузе (указываем текущий файл, если есть)
            var name = _currentDownloadingName ?? "установке компонентов";
            if (StatusTextCtrl != null) StatusTextCtrl.Text = $"Пауза: {name}...";
        });

        _temporarilyPausedByUser = true;

        // Показываем вопрос с понятными вариантами: 'Да' = остановить, 'Нет' = возобновить
        string nameForMsg = _currentDownloadingName ?? "установку компонентов";
        bool stop = await ShowConfirmAsync(
            "Прекратить установку",
            $"Установка {nameForMsg} приостановлена.\nНажмите 'Да' чтобы остановить установку или 'Нет' чтобы возобновить."
        );

        if (stop)
        {
            // Пользователь выбрал остановить
            _cts?.Cancel();
            await Task.Delay(500);
            await CleanupFileAsync();

            Log("W", "Установка прервана пользователем (через диалог остановки)");
            WriteToEventLog("MarsXZ Media", "Установка отменена пользователем", EventLogEntryType.Information);

            await Task.Run(() => { ShowErrorMessageBox("Ошибка", "Установка прервана пользователем.\nПовторите попытку ещё раз."); });

            _canCloseWindow = true;
            await Dispatcher.UIThread.InvokeAsync(() => Close());
        }
        else
        {
            // Пользователь выбрал возобновить
            _temporarilyPausedByUser = false;

            // Восстанавливаем UI
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (StatusTextCtrl != null) StatusTextCtrl.Text = _savedStatusText;
                if (ProgressBarCtrl != null)
                {
                    ProgressBarCtrl.Value = _savedProgressValue;
                    ProgressBarCtrl.IsIndeterminate = _savedIsIndeterminate;
                }
                if (PercentTextCtrl != null) PercentTextCtrl.Text = _savedPercentText;
                if (SpeedTextCtrl != null) SpeedTextCtrl.Text = _savedSpeedText;
                if (EtaTextCtrl != null) EtaTextCtrl.Text = _savedEtaText;
            });
        }
    });
};
}

        private IEnumerable<string> GetPendingInstallTargets()
        {
            foreach (var file in _filesToDownload)
            {
                if (!File.Exists(file.Path))
                    yield return file.Path;
            }

            if (_pendingYtDlpUpdateTarget.HasValue)
                yield return _pendingYtDlpUpdateTarget.Value.Path;

            if (!IsJsRuntimeAvailable())
            {
                yield return Path.Combine(AppDirectory, "node.exe");
            }
        }

        private bool EnsureElevatedForTargets(IReadOnlyCollection<string> targetPaths, string messagePrefix)
        {
            try
            {
                if (targetPaths == null || targetPaths.Count == 0)
                    return true;

                if (!InstallAccessHelper.NeedsElevationForWrite(targetPaths, out string reason, "изменения файлов рядом с программой"))
                    return true;

                if (!InstallAccessHelper.IsRunningAsAdministrator())
                {
                    Log("W", reason);
                    ShowErrorMessageBox(
                        "Требуются права администратора",
                        $"{messagePrefix}\nПапка: {AppDirectory}\n\n{reason}\n\nСейчас будет запрошен запуск от имени администратора.");

                    if (!InstallAccessHelper.TryRestartElevatedForSetup(out var elevateError))
                    {
                        Log("E", "Не удалось запросить повышение прав", elevateError);
                        ShowErrorMessageBox(
                            "Запрос администратора отклонён",
                            "Не удалось запустить установку от имени администратора. Разрешите запрос UAC и повторите попытку.");
                    }

                    return false;
                }

                ShowErrorMessageBox(
                    "Ошибка доступа",
                    $"Приложение уже запущено от имени администратора, но запись в папку всё равно недоступна:\n{AppDirectory}\n\n{reason}\n\nПроверьте атрибуты папки и ограничения безопасности.");
                return false;
            }
            catch (Exception ex)
            {
                Log("E", "Не удалось запросить права администратора", ex);
                return false;
            }
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        private void OnSettingsClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        SettingsRequested?.Invoke();
    }

        // ================= НОВАЯ ЛОГИКА ЦИКЛА ЗАГРУЗКИ =================
        private void ShowErrorMessageBox(string title, string message)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Используем предупреждение (Warning) вместо ошибки, как вы и хотели
                MessageBoxW(IntPtr.Zero, message, title, MB_OK | MB_ICONWARNING | 0x00002000);
            }
            else
            {
                Log("E", $"{title}: {message}");
            }
        }

        private void ShowSettings()
    {
        MainContent.Content = _settingsView;
    }

    private void ShowSetup()
    {
        MainContent.Content = this.FindControl<Grid>("SetupGrid");
    }
// ================= ИСПРАВЛЕННЫЙ ЦИКЛ ЗАГРУЗКИ =================
private async Task StartDownloadLoop()
{
    if (_isInstalling) return; // предотвращаем повторный запуск
    _isInstalling = true;

    // Немедленный лог и watchdog на случай подвисания
    Log("D", "StartDownloadLoop started");
    _ = Task.Run(async () =>
    {
        await Task.Delay(20000);
        if (!_isDownloadFinished && _isInstalling)
        {
            Log("W", "Watchdog: установка кажется зависшей, показывайте инструкции для повторной попытки.");
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (StatusTextCtrl != null) StatusTextCtrl.Text = "Ожидание... Перезапустите приложение или установите Node.js x86 вручную.";
            });
        }
    });

    while (!_canCloseWindow && !_isDownloadFinished)
    {
        _cts = new CancellationTokenSource();
        string errorReason = "";
        bool criticalError = false;

        // Проверяем наличие JS runtime (Node.js x86) и предлагаем автозагрузку при отсутствии
        try
        {
            Log("D", "Проверка наличия JS runtime (node) ...");
            await Dispatcher.UIThread.InvokeAsync(() => { if (StatusTextCtrl != null) StatusTextCtrl.Text = "Проверка JS runtime..."; });
            bool jsOk = await EnsureJsRuntimeAvailableAsync();
            Log("D", $"Проверка JS runtime завершена: {(jsOk ? "OK" : "NOT_FOUND")}");
            if (!jsOk) {
                Log("W", "JS runtime не найден или не установлен — некоторые форматы могут быть недоступны");
                await Dispatcher.UIThread.InvokeAsync(() => {
                    if (StatusTextCtrl != null) StatusTextCtrl.Text = "JS runtime не найден — некоторые форматы могут быть пропущены";
                });
            }
        }
        catch (Exception ex)
        {
            Log("E", "Ошибка при проверке JS runtime", ex);
        }

        // 1. ПРОВЕРКА: какие файлы отсутствуют или требуют обновления
        var pendingFiles = _filesToDownload
            .Where(f => !File.Exists(f.Path))
            .Select(f => (f.Name, f.Url, f.Path))
            .ToList();

        var ytUpdateTarget = await GetPendingYtDlpUpdateTargetAsync(_cts.Token);
        if (ytUpdateTarget.HasValue && !pendingFiles.Any(f => string.Equals(f.Path, ytUpdateTarget.Value.Path, StringComparison.OrdinalIgnoreCase)))
        {
            pendingFiles.Insert(0, ytUpdateTarget.Value);
        }

        if (!EnsureElevatedForTargets(pendingFiles.Select(f => f.Path).ToList(), "Для установки или обновления компонентов нужен доступ к папке рядом с программой."))
        {
            _canCloseWindow = true;
            await Dispatcher.UIThread.InvokeAsync(() => Close());
            return;
        }

        // Если всё уже скачано и обновление не требуется
        if (pendingFiles.Count == 0) 
        { 
            _isDownloadFinished = true;
            _canCloseWindow = true;
            IsInstallationSuccessful = true;
            await Dispatcher.UIThread.InvokeAsync(() => Close());
            return; 
        }

        // Обновляем заголовок
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (HeaderTitleCtrl != null)
            {
                HeaderTitleCtrl.Text = pendingFiles.Count > 1
                    ? "Установка компонентов..."
                    : (string.Equals(pendingFiles[0].Name, "yt-dlp.exe", StringComparison.OrdinalIgnoreCase) && File.Exists(pendingFiles[0].Path)
                        ? "Обновление yt-dlp..."
                        : $"Установка {pendingFiles[0].Name}...");
            }
        });

        try
        {
            // 2. ЗАПУСК ЗАГРУЗКИ
            Log("I", $"Начинаем установку компонентов: {string.Join(", ", pendingFiles.Select(m => m.Name))}");
            await Dispatcher.UIThread.InvokeAsync(() => {
                if (StatusTextCtrl != null) StatusTextCtrl.Text = pendingFiles.Any(f => string.Equals(f.Name, "yt-dlp.exe", StringComparison.OrdinalIgnoreCase) && File.Exists(f.Path))
                    ? "Обновляем yt-dlp до последней версии..."
                    : "Начинаем установку компонентов...";
            });

            await RunDownload(pendingFiles, _cts.Token);
            _pendingYtDlpUpdateTarget = null;

            // Если дошли сюда и не было отмены — значит успех
            _isDownloadFinished = true;
            IsInstallationSuccessful = true;
            _canCloseWindow = true;

            await Dispatcher.UIThread.InvokeAsync(() => {
                if (StatusTextCtrl != null) StatusTextCtrl.Text = "Готово!";
                if (ProgressBarCtrl != null) ProgressBarCtrl.Value = 100;
            });

            await Task.Delay(1000);
            await Dispatcher.UIThread.InvokeAsync(() => Close()); 
            return;
        }
        catch (OperationCanceledException)
        {
            Log("W", "Загрузка отменена");
            await Dispatcher.UIThread.InvokeAsync(() => { if (StatusTextCtrl != null) StatusTextCtrl.Text = "Загрузка отменена пользователем."; });
            return; // Выходим из цикла
        }
        catch (Exception ex)
        {
            errorReason = ex is HttpRequestException ? "Ошибка сети" : ex.Message;
            criticalError = true;
            Log("E", $"Error: {ex.Message}");
            await Dispatcher.UIThread.InvokeAsync(() => {
                if (StatusTextCtrl != null) StatusTextCtrl.Text = $"Ошибка: {errorReason} (см. лог). Перезапустите приложение для повторной попытки.";
            });
        }

        if (criticalError)
        {
            await CleanupFileAsync(); // Удаляем недокачанные куски
            this.Hide(); 
            await ShowErrorDialogAsync(errorReason);
            this.Show();
            await Task.Delay(500); 
        }
    }
}
        // ================= ПРОЦЕСС ЗАГРУЗКИ (ИЗМЕНЕН ПУТЬ) =================

        private async Task RunDownload(List<(string Name, string Url, string FullPath)> targets, CancellationToken token)
{
    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    client.DefaultRequestHeaders.UserAgent.ParseAdd("MarsXZ Media-Setup");

    foreach (var target in targets)
{
    bool isZip = target.Url.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
    // target.FullPath (в кортеже это Item3 - Path) уже содержит полный путь к BinFolder
    string finalExePath = target.FullPath;

    // Временный файл тоже кладем рядом в BinFolder
    string downloadPath = finalExePath + ".download";

        try 
        {
            Stopwatch sw = new Stopwatch();
            DateTime lastUiUpdate = DateTime.MinValue;
            long currentFileDownloaded = 0;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!_windowAlive) return;
                StatusTextCtrl?.SetCurrentValue(TextBlock.TextProperty, $"Подключение к {target.Name}...");
                ProgressBarCtrl?.SetCurrentValue(ProgressBar.IsIndeterminateProperty, true);
            });

            using var response = await client.GetAsync(target.Url, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            long totalBytes = response.Content.Headers.ContentLength ?? -1;

            Log("D", $"Downloading {target.Name} from {target.Url} to {finalExePath}. Size: {totalBytes}");

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                ProgressBarCtrl?.SetCurrentValue(ProgressBar.IsIndeterminateProperty, totalBytes <= 0);
                if (StatusTextCtrl != null) StatusTextCtrl.Text = $"Скачивание {target.Name}...";
            });

            // Сама загрузка
            using (var input = await response.Content.ReadAsStreamAsync(token))
            using (var output = new FileStream(downloadPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
            {
                byte[] buffer = new byte[81920];
                sw.Start();

                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    if (_temporarilyPausedByUser) { await Task.Delay(200, token); continue; }

                    int read = await input.ReadAsync(buffer, 0, buffer.Length, token);
                    if (read == 0) break;

                    await output.WriteAsync(buffer, 0, read, token);
                    currentFileDownloaded += read;

                    if ((DateTime.Now - lastUiUpdate).TotalMilliseconds < 100) continue;
                    lastUiUpdate = DateTime.Now;

                    double elapsedSec = sw.Elapsed.TotalSeconds;
                    double speedMb = elapsedSec > 0.1 ? (currentFileDownloaded / elapsedSec) / (1024.0 * 1024.0) : 0;

                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        UpdateDownloadUI(target.Name, currentFileDownloaded, totalBytes, speedMb, elapsedSec);
                    });
                }
            }
            sw.Stop();

            // Логика распаковки (только для ZIP)
            if (isZip)
            {
                await Dispatcher.UIThread.InvokeAsync(() => StatusTextCtrl?.SetCurrentValue(TextBlock.TextProperty, "Распаковка компонентов..."));
                Log("D", $"Распаковка архива для {target.Name}...");
                
                await Task.Run(() => 
                {
                    try {
                        using (ZipArchive archive = ZipFile.OpenRead(downloadPath))
                        {
                            var entry = archive.Entries.FirstOrDefault(e => e.FullName.EndsWith("ffmpeg.exe", StringComparison.OrdinalIgnoreCase));
                            if (entry != null)
                            {
                                string tempExtractPath = finalExePath + ".new";
                                entry.ExtractToFile(tempExtractPath, true);
                                ReplaceFileWithRetry(tempExtractPath, finalExePath);
                                Log("I", "FFmpeg успешно извлечен.");
                            }
                            else throw new Exception("ffmpeg.exe не найден в архиве.");
                        }
                    }
                    finally {
                        // Удаляем временный скачанный архив .download после распаковки
                        if (File.Exists(downloadPath)) File.Delete(downloadPath);
                    }
                }, token);

                // Обновляем UI после распаковки
                await Dispatcher.UIThread.InvokeAsync(() => {
                    StatusTextCtrl?.Text = $"{target.Name} установлен";
                    if (ProgressBarCtrl != null) ProgressBarCtrl.Value = 100;
                });
            }
            else
            {
                if (File.Exists(finalExePath))
                    PreDeleteToolIfExists(finalExePath);

                // Для yt-dlp просто переименовываем из .download в .exe
                ReplaceFileWithRetry(downloadPath, finalExePath);
                Log("I", $"{target.Name} успешно установлен.");
            }
        }
        catch (Exception)
        {
            // Если была отмена или ошибка — чистим только текущий недокачанный файл
            await CleanupFileAsync(downloadPath); 
            throw; 
        }
    }
}
private void UpdateDownloadUI(string name, long current, long total, double speedMb, double elapsed)
{
    if (!_windowAlive) return;

    if (total > 0)
    {
        double percent = (current * 100.0) / total;
        ProgressBarCtrl?.SetCurrentValue(ProgressBar.ValueProperty, percent);
        PercentTextCtrl?.SetCurrentValue(TextBlock.TextProperty, $"{(int)percent}%");
        
        // Расчет времени до конца (ETA)
        double bytesPerSec = current / elapsed;
        double secondsLeft = bytesPerSec > 100 ? (total - current) / bytesPerSec : 0;
        
        if (EtaTextCtrl != null)
            EtaTextCtrl.Text = secondsLeft < 60 ? $"Осталось: {secondsLeft:F0} сек" : $"Осталось: {TimeSpan.FromSeconds(secondsLeft):mm\\:ss}";
        
        StatusTextCtrl?.SetCurrentValue(TextBlock.TextProperty, $"{name}: {current / 1024 / 1024:F1} МБ / {total / 1024 / 1024:F1} МБ");
    }

    if (SpeedTextCtrl != null)
    {
        // Динамическое переключение КБ/с и МБ/с
        if (speedMb < 1.0)
            SpeedTextCtrl.Text = $"Скорость: {speedMb * 1024.0:F1} КБ/с";
        else
            SpeedTextCtrl.Text = $"Скорость: {speedMb:F1} МБ/с";
    }
}
        // ================= ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ =================

        private void ReplaceFileWithRetry(string sourcePath, string targetPath)
{
    Exception? lastError = null;

    for (int i = 0; i < 20; i++)
    {
        try
        {
            if (File.Exists(targetPath))
                File.Delete(targetPath);

            File.Move(sourcePath, targetPath);
            return;
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            lastError = ex;
            Thread.Sleep(250);
        }
    }

    throw lastError ?? new IOException($"?? ??????? ???????? ????: {Path.GetFileName(targetPath)}");
}


        private async Task CleanupFileAsync(string? filePath = null)
{
    try
    {
        await Task.Delay(300); // Даем время потокам закрыться

        if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
        {
            Log("W", $"Удаление временного/недокачанного файла: {filePath}");
            DeleteWithRetry(filePath);
        }
    }
    catch (Exception ex)
    {
        Log("E", $"Ошибка при очистке: {ex.Message}");
    }
}

private void DeleteWithRetry(string path)
{
    for (int i = 0; i < 5; i++)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
            break;
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { Thread.Sleep(250); }
    }
}
        // [ИЗМЕНЕНИЕ 2] Окно ошибки "Программа не была установлена"
        private async Task ShowErrorDialogAsync(string reason)
{
    // Используем TaskCompletionSource, чтобы "ждать" закрытия окна без ShowDialog
    var tcs = new TaskCompletionSource<bool>();

    await Dispatcher.UIThread.InvokeAsync(() =>
    {
        var errorWin = new Window
        {
            Title = "Ошибка установки",
            Width = 450, Height = 220,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterScreen, // Центрируем по экрану, т.к. родитель скрыт
            SystemDecorations = SystemDecorations.BorderOnly,
            Background = Brushes.White,
            Topmost = true // Убедимся, что окно поверх всех
        };

        var mainStack = new StackPanel { Margin = new Thickness(20) };

        // Заголовок
        mainStack.Children.Add(new TextBlock 
        { 
            Text = "Программа не была установлена", 
            FontSize = 20, 
            FontWeight = FontWeight.Bold, 
            Foreground = Brushes.DarkRed,
            Margin = new Thickness(0, 0, 0, 15)
        });

        // Причина
        mainStack.Children.Add(new TextBlock 
        { 
            Text = $"Причина: {reason}", 
            TextWrapping = TextWrapping.Wrap,
            FontSize = 14,
            Margin = new Thickness(0, 0, 0, 10),
            MaxHeight = 80
        });

         mainStack.Children.Add(new TextBlock 
        { 
            Text = "Повторите попытку ещё раз.", 
            FontSize = 14,
            FontStyle = FontStyle.Italic,
            Margin = new Thickness(0, 0, 0, 20)
        });

        // Кнопка ОК
        var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var btnOk = new Button 
        { 
            Content = "Ок", 
            Width = 100, 
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        
        // При нажатии закрываем окно
        btnOk.Click += (_, _) => errorWin.Close();
        
        // Когда окно реально закрылось — сообщаем задаче (tcs), что можно продолжать
        errorWin.Closed += (_, _) => tcs.TrySetResult(true);

        btnPanel.Children.Add(btnOk);
        mainStack.Children.Add(btnPanel);

        errorWin.Content = mainStack;

        // ВАЖНО: Используем Show(), а не ShowDialog(this)
        errorWin.Show(); 
    });

    // Ждем, пока пользователь нажмет ОК и окно закроется
    await tcs.Task;
}
        private async Task<(string Name, string Url, string Path)?> GetPendingYtDlpUpdateTargetAsync(CancellationToken token)
        {
            string ytDlpPath = Path.Combine(AppDirectory, "yt-dlp.exe");
            if (!File.Exists(ytDlpPath))
            {
                _pendingYtDlpUpdateTarget = null;
                return null;
            }

            var updateCheck = await YtDlpUpdateHelper.CheckAsync(ytDlpPath, token);
            if (!updateCheck.CheckSucceeded)
            {
                if (!string.IsNullOrWhiteSpace(updateCheck.Message))
                    Log("W", $"Проверка обновления yt-dlp пропущена: {updateCheck.Message}");
                _pendingYtDlpUpdateTarget = null;
                return null;
            }

            if (!updateCheck.IsOutdated)
            {
                Log("D", updateCheck.Message);
                _pendingYtDlpUpdateTarget = null;
                return null;
            }

            Log("I", updateCheck.Message);
            _pendingYtDlpUpdateTarget = ("yt-dlp.exe", string.IsNullOrWhiteSpace(updateCheck.DownloadUrl) ? YtDlpUpdateHelper.DefaultX86DownloadUrl : updateCheck.DownloadUrl, ytDlpPath);
            return _pendingYtDlpUpdateTarget;
        }

        private void PreDeleteToolIfExists(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            try
            {
                if (File.Exists(path))
                {
                    Log("W", $"Удаляем старый файл перед обновлением: {Path.GetFileName(path)}");
                    DeleteWithRetry(path);
                }
            }
            catch (Exception ex)
            {
                Log("E", $"Не удалось удалить старую версию файла: {path}", ex);
                throw;
            }
        }

        private async Task<bool> ShowConfirmAsync(string title, string text)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Запускаем в Task.Run, чтобы не блокировать UI поток Avalonia напрямую
                return await Task.Run(() =>
                {
                    int result = MessageBoxW(IntPtr.Zero, text, title, MB_YESNO | MB_ICONQUESTION | 0x00002000); // 0x2000 = MB_TASKMODAL
                    return result == IDYES;
                });
            }

            // Если не Windows, можно оставить старое окно на Avalonia (которое у вас было в коде)
            // Но для теста вернем true, чтобы логика сработала
            return false; 
        }
        private void WriteToEventLog(string source, string message, EventLogEntryType type)
        {
            Task.Run(() => { try { if (!EventLog.SourceExists(source)) EventLog.CreateEventSource(source, "Application"); EventLog.WriteEntry(source, message, type); } catch { } });
        }
// --- ИЗМЕНЕНИЕ 3: Инициализация логов ---
private void InitSessionLog()
{
    try {
        // Используем нашу новую переменную LogsFolder
        Directory.CreateDirectory(LogsFolder);
        SessionLogPath = Path.Combine(LogsFolder, $"session_{DateTime.Now:yyyyMMdd_HHmmss}.log");
        Log("I", $"Start Session. App Directory: {AppDirectory}");
    } catch { }
}
private void Log(string level, string message, Exception? ex = null)
{
    // 1. Запись в общий сервис
    SharedLogService.WriteLine(level, message, "Setup", ex);
}



        // ================= JS RUNTIME (Node.js x86) =================
        // --- ИЗМЕНЕНИЕ 4: Проверка JS Runtime (Node.js x86) ---
private bool IsJsRuntimeAvailable()
{
    // Проверяем наличие в нашей папке Bin
    string nodeLocal = Path.Combine(AppDirectory, "node.exe");
    // Также проверяем старый BaseDirectory на всякий случай (совместимость)
    string nodeBase = Path.Combine(AppContext.BaseDirectory, "node.exe");

    if (File.Exists(nodeLocal) || File.Exists(nodeBase)) return true;

    // PATH check...
    if (TryRunProcess("node", "--version", out _)) return true;

    return false;
}

        private bool TryRunProcess(string fileName, string args, out string output)
        {
            output = string.Empty;
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
                output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                p.WaitForExit(3000);
                var success = p.ExitCode == 0 || !string.IsNullOrWhiteSpace(output);
                if (success)
                {
                    // Логируем запуск runtime как отдельное событие
                    SharedLogService.WriteLine("I", $"=== APPLICATION {fileName} STARTED ===", "Setup", null, fileName);
                }
                return success;
            }
            catch { return false; }
        }

        private async Task<bool> EnsureJsRuntimeAvailableAsync()
        {
            try
            {
                Log("D", "EnsureJsRuntimeAvailableAsync: start");
                if (IsJsRuntimeAvailable())
                {
                    Log("D", "EnsureJsRuntimeAvailableAsync: runtime found");
                    return true;
                }

                // Предложим пользователю автозагрузку портативного Node.js x86
                
                var ask = "Для корректного извлечения форматов с YouTube требуется JavaScript runtime (Node.js x86). Попробовать скачать портативный Node.js x86 и поместить рядом с приложением?";
                bool tryAuto = await ShowConfirmAsync("JS runtime не найден", ask);
                Log("D", $"User response for auto-install Node.js x86: {tryAuto}");
                if (!tryAuto) {
                    await Dispatcher.UIThread.InvokeAsync(()=> ShowErrorMessageBox("Инструкция по установке", "Если автоматическая установка не подходит, установите Node.js LTS с https://nodejs.org/ или Node.js x86 с https://nodejs.org/ и добавьте исполняемый файл в PATH или поместите рядом с приложением."));
                    return false;
                }

                // Пробуем автозагрузку, но логируем каждый шаг
                Log("I", "Попытка автоматической загрузки Node.js x86 (portable)...");
                bool installed = await TryInstallNodePortableAsync(_cts?.Token ?? CancellationToken.None);
                Log("D", $"TryInstallNodePortableAsync result: {installed}");

                if (installed)
                {
                    Log("I", "Node.js x86 успешно установлен рядом с приложением");
                    return true;
                }
                else
                {
                    await Dispatcher.UIThread.InvokeAsync(()=> ShowErrorMessageBox("Установка не удалась", "Автоматическая загрузка Node.js x86 не удалась. Если простое скачивание и помещение исполняемого файла рядом с приложением не помогает, установите Node.js (LTS) или Node.js x86 официальным установщиком и добавьте их в PATH. Для Node вы можете использовать `winget install OpenJS.NodeJS.LTS` или официальный инсталлятор с https://nodejs.org/. Для Node.js x86 см. https://nodejs.org/. После установки перезапустите приложение."));
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log("E", "Ошибка при проверке/установке JS runtime", ex);
                return false;
            }
            finally
            {
                Log("D", "EnsureJsRuntimeAvailableAsync: end");
            }
        }

        private async Task<bool> TryInstallNodePortableAsync(CancellationToken token)
        {
            string nodeUrl = "https://nodejs.org/download/release/latest-v20.x/win-x86/node.exe";
            string tempDownload = Path.Combine(AppDirectory, "node.exe.download");
            string finalExe = Path.Combine(AppDirectory, "node.exe");

            _currentDownloadingName = Path.GetFileName(finalExe);
            Log("I", $"Downloading Node.js x86 from {nodeUrl}");
            await Dispatcher.UIThread.InvokeAsync(() => {
                if (HeaderTitleCtrl != null) HeaderTitleCtrl.Text = $"Установка {_currentDownloadingName}...";
                if (StatusTextCtrl != null) StatusTextCtrl.Text = $"Установка {_currentDownloadingName}...";
                if (PercentTextCtrl != null) PercentTextCtrl.Text = "0%";
                if (ProgressBarCtrl != null) { ProgressBarCtrl.Value = 0; ProgressBarCtrl.IsIndeterminate = false; }
            });

            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
                client.DefaultRequestHeaders.UserAgent.ParseAdd("MarsXZ Media-Setup");

                using var resp = await client.GetAsync(nodeUrl, HttpCompletionOption.ResponseHeadersRead, token);
                resp.EnsureSuccessStatusCode();

                long total = resp.Content.Headers.ContentLength ?? -1;
                Log("D", $"Node.js download response OK. Content-Length={total}");

                await using (var inStream = await resp.Content.ReadAsStreamAsync(token))
                await using (var outFile = new FileStream(tempDownload, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    byte[] buffer = new byte[81920];
                    int read;
                    long downloaded = 0;
                    var sw = Stopwatch.StartNew();
                    DateTime lastLog = DateTime.MinValue;

                    await Dispatcher.UIThread.InvokeAsync(() => {
                        ProgressBarCtrl?.SetCurrentValue(ProgressBar.IsIndeterminateProperty, total <= 0);
                        if (StatusTextCtrl != null) StatusTextCtrl.Text = $"Установка {_currentDownloadingName}...";
                    });

                    while ((read = await inStream.ReadAsync(buffer, 0, buffer.Length, token)) > 0)
                    {
                        token.ThrowIfCancellationRequested();

                        while (_temporarilyPausedByUser)
                        {
                            await Task.Delay(200, token);
                            if (token.IsCancellationRequested) token.ThrowIfCancellationRequested();
                        }

                        await outFile.WriteAsync(buffer, 0, read);
                        downloaded += read;

                        double elapsedSec = sw.Elapsed.TotalSeconds > 0.001 ? sw.Elapsed.TotalSeconds : 0.001;
                        double speedMb = (downloaded / elapsedSec) / (1024.0 * 1024.0);

                        if ((DateTime.Now - lastLog).TotalSeconds >= 1)
                        {
                            lastLog = DateTime.Now;
                            Log("D", $"Node.js downloading: {downloaded} bytes{(total > 0 ? $" / {total}" : "")}");

                            await Dispatcher.UIThread.InvokeAsync(() => {
                                UpdateDownloadUI(_currentDownloadingName ?? "node.exe", downloaded, total, speedMb, elapsedSec);
                            });
                        }
                    }
                    sw.Stop();
                    Log("I", $"Node.js downloaded: {downloaded} bytes in {sw.Elapsed.TotalSeconds:F1}s");

                    await Dispatcher.UIThread.InvokeAsync(() => {
                        UpdateDownloadUI(_currentDownloadingName ?? "node.exe", downloaded, total, (downloaded / sw.Elapsed.TotalSeconds) / (1024.0 * 1024.0), sw.Elapsed.TotalSeconds);
                    });
                }

                if (File.Exists(finalExe)) File.Delete(finalExe);
                File.Move(tempDownload, finalExe);

                Log("I", "Node.js x86 installed.");
                await Dispatcher.UIThread.InvokeAsync(() => { if (StatusTextCtrl != null) StatusTextCtrl.Text = $"{_currentDownloadingName} installed"; });

                _currentDownloadingName = null;
                return File.Exists(finalExe);
            }
            catch (Exception ex)
            {
                Log("E", "Failed to install Node.js x86", ex);
                try { if (File.Exists(tempDownload)) File.Delete(tempDownload); } catch { }
                _currentDownloadingName = null;
                return false;
            }
        }

        // Константы для Win32 API
        private const uint MB_OK = 0x00000000;
        private const uint MB_YESNO = 0x00000004;          // Кнопки Да и Нет
        private const uint MB_ICONERROR = 0x00000010;        // Иконка ошибки
        private const uint MB_ICONQUESTION = 0x00000020;     // Иконка вопроса
        private const uint MB_ICONWARNING = 0x00000030;      // Иконка предупреждения
        private const int IDYES = 6;                         // Ответ "Да"

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int MessageBoxW(IntPtr hWnd, string lpText, string lpCaption, uint uType);
    }

