﻿﻿﻿﻿﻿using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using System;
using System.IO;
using System.Linq;

namespace MarsXZMedia;

public partial class SettingsView : UserControl
{
    public event EventHandler? SettingsSaved;
    public event EventHandler? SettingsCanceled;
    public event Action? BackRequested;

    private string _originalVideoPath = "";
    private string _originalMusicPath = "";
    private string _originalFontChoice = "";
    private string _originalSoundTheme = "";
    private string _lastCustomVideoPath = "";
    private string _lastCustomMusicPath = "";
    private bool _hasValidationError = false;
    private bool _autoSaveDone = false;
    private bool _originalSeparatePaths = false;
    private bool _originalUseDefaultPath = false;
    private bool _originalCreateSubfolders = false;
    private bool _originalDisableOpenFile = false;
    private bool _updatingUI = false;
    private MainWindow? _hostMainWindow;
    private TextBox? _internalMaxDaysTextBox;

    private bool _originalLogAutoDeleteInfinite = false;
    private int _originalLogAutoDeleteMaxDays = 30;
    private bool _originalDisableLogs = false;
    public static bool UseDefaultPath { get; set; } = false;

    public SettingsView()
    {
        InitializeComponent();
        _originalVideoPath = MainWindow.VideoPath;
        _originalMusicPath = MainWindow.MusicPath;
        _originalFontChoice = MainWindow.FontChoice ?? "Default";
        _originalSoundTheme = MainWindow.SoundTheme ?? "None";
        _lastCustomVideoPath = string.IsNullOrWhiteSpace(MainWindow.LastCustomVideoPath)
            ? _originalVideoPath
            : MainWindow.LastCustomVideoPath;
        _lastCustomMusicPath = string.IsNullOrWhiteSpace(MainWindow.LastCustomMusicPath)
            ? _originalMusicPath
            : MainWindow.LastCustomMusicPath;

        _originalLogAutoDeleteInfinite = MainWindow.LogAutoDeleteInfinite;
        _originalLogAutoDeleteMaxDays = MainWindow.LogAutoDeleteMaxDays;
        _originalDisableLogs = MainWindow.DisableLogs;
        _originalSeparatePaths = MainWindow.SeparatePaths;
        _originalUseDefaultPath = MainWindow.UseDefaultPath;
        _originalCreateSubfolders = MainWindow.CreateSubfolders;
        _originalDisableOpenFile = MainWindow.DisableOpenFile;

        DisableLogsCheckBox?.SetCurrentValue(CheckBox.IsCheckedProperty, MainWindow.DisableLogs);
        
        InitFontAndSoundCombos();
        UpdateUI();

        // ОБНОВЛЯЕМ ГАЛОЧКИ И ТЕКСТ КАЖДЫЙ РАЗ ПРИ ОТКРЫТИИ НАСТРОЕК
        this.AttachedToVisualTree += (s, ev) => { 
            AttachMaxDaysHandlers(); 
            _hostMainWindow = TopLevel.GetTopLevel(this) as MainWindow; 
            InitFontAndSoundCombos(); 
        };
        this.DetachedFromVisualTree += (s, ev) => AutoSaveOnLeave();
    }

    // --- ОБРАБОТКА ВНЕШНЕГО ВИДА (УМНЫЕ ГАЛОЧКИ) ---
    private void InitFontAndSoundCombos()
    {
        _updatingUI = true;
        
        if (FontChoiceCheckBox != null)
        {
            bool isMonoCraft = MainWindow.FontChoice == "MonoCraft";
            FontChoiceCheckBox.IsChecked = isMonoCraft;
            FontChoiceCheckBox.Content = isMonoCraft ? "Выключить шрифт MonoCraft" : "Включить шрифт MonoCraft";
        }

        if (SoundThemeCheckBox != null)
        {
            bool isSoundOn = MainWindow.SoundTheme != "None";
            SoundThemeCheckBox.IsChecked = isSoundOn;
            SoundThemeCheckBox.Content = isSoundOn ? "Выключить звуки" : "Включить звуки";
        }

        _updatingUI = false;
    }

    private void FontChoiceChanged(object? sender, RoutedEventArgs e)
    {
        if (_updatingUI) return;
        _updatingUI = true;

        if (FontChoiceCheckBox?.IsChecked == true)
        {
            MainWindow.FontChoice = "MonoCraft";
            if (FontChoiceCheckBox != null) FontChoiceCheckBox.Content = "Выключить шрифт MonoCraft";
        }
        else
        {
            MainWindow.FontChoice = "Default";
            if (FontChoiceCheckBox != null) FontChoiceCheckBox.Content = "Включить шрифт MonoCraft";
        }

        ApplyFontChoice();      
        
        _updatingUI = false;
    }

    private void SoundThemeChanged(object? sender, RoutedEventArgs e)
    {
        if (_updatingUI) return;
        _updatingUI = true;

        if (SoundThemeCheckBox?.IsChecked == true)
        {
            MainWindow.SoundTheme = "System";
            if (SoundThemeCheckBox != null) SoundThemeCheckBox.Content = "Выключить звуки";
        }
        else
        {
            MainWindow.SoundTheme = "None";
            if (SoundThemeCheckBox != null) SoundThemeCheckBox.Content = "Включить звуки";
        }

        SoundService.ApplyTheme(MainWindow.SoundTheme);
        if (MainWindow.SoundTheme != "None") SoundService.PlayClick();
        
        _updatingUI = false;
    }

    private void ApplyFontChoice()
    {
        var app = Avalonia.Application.Current;
        if (app == null) return;
        
        try
        {
            // ПРОГРАММА САМА УЗНАЁТ СВОЁ ИМЯ (MarsXZ Media или MarsXZ Media Legacy)
            string asmName = typeof(App).Assembly.GetName().Name ?? "MarsXZ Media";
            string fontUri = $"avares://{asmName}/Assets/Fonts/Monocraft.ttf#Monocraft";

            var fontReg = MainWindow.FontChoice == "MonoCraft" 
                ? new FontFamily(fontUri) 
                : FontFamily.Default;

            // Динамически применяем глобальные ресурсы
            app.Resources["AppFont"] = fontReg;
            app.Resources["AppFontBold"] = fontReg; // Используем тот же шрифт
        }
        catch (Exception ex)
        {
            SharedLogService.WriteLine("E", "Ошибка применения шрифта", "Settings", ex);
        }
    }

    // --- КОНЕЦ БЛОКА ВНЕШНЕГО ВИДА ---

    private void CloseSettings(object? sender, RoutedEventArgs e)
    {
        SettingsCanceled?.Invoke(this, EventArgs.Empty);
        BackRequested?.Invoke();
    }

    private void UpdateUI()
    {
        if (_updatingUI) return;
        _updatingUI = true;
        try
        {
            if (VideoPathTextBox == null || MusicPathTextBox == null || UseDefaultPathCheckBox == null) return;

            string defaultDownloadPath = AppPaths.DownloadsRoot;
            bool isDefault = MainWindow.UseDefaultPath; 
            if (isDefault && MainWindow.SeparatePaths) MainWindow.SeparatePaths = false;
            UseDefaultPathCheckBox.SetCurrentValue(CheckBox.IsCheckedProperty, isDefault);
            bool isSeparate = MainWindow.SeparatePaths;

            if (isSeparate && !isDefault)
            {
                VideoPathLabel.Text = "Путь к Video:";
                MusicPanel.IsVisible = true;
            }
            else
            {
                VideoPathLabel.Text = "Путь к Video и Audio:";
                MusicPanel.IsVisible = false;
            }

            if (SeparateCheckBox != null)
            {
                SeparateCheckBox.IsEnabled = !isDefault;
                SeparateCheckBox.IsChecked = isSeparate && !isDefault;
            }

            if (!MainWindow.SeparatePaths) MainWindow.MusicPath = MainWindow.VideoPath;
            VideoPathTextBox.Text = MainWindow.VideoPath;
            MusicPathTextBox.Text = MainWindow.MusicPath;

            VideoPathTextBox.IsEnabled = !isDefault;
            if (SelectVideoPathButton != null) SelectVideoPathButton.IsEnabled = !isDefault;
            if (SeparateCheckBox != null) SeparateCheckBox.IsEnabled = !isDefault;

            if (CreateSubfoldersCheckBox != null) CreateSubfoldersCheckBox.IsChecked = !MainWindow.CreateSubfolders;
            if (MusicPanel != null) MusicPanel.IsVisible = MainWindow.SeparatePaths;
            if (DisableOpenFileCheckBox != null) DisableOpenFileCheckBox.IsChecked = MainWindow.DisableOpenFile;

            if (!MainWindow.SeparatePaths)
            {
                MusicPathTextBox.IsEnabled = false;
                MusicPathTextBox.Text = MainWindow.VideoPath;
            }
            else
            {
                MusicPathTextBox.IsEnabled = !isDefault;
            }

            try
            {
                if (MaxDeleteDaysUpDown != null)
                {
                    if (MainWindow.LogAutoDeleteInfinite)
                        MaxDeleteDaysUpDown.Value = 365m;
                    else
                        MaxDeleteDaysUpDown.Value = (decimal)MainWindow.LogAutoDeleteMaxDays;
                }
            }
            catch { }

            InfiniteKeepCheckBox?.SetCurrentValue(CheckBox.IsCheckedProperty, MainWindow.LogAutoDeleteInfinite);
            DisableLogsCheckBox?.SetCurrentValue(CheckBox.IsCheckedProperty, MainWindow.DisableLogs);

            if (MainWindow.LogAutoDeleteInfinite)
            {
                ErrorTextBlock.Text = "Авто-удаление отключено (вечное хранилище)";
                ErrorTextBlock.Foreground = Brushes.Gray;
                ErrorTextBlock.IsVisible = true;
            }

            if (ErrorTextBlock != null)
            {
                if (MainWindow.DisableLogs)
                {
                    if (MaxDeleteDaysUpDown != null) MaxDeleteDaysUpDown.IsEnabled = false;
                    if (InfiniteKeepCheckBox != null) InfiniteKeepCheckBox.IsEnabled = false;
                    ErrorTextBlock.Text = "Логи отключены. Новые записи не будут создаваться.";
                    ErrorTextBlock.Foreground = Brushes.Gray;
                    ErrorTextBlock.IsVisible = true;
                }
                else
                {
                    if (MaxDeleteDaysUpDown != null) MaxDeleteDaysUpDown.IsEnabled = !MainWindow.LogAutoDeleteInfinite;
                    if (InfiniteKeepCheckBox != null) InfiniteKeepCheckBox.IsEnabled = true;

                    if (MainWindow.LogAutoDeleteInfinite)
                    {
                        ErrorTextBlock.Text = "Вечное хранение логов включено.";
                        ErrorTextBlock.Foreground = Brushes.Gray;
                        ErrorTextBlock.IsVisible = true;
                    }
                    else
                    {
                        ErrorTextBlock.IsVisible = false;
                    }
                }
            }

            UpdatePathControls();
            AttachMaxDaysHandlers();
            ValidatePaths();
        }
        finally
        {
            _updatingUI = false;
        }
    }

    private void UpdatePathControls()
    {
        if (UseDefaultPathCheckBox == null || SeparateCheckBox == null || VideoPathTextBox == null || MusicPathTextBox == null) return;

        bool useDefault = UseDefaultPathCheckBox.IsChecked == true;
        bool separate = SeparateCheckBox.IsChecked == true && !useDefault;

        UseDefaultPathCheckBox.IsEnabled = true;
        if (CreateSubfoldersCheckBox != null) CreateSubfoldersCheckBox.IsEnabled = true;

        VideoPathTextBox.IsEnabled = !useDefault;
        if (SelectVideoPathButton != null) SelectVideoPathButton.IsEnabled = !useDefault;

        if (MusicPanel != null) MusicPanel.IsVisible = separate;
        MusicPathTextBox.IsEnabled = !useDefault && separate;
        if (SelectMusicPathButton != null) SelectMusicPathButton.IsEnabled = !useDefault && separate;

        SeparateCheckBox.IsEnabled = !useDefault;
    }

    private void ValidatePaths()
    {
        bool hasError = false;
        string errorMessage = "";
        _hasValidationError = false;
        bool useDefault = MainWindow.UseDefaultPath || IsUsingDefaultPath();

        if (MainWindow.SeparatePaths && !useDefault)
        {
            if (MainWindow.VideoPath == AppPaths.DownloadsRoot || MainWindow.MusicPath == AppPaths.DownloadsRoot)
            {
                hasError = true;
                errorMessage += "При включённом «Отдельно» пути не должны быть путями по умолчанию.\n";
            }

            if (!Directory.Exists(MainWindow.VideoPath))
            {
                VideoPathTextBox.BorderBrush = Brushes.Red;
                hasError = true;
                errorMessage += "Путь для Video не существует.\n";
            }
            else VideoPathTextBox.BorderBrush = Brushes.Gray;

            if (!Directory.Exists(MainWindow.MusicPath))
            {
                MusicPathTextBox.BorderBrush = Brushes.Red;
                hasError = true;
                errorMessage += "Путь для Audio не существует.\n";
            }
            else MusicPathTextBox.BorderBrush = Brushes.Gray;
        }
        else
        {
            if (!useDefault && !Directory.Exists(MainWindow.VideoPath))
            {
                VideoPathTextBox.BorderBrush = Brushes.Red;
                hasError = true;
                errorMessage = "Указанный путь не существует.";
            }
            else VideoPathTextBox.BorderBrush = Brushes.Gray;
        }

        _hasValidationError = hasError;

        if (InfiniteExtraText != null)
        {
            if (MainWindow.LogAutoDeleteInfinite)
            {
                InfiniteExtraText.Text = "Авто-удаление отключено (вечное хранилище)";
                InfiniteExtraText.Foreground = Brushes.Gray;
                InfiniteExtraText.IsVisible = true;
            }
            else InfiniteExtraText.IsVisible = false;
        }

        if (hasError)
        {
            ErrorTextBlock.Text = errorMessage.TrimEnd(); 
            ErrorTextBlock.Foreground = Brushes.Red;
            ErrorTextBlock.IsVisible = true;
        }
        else ErrorTextBlock.IsVisible = false;
    }

    private void SeparateChecked(object? sender, RoutedEventArgs e)
    {
        if (_updatingUI) return;
        if (MainWindow.UseDefaultPath)
        {
            MainWindow.UseDefaultPath = false;
            if (UseDefaultPathCheckBox != null) UseDefaultPathCheckBox.IsChecked = false;
            if (!string.IsNullOrWhiteSpace(_lastCustomVideoPath))
            {
                MainWindow.VideoPath = _lastCustomVideoPath;
                MainWindow.MusicPath = string.IsNullOrWhiteSpace(_lastCustomMusicPath) ? _lastCustomVideoPath : _lastCustomMusicPath;
            }
        }

        MainWindow.SeparatePaths = true;

        if (string.IsNullOrWhiteSpace(MainWindow.MusicPath) || MainWindow.MusicPath == MainWindow.VideoPath)
            MainWindow.MusicPath = string.IsNullOrWhiteSpace(_lastCustomMusicPath) ? MainWindow.VideoPath : _lastCustomMusicPath;

        UpdatePathControls();
        UpdateUI();
    }

    private void SeparateUnchecked(object? sender, RoutedEventArgs e)
    {
        if (_updatingUI) return;
        MainWindow.SeparatePaths = false;
        MainWindow.MusicPath = MainWindow.VideoPath;

        UpdatePathControls();
        UpdateUI();
    }

    private void CreateSubfoldersChecked(object? sender, RoutedEventArgs e)
    {
        if (_updatingUI) return;
        MainWindow.CreateSubfolders = false;
    }

    private void CreateSubfoldersUnchecked(object? sender, RoutedEventArgs e)
    {
        if (_updatingUI) return;
        MainWindow.CreateSubfolders = true;
    }

    private void VideoPathChanged(object? sender, TextChangedEventArgs e)
    {
        if (_updatingUI) return;
        if (VideoPathTextBox != null)
        {
            MainWindow.VideoPath = VideoPathTextBox.Text ?? MainWindow.VideoPath;
            if (!MainWindow.SeparatePaths) MainWindow.MusicPath = MainWindow.VideoPath;

            if (!MainWindow.UseDefaultPath && !IsUsingDefaultPath())
            {
                _lastCustomVideoPath = MainWindow.VideoPath;
                if (!MainWindow.SeparatePaths) _lastCustomMusicPath = MainWindow.MusicPath;
            }

            ValidatePaths();
        }
    }

    private void MusicPathChanged(object? sender, TextChangedEventArgs e)
    {
        if (_updatingUI) return;
        if (MusicPathTextBox != null)
        {
            MainWindow.MusicPath = MusicPathTextBox.Text ?? MainWindow.MusicPath;
            if (!MainWindow.UseDefaultPath && !IsUsingDefaultPath())
                _lastCustomMusicPath = MainWindow.MusicPath;
            ValidatePaths();
        }
    }

    private async void SelectVideoPath(object? sender, RoutedEventArgs e)
    {
        if (_updatingUI) return;
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage == null) return;
        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select folder for Video",
            AllowMultiple = false
        });
        if (folders.Count > 0)
        {
            var result = folders[0].Path.LocalPath;
            MainWindow.VideoPath = result;
            if (!MainWindow.SeparatePaths) MainWindow.MusicPath = result;
            if (!MainWindow.UseDefaultPath && !IsUsingDefaultPath())
            {
                _lastCustomVideoPath = MainWindow.VideoPath;
                if (!MainWindow.SeparatePaths) _lastCustomMusicPath = MainWindow.MusicPath;
            }
            UpdateUI();
        }
    }

    private async void SelectMusicPath(object? sender, RoutedEventArgs e)
    {
        if (_updatingUI) return;
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage == null) return;
        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select folder for Audio",
            AllowMultiple = false
        });
        if (folders.Count > 0)
        {
            var result = folders[0].Path.LocalPath;
            MainWindow.MusicPath = result;
            if (!MainWindow.UseDefaultPath && !IsUsingDefaultPath()) _lastCustomMusicPath = result;
            UpdateUI();
        }
    }

    private void InfiniteChecked(object? sender, RoutedEventArgs e)
    {
        if (_updatingUI) return;
        MainWindow.LogAutoDeleteInfinite = true;
        if (MaxDeleteDaysUpDown != null) MaxDeleteDaysUpDown.IsEnabled = false;
        if (InfiniteExtraText != null) InfiniteExtraText.IsVisible = true;
        UpdateUI();
    }

    private void InfiniteUnchecked(object? sender, RoutedEventArgs e)
    {
        if (_updatingUI) return;
        MainWindow.LogAutoDeleteInfinite = false;
        if (MaxDeleteDaysUpDown != null)
        {
            MaxDeleteDaysUpDown.IsEnabled = !MainWindow.DisableLogs;
            try { MaxDeleteDaysUpDown.Value = 365m; } catch { }
        }
        InfiniteKeepCheckBox.Content = "Безгранично";
        InfiniteExtraText.IsVisible = false;
        ErrorTextBlock.IsVisible = false;
    }

    private void DisableOpenFileChecked(object? sender, RoutedEventArgs e)
    {
        if (_updatingUI) return;
        MainWindow.DisableOpenFile = true;
    }

    private void DisableOpenFileUnchecked(object? sender, RoutedEventArgs e)
    {
        if (_updatingUI) return;
        MainWindow.DisableOpenFile = false;
    }

    private void UseDefaultPathChecked(object? sender, RoutedEventArgs e)
    {
        if (_updatingUI) return;
        string defaultPath = AppPaths.DownloadsRoot;

        if (!IsUsingDefaultPath())
        {
            _lastCustomVideoPath = MainWindow.VideoPath;
            _lastCustomMusicPath = MainWindow.MusicPath;
        }

        MainWindow.SeparatePaths = false;
        MainWindow.UseDefaultPath = true;
        MainWindow.VideoPath = defaultPath;
        MainWindow.MusicPath = defaultPath;

        UpdateUI();
        UpdatePathControls();
    }

    private void UseDefaultPathUnchecked(object? sender, RoutedEventArgs e)
    {
        if (_updatingUI) return;
        MainWindow.UseDefaultPath = false;

        if (!string.IsNullOrWhiteSpace(_lastCustomVideoPath))
        {
            MainWindow.VideoPath = _lastCustomVideoPath;
            MainWindow.MusicPath = string.IsNullOrWhiteSpace(_lastCustomMusicPath) ? _lastCustomVideoPath : _lastCustomMusicPath;
        }

        if (!MainWindow.SeparatePaths) MainWindow.MusicPath = MainWindow.VideoPath;

        MainWindow.VideoPath = ""; 
        MainWindow.MusicPath = "";

        UpdatePathControls();
        UpdateUI();
    }

    private void MaxDeleteDaysChanged(object? sender, RoutedEventArgs e)
    {
        if (_updatingUI) return;
        try
        {
            if (MaxDeleteDaysUpDown == null) return;
            var raw = MaxDeleteDaysUpDown.Value ?? 0m;
            int v = (int)Math.Round((double)raw);
            if (v < 0) v = 0;
            if (v > 365) v = 365;
            if (MaxDeleteDaysUpDown.Value != (decimal)v) MaxDeleteDaysUpDown.Value = (decimal)v;
            MaxDeleteDaysUpDown.BorderBrush = Brushes.Gray;
        }
        catch { }
    }

    private void MaxDeleteDaysTextInput(object? sender, Avalonia.Input.TextInputEventArgs e)
    {
        if (_updatingUI) return;
        if (string.IsNullOrEmpty(e.Text) || System.Text.RegularExpressions.Regex.IsMatch(e.Text, "^[0-9]+$")) { }
        else e.Handled = true;
    }

    private void AttachMaxDaysHandlers()
    {
        try
        {
            if (_internalMaxDaysTextBox != null)
            {
                _internalMaxDaysTextBox.TextChanged -= InternalMaxDaysTextChanged;
                _internalMaxDaysTextBox.TextChanging -= AutoDeleteDaysBox_TextChanging;
            }

            _internalMaxDaysTextBox = MaxDeleteDaysUpDown?.FindControl<TextBox>("PART_TextBox");
            if (_internalMaxDaysTextBox != null)
            {
                _internalMaxDaysTextBox.MaxLength = 3;
                _internalMaxDaysTextBox.TextChanged += InternalMaxDaysTextChanged;
                _internalMaxDaysTextBox.TextChanging += AutoDeleteDaysBox_TextChanging;
            }
        }
        catch { }
    }

    private void InternalMaxDaysTextChanged(object? sender, TextChangedEventArgs e)
    {
        try
        {
            if (sender is not TextBox tb) return;
            var text = tb.Text ?? "";
            var digits = new string(text.Where(char.IsDigit).ToArray());
            if (digits.Length > 3) digits = digits.Substring(0, 3);
            if (digits != text)
            {
                var pos = tb.CaretIndex;
                tb.Text = digits;
                tb.CaretIndex = Math.Min(pos, digits.Length);
            }

            if (MaxDeleteDaysUpDown == null) return;
            if (string.IsNullOrEmpty(digits))
            {
                MaxDeleteDaysUpDown.Value = 0m;
                return;
            }

            if (int.TryParse(digits, out var v))
            {
                if (v < 0) v = 0;
                if (v > 365) v = 365;
                if (MaxDeleteDaysUpDown.Value != (decimal)v) MaxDeleteDaysUpDown.Value = (decimal)v;
            }
        }
        catch { }
    }

    private void AutoDeleteDaysBox_TextChanging(object? sender, TextChangingEventArgs e)
    {
        try
        {
            if (sender is TextBox tb)
            {
                var text = tb.Text ?? "";
                var digits = new string(text.Where(char.IsDigit).ToArray());
                if (digits.Length > 3) digits = digits.Substring(0, 3);
                if (digits != text)
                {
                    var pos = tb.CaretIndex;
                    tb.Text = digits;
                    tb.CaretIndex = Math.Min(pos, digits.Length);
                }
            }
        }
        catch { }
    }

    private bool IsUsingDefaultPath()
    {
        if (string.IsNullOrWhiteSpace(MainWindow.VideoPath)) return false;
        return MainWindow.VideoPath == AppPaths.DownloadsRoot && MainWindow.MusicPath == AppPaths.DownloadsRoot;
    }

    private void AutoSaveOnLeave()
    {
        if (_autoSaveDone) return;
        _autoSaveDone = true;
        ApplySettings(showNotification: true);
    }

    private bool ApplySettings(bool showNotification)
    {
        ValidatePaths();
        if (_hasValidationError)
        {
            if (showNotification) NotifyHost("\u041d\u0430\u0441\u0442\u0440\u043e\u0439\u043a\u0438", "\u041d\u0435 \u0443\u0434\u0430\u043b\u043e\u0441\u044c \u0441\u043e\u0445\u0440\u0430\u043d\u0438\u0442\u044c: \u043f\u0440\u043e\u0432\u0435\u0440\u044c\u0442\u0435 \u043f\u0443\u0442\u0438", 4);
            return false;
        }

        bool infinite = InfiniteKeepCheckBox?.IsChecked == true;
        MainWindow.LogAutoDeleteInfinite = infinite;

        if (!infinite)
        {
            if (MaxDeleteDaysUpDown?.Value == null)
            {
                if (showNotification) NotifyHost("\u041d\u0430\u0441\u0442\u0440\u043e\u0439\u043a\u0438", "\u041d\u0435 \u0443\u0434\u0430\u043b\u043e\u0441\u044c \u0441\u043e\u0445\u0440\u0430\u043d\u0438\u0442\u044c: \u0443\u043a\u0430\u0436\u0438\u0442\u0435 \u0441\u0440\u043e\u043a \u0445\u0440\u0430\u043d\u0435\u043d\u0438\u044f \u043b\u043e\u0433\u043e\u0432", 4);
                return false;
            }
            var maxDays = (int)MaxDeleteDaysUpDown.Value.Value;
            if (maxDays < 0)
            {
                if (showNotification) NotifyHost("\u041d\u0430\u0441\u0442\u0440\u043e\u0439\u043a\u0438", "\u041d\u0435 \u0443\u0434\u0430\u043b\u043e\u0441\u044c \u0441\u043e\u0445\u0440\u0430\u043d\u0438\u0442\u044c: \u043d\u0435\u0432\u0435\u0440\u043d\u044b\u0439 \u0441\u0440\u043e\u043a \u0445\u0440\u0430\u043d\u0435\u043d\u0438\u044f", 4);
                return false;
            }
            MainWindow.LogAutoDeleteMaxDays = maxDays;
        }

        bool logDaysChanged =
            !MainWindow.LogAutoDeleteInfinite &&
            !_originalLogAutoDeleteInfinite &&
            _originalLogAutoDeleteMaxDays != MainWindow.LogAutoDeleteMaxDays;

        bool changed =
            _originalFontChoice != MainWindow.FontChoice ||
            _originalSoundTheme != MainWindow.SoundTheme ||
            _originalVideoPath != MainWindow.VideoPath ||
            _originalMusicPath != MainWindow.MusicPath ||
            _originalLogAutoDeleteInfinite != MainWindow.LogAutoDeleteInfinite ||
            logDaysChanged ||
            _originalDisableLogs != MainWindow.DisableLogs ||
            _originalSeparatePaths != MainWindow.SeparatePaths ||
            _originalUseDefaultPath != MainWindow.UseDefaultPath ||
            _originalCreateSubfolders != MainWindow.CreateSubfolders ||
            _originalDisableOpenFile != MainWindow.DisableOpenFile;

        _originalVideoPath = MainWindow.VideoPath;
        _originalMusicPath = MainWindow.MusicPath;
        _originalFontChoice = MainWindow.FontChoice ?? "Default";
        _originalSoundTheme = MainWindow.SoundTheme ?? "None";
        _originalLogAutoDeleteInfinite = MainWindow.LogAutoDeleteInfinite;
        _originalLogAutoDeleteMaxDays = MainWindow.LogAutoDeleteMaxDays;
        _originalDisableLogs = MainWindow.DisableLogs;
        _originalSeparatePaths = MainWindow.SeparatePaths;
        _originalUseDefaultPath = MainWindow.UseDefaultPath;
        _originalCreateSubfolders = MainWindow.CreateSubfolders;
        _originalDisableOpenFile = MainWindow.DisableOpenFile;
        MainWindow.LastCustomVideoPath = _lastCustomVideoPath;
        MainWindow.LastCustomMusicPath = _lastCustomMusicPath;

        if (changed)
        {
            AppSettingsStore.Save(AppSettingsStore.FromMainWindow());
            SharedLogService.WriteLine("I", "\u041d\u0430\u0441\u0442\u0440\u043e\u0439\u043a\u0438 \u0431\u044b\u043b\u0438 \u0438\u0437\u043c\u0435\u043d\u0435\u043d\u044b, \u0441\u043e\u0437\u0434\u0430\u043d \u0444\u0430\u0439\u043b settings.json", "Settings");
            SoundService.PlayApply();
        }

        if (showNotification && changed)
        {
            NotifyHost("\u041d\u0430\u0441\u0442\u0440\u043e\u0439\u043a\u0438", "\u041d\u0430\u0441\u0442\u0440\u043e\u0439\u043a\u0438 \u0438\u0437\u043c\u0435\u043d\u0435\u043d\u044b \u0438 \u0441\u043e\u0445\u0440\u0430\u043d\u0435\u043d\u044b", 3, silent: true);
        }

        return true;
    }

    private void NotifyHost(string title, string message, int seconds, bool silent = false)
    {
        var mw = _hostMainWindow ?? TopLevel.GetTopLevel(this) as MainWindow;
        if (mw != null)
        {
            mw.ShowInlineNotification(title, message, seconds, silent);
        }
    }

    private async void ExportLogs(object? sender, RoutedEventArgs e)
    {
        if (_updatingUI) return;
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage == null) return;
        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select folder for logs",
            AllowMultiple = false
        });
        if (folders.Count == 0) return;

        try
        {
            string logPath = SharedLogService.CombinedLogPath;
            if (!File.Exists(logPath))
            {
                ErrorTextBlock.Text = "Log file not found";
                ErrorTextBlock.Foreground = Brushes.Red;
                ErrorTextBlock.IsVisible = true;
                return;
            }

            var destFolder = folders[0].Path.LocalPath;
            var destPath = Path.Combine(destFolder, $"Mars_Logs_{DateTime.Now:yyyyMMdd_HHmm}.log");
            using (var sourceStream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var destStream = new FileStream(destPath, FileMode.Create, FileAccess.Write))
            {
                await sourceStream.CopyToAsync(destStream);
            }

            ErrorTextBlock.Text = $"Logs exported: {Path.GetFileName(destPath)}";
            ErrorTextBlock.Foreground = Brushes.Green;
            ErrorTextBlock.IsVisible = true;
        }
        catch (Exception ex)
        {
            ErrorTextBlock.Text = "Export error: " + ex.Message;
            ErrorTextBlock.Foreground = Brushes.Red;
            ErrorTextBlock.IsVisible = true;
        }
    }

    private void DisableLogsChecked(object? sender, RoutedEventArgs e)
    {
        if (_updatingUI) return;
        MainWindow.DisableLogs = true;
        if (MaxDeleteDaysUpDown != null) MaxDeleteDaysUpDown.IsEnabled = false;
        if (InfiniteKeepCheckBox != null) InfiniteKeepCheckBox.IsEnabled = false;

        ErrorTextBlock.Text = "Логи отключены. Существующие файлы логов не будут удалены, но новые записи не будут создаваться.";
        ErrorTextBlock.Foreground = Brushes.Gray;
        ErrorTextBlock.IsVisible = true;
    }

    private void DisableLogsUnchecked(object? sender, RoutedEventArgs e)
    {
        if (_updatingUI) return;
        MainWindow.DisableLogs = false;
        if (MaxDeleteDaysUpDown != null) MaxDeleteDaysUpDown.IsEnabled = !MainWindow.LogAutoDeleteInfinite;
        if (InfiniteKeepCheckBox != null) InfiniteKeepCheckBox.IsEnabled = true;
        ErrorTextBlock.IsVisible = false;
    }

    private void SaveSettings(object? sender, RoutedEventArgs e)
    {
        if (!ApplySettings(showNotification: true)) return;
        _autoSaveDone = true;
        SettingsSaved?.Invoke(this, EventArgs.Empty);
        BackRequested?.Invoke();
    }

    private void OpenAbout(object? sender, RoutedEventArgs e)
    {
        var owner = TopLevel.GetTopLevel(this) as Window;
        var win = new AboutWindow();
        if (owner != null)
            win.ShowDialog(owner);
        else
            win.Show();
    }
}