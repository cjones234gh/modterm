
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using System.Linq;
using System.IO;
using System.Text.Json;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI;
using Windows.UI;
using Windows.Graphics;
using System.ComponentModel;
using System.Diagnostics;

namespace modterm
{
    public sealed partial class ModtermWindow : Window
    {
        // VtNetCore terminal state
        private VtNetCore.VirtualTerminal.VirtualTerminalController _vtController = null!;
        private VtNetCore.XTermParser.DataConsumer _vtDataConsumer = null!;

        // main terminal logic and state
        private ConPTYTerminal _terminal = null!;
        private int _scrollOffset = 0;
        private DispatcherQueueTimer _cursorTimer = null!;
        private DispatcherQueueTimer _resizeStopTimer = null!;
        private DispatcherQueueTimer _configReloadTimer = null!;
        // modterm UI controls
        private ControlGroup _titleBarControls = null!;
        private ControlGroup _rightButtonControls = null!;
        private TextDisplayControl _shellInfoCtrl = null!;
        private TextDisplayControl _appearanceInfoCtrl = null!;
        private TextDisplayControl _linesColsInfoCtrl = null!;
        private TextDisplayControl _systemBackdropBtn = null!;
        private TextDisplayControl _backdropColorBtn = null!;
        private TextDisplayControl _backdropOpacityBtn = null!;
        private TextDisplayControl _glowBtn = null!;
        private TextDisplayControl _shellSelBtn = null!;

        // user storage for configs, themes, etc.
        private string _userConfigDirectory = string.Empty;
        private string _userAppConfigPath = string.Empty;

        // user app configuration
        private UserAppConfiguration _uac = null!;
        private bool _saveConfiguration = true;
        private bool _showConfigLoadFailureDialog = false;
        private FileSystemWatcher _configWatcher = null!;
        private DateTime _ignoreConfigWatcherUntilUtc = DateTime.MinValue;

        // theme names from config directory
        private List<string> _themeNames = new List<string>();

        // modterm display
        private ModtermDisplay _mtd = new ModtermDisplay();

        // VT mode: cursor visibility only
        private bool _cursorVisible = true;
        private int _cursorSpeed = 500;

        // context menu flyout for right-click
        private MenuFlyout _flyout = null!;
        private SizeInt32 _lastWindowSize;
        private SizeInt32 _resizeStartSize;
        private SizeInt32 _resizeEndSize;
        private bool _isResizeSessionActive = false;
        private bool _isResizeConfirmationInProgress = false;
        private bool _suppressResizeHandling = false;

        // mouse selection state
        private bool _isSelecting = false;
        private Windows.Foundation.Point _selectionStart;
        private Windows.Foundation.Point _selectionEnd;
        private int _selectionTopRow = 0;
        private VtNetCore.VirtualTerminal.TextRange? _selectionRange;
        private string _selectedText = "";

        private void InitializeApplication()
        {
            _flyout = new MenuFlyout();
            RootGrid.Loaded += RootGrid_Loaded;

            _cursorTimer = DispatcherQueue.CreateTimer();
            _resizeStopTimer = DispatcherQueue.CreateTimer();
            _configReloadTimer = DispatcherQueue.CreateTimer();

            _terminal = new ConPTYTerminal();

            // Initialize VtNetCore terminal controller and data consumer
            _vtController = new VtNetCore.VirtualTerminal.VirtualTerminalController();
            _vtDataConsumer = new VtNetCore.XTermParser.DataConsumer(_vtController);

            _vtController.SetRgbForegroundColor(_mtd.OutputColor.R,
                _mtd.OutputColor.G, _mtd.OutputColor.B);

            // init modterm display and set default appearance config
            _mtd.Initialize();

            _userConfigDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "modterm");
            Directory.CreateDirectory(_userConfigDirectory);
            _userAppConfigPath = Path.Combine(_userConfigDirectory, "userAppConfig.json");

            if (File.Exists(_userAppConfigPath))
            {
                if (TryLoadUserConfiguration(out var loadedConfiguration))
                {
                    SetUserConfiguration(loadedConfiguration);
                }
                else
                {
                    _saveConfiguration = false;
                    _showConfigLoadFailureDialog = true;
                    SetUserConfiguration(_mtd.GetDefaultAppConfiguration());
                }
            }
            else
            {
                SetUserConfiguration(_mtd.GetDefaultAppConfiguration());
                WriteConfigurationToDisk(_uac);
                WriteDefaultThemeConfigurations(overwriteExisting: false);
            }

            InitializeConfigurationWatcher();
            LoadThemeNames();
            ApplyCurrentUserConfiguration(applyWindowBounds: true);

            // all modterm-style labels and flyout controls
            InitializeModtermControls();

            // window setup
            this.AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;
            this.AppWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
            this.AppWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            this.SetTitleBar(AppTitleBar);

            this.SizeChanged += MainWindow_SizeChanged;
            this.Closed += (s, e) =>
            {
                _configWatcher?.Dispose();
                _terminal?.Dispose();
            };

            RootGrid.KeyDown += ModtermCanvas_KeyDown;

            ModtermCanvas.Draw += this.ModtermCanvas_Draw;
            ModtermCanvas.RightTapped += this.ModtermCanvas_RightTapped;

            // Mouse support
            ModtermCanvas.PointerWheelChanged += this.ModtermCanvas_PointerWheelChanged;
            ModtermCanvas.PointerPressed += this.ModtermCanvas_PointerPressed;
            ModtermCanvas.PointerMoved += this.ModtermCanvas_PointerMoved;
            ModtermCanvas.PointerReleased += this.ModtermCanvas_PointerReleased;

            // Blinking cursor
            _cursorTimer.Interval = TimeSpan.FromMilliseconds(_cursorSpeed);
            _cursorTimer.Tick += (s, e) =>
            {
                _cursorVisible = !_cursorVisible;
                ModtermCanvas.Invalidate();
            };
            _cursorTimer.Start();

            // Detect "resize finished" by waiting for a brief pause in size events.
            _resizeStopTimer.Interval = TimeSpan.FromMilliseconds(350);
            _resizeStopTimer.Tick += async (s, e) =>
            {
                _resizeStopTimer.Stop();
                await ConfirmResizeRestartAsync();
            };

            _configReloadTimer.Interval = TimeSpan.FromMilliseconds(350);
            _configReloadTimer.Tick += async (s, e) =>
            {
                _configReloadTimer.Stop();
                await ReloadConfigurationFromDiskAsync();
            };

            this.InitializeFlyouts();
            _lastWindowSize = this.AppWindow.Size;

            // Update labels and ensure a draw pass runs so deferred ConPTY start can measure the canvas.
            UpdateTitleBarLabels();
        }

        private void SaveConfig()
        {
            if (!_saveConfiguration)
            {
                return;
            }

            WriteConfigurationToDisk(_uac);
        }

        private void WriteConfigurationToDisk(UserAppConfiguration configuration)
        {
            _ignoreConfigWatcherUntilUtc = DateTime.UtcNow.AddMilliseconds(750);
            string json = JsonSerializer.Serialize(configuration, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_userAppConfigPath, json);
            _ignoreConfigWatcherUntilUtc = DateTime.UtcNow.AddMilliseconds(750);
        }

        private void WriteDefaultThemeConfigurations(bool overwriteExisting)
        {
            foreach (var themeConfig in _mtd.GetAllColorConfigurations())
            {
                string themePath = Path.Combine(_userConfigDirectory, $"theme_{themeConfig.Name}.json");
                if (!overwriteExisting && File.Exists(themePath))
                {
                    continue;
                }

                string themeJson = JsonSerializer.Serialize(themeConfig, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(themePath, themeJson);
            }
        }

        private void LoadThemeNames()
        {
            var themeFiles = Directory.GetFiles(_userConfigDirectory, "theme_*.json");
            _themeNames = themeFiles
                .Select(f => Path.GetFileNameWithoutExtension(f).Substring(6))
                .ToList();
        }

        private void InitializeConfigurationWatcher()
        {
            _configWatcher = new FileSystemWatcher(_userConfigDirectory, Path.GetFileName(_userAppConfigPath))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime | NotifyFilters.FileName
            };

            _configWatcher.Changed += (_, __) => ScheduleConfigurationReload();
            _configWatcher.Created += (_, __) => ScheduleConfigurationReload();
            _configWatcher.Renamed += (_, __) => ScheduleConfigurationReload();
            _configWatcher.EnableRaisingEvents = true;
        }

        private void ScheduleConfigurationReload()
        {
            if (DateTime.UtcNow <= _ignoreConfigWatcherUntilUtc)
            {
                return;
            }

            DispatcherQueue.TryEnqueue(() =>
            {
                if (DateTime.UtcNow <= _ignoreConfigWatcherUntilUtc)
                {
                    return;
                }

                _configReloadTimer.Stop();
                _configReloadTimer.Start();
            });
        }

        private bool TryLoadUserConfiguration(out UserAppConfiguration configuration)
        {
            try
            {
                string json = File.ReadAllText(_userAppConfigPath);
                configuration = ValidateLoadedConfiguration(JsonSerializer.Deserialize<UserAppConfiguration>(json));
                return true;
            }
            catch
            {
                configuration = _mtd.GetDefaultAppConfiguration();
                return false;
            }
        }

        private static UserAppConfiguration ValidateLoadedConfiguration(UserAppConfiguration? configuration)
        {
            if (configuration is null)
            {
                throw new InvalidDataException("User configuration deserialized to null.");
            }

            if (configuration.TerminalShell is null)
            {
                throw new InvalidDataException("Terminal shell is missing.");
            }

            if (configuration.ThemeConfiguration is null)
            {
                throw new InvalidDataException("Theme configuration is missing.");
            }

            if (configuration.ShellConfigurations is null)
            {
                throw new InvalidDataException("Shell configurations are missing.");
            }

            return configuration;
        }

        private void SetUserConfiguration(UserAppConfiguration configuration)
        {
            if (_uac is not null)
            {
                _uac.PropertyChanged -= UserConfiguration_PropertyChanged;
                _uac.ThemeConfiguration.PropertyChanged -= ThemeConfiguration_PropertyChanged;
            }

            _uac = configuration;
            _uac.PropertyChanged += UserConfiguration_PropertyChanged;
            _uac.ThemeConfiguration.PropertyChanged += ThemeConfiguration_PropertyChanged;
        }

        private void SetThemeConfiguration(ThemeConfiguration themeConfig)
        {
            _uac.ThemeConfiguration.PropertyChanged -= ThemeConfiguration_PropertyChanged;
            _uac.ThemeConfiguration = themeConfig;
            _uac.ThemeConfiguration.PropertyChanged += ThemeConfiguration_PropertyChanged;
        }

        private void ApplyCurrentUserConfiguration(bool applyWindowBounds)
        {
            if (applyWindowBounds)
            {
                var loc = _uac.WindowLocation;
                var rectInt32 = new Windows.Graphics.RectInt32
                {
                    X = (int)loc.X,
                    Y = (int)loc.Y,
                    Width = (int)loc.Width,
                    Height = (int)loc.Height
                };
                this.AppWindow.MoveAndResize(rectInt32);
            }

            _mtd.CurrentFont = _uac.TerminalFont;
            _mtd.CurrentControlFont = _uac.TerminalControlFont;
            _mtd.CurrentFontSize = _uac.TerminalFontSize;
            _mtd.SetColorConfiguration(_uac.ThemeConfiguration, this);
            RootGrid.Background = _mtd.GetBackgroundBrush();
        }

        private void ResetDefaultConfiguration()
        {
            _saveConfiguration = true;
            SetUserConfiguration(_mtd.GetDefaultAppConfiguration());
            ApplyCurrentUserConfiguration(applyWindowBounds: false);
            InitializeModtermControls();
            RestartTerminalForLayoutChange();
            WriteConfigurationToDisk(_uac);
            WriteDefaultThemeConfigurations(overwriteExisting: true);
            LoadThemeNames();
            InitializeFlyouts();
            UpdateTitleBarLabels();
        }

        private async Task ReloadConfigurationFromDiskAsync()
        {
            if (!File.Exists(_userAppConfigPath))
            {
                return;
            }

            var previousConfiguration = _uac;
            if (TryLoadUserConfiguration(out var loadedConfiguration))
            {
                _saveConfiguration = true;
                SetUserConfiguration(loadedConfiguration);
                ApplyCurrentUserConfiguration(applyWindowBounds: true);
                InitializeModtermControls();
                InitializeFlyouts();
                UpdateTitleBarLabels();

                if (HasTerminalShellChanged(previousConfiguration, loadedConfiguration))
                {
                    RestartTerminalForLayoutChange();
                }

                return;
            }

            _saveConfiguration = false;
            SetUserConfiguration(_mtd.GetDefaultAppConfiguration());
            ApplyCurrentUserConfiguration(applyWindowBounds: true);
            InitializeModtermControls();
            InitializeFlyouts();
            UpdateTitleBarLabels();

            if (HasTerminalShellChanged(previousConfiguration, _uac))
            {
                RestartTerminalForLayoutChange();
            }

            await ShowConfigurationLoadFailureDialogAsync();
        }

        private static bool HasTerminalShellChanged(UserAppConfiguration previousConfiguration, UserAppConfiguration nextConfiguration)
        {
            return previousConfiguration.TerminalShell.Name != nextConfiguration.TerminalShell.Name ||
                previousConfiguration.TerminalShell.Path != nextConfiguration.TerminalShell.Path ||
                previousConfiguration.TerminalShell.Arguments != nextConfiguration.TerminalShell.Arguments;
        }

        private void UserConfiguration_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            SaveConfig();
            UpdateTitleBarLabels();
        }

        private void ThemeConfiguration_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            SaveConfig();
            UpdateTitleBarLabels();
        }

        private async void RootGrid_Loaded(object sender, RoutedEventArgs e)
        {
            if (!_showConfigLoadFailureDialog)
            {
                return;
            }

            _showConfigLoadFailureDialog = false;
            await ShowConfigurationLoadFailureDialogAsync();
        }

        private async Task ShowConfigurationLoadFailureDialogAsync()
        {
            if (RootGrid.XamlRoot is null)
            {
                return;
            }

            var dialog = new ContentDialog
            {
                Title = "Configuration Load Failed",
                Content = "Your configration failed to load. Using the default, any visual changes won't be saved.",
                CloseButtonText = "OK",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = RootGrid.XamlRoot
            };

            await dialog.ShowAsync();
        }

        private void OpenConfigurationInNotepad()
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "notepad.exe",
                Arguments = $"\"{_userAppConfigPath}\"",
                UseShellExecute = true
            };

            Process.Start(startInfo);
        }

        private void MainWindow_SizeChanged(object sender, WindowSizeChangedEventArgs e)
        {
            _rightButtonControls?.InvalidateExpandableChildMeasureCache();

            if (_suppressResizeHandling || _isResizeConfirmationInProgress)
            {
                _lastWindowSize = this.AppWindow.Size;
                return;
            }

            var currentSize = this.AppWindow.Size;
            if (!_isResizeSessionActive)
            {
                _isResizeSessionActive = true;
                _resizeStartSize = _lastWindowSize;
            }

            _resizeEndSize = currentSize;
            _lastWindowSize = currentSize;
            _uac.WindowLocation = new Rect(this.AppWindow.Position.X, this.AppWindow.Position.Y,
                this.AppWindow.Size.Width, this.AppWindow.Size.Height);

            _resizeStopTimer.Stop();
            _resizeStopTimer.Start();
        }

        private void InitializeModtermControls()
        {
            // ui control groups
            _titleBarControls = new ControlGroup(
                ControlGroup.ControlDock.Top, _mtd.ControlPadding);
            _rightButtonControls = new ControlGroup(
                ControlGroup.ControlDock.Right, _mtd.ControlPadding);

            // font glow
            _glowBtn = new TextDisplayControl("GLOW", true);
            var glowSubAmts = new[] { 0F, 1F, 2F, 3F, 5F, 7F, 10F, 15F };
            foreach (var s in glowSubAmts)
            {
                var item = new TextDisplayControl(s.ToString(), true);
                item.Clicked += (_, __) =>
                {
                    _mtd.BlurAmount = s;
                    _uac.ThemeConfiguration.BlurAmount = s;
                };
                _glowBtn.Children.Add(item);
            }

            // window transparency
            _backdropOpacityBtn = new TextDisplayControl("WINDOW OPACITY", true);
            for (int i = 0; i <= 10; i++)
            {
                int pct = i * 10;
                var item = new TextDisplayControl(pct.ToString(), true);
                item.Clicked += (_, __) =>
                {
                    _mtd.OpacityPct = pct;
                    _uac.ThemeConfiguration.WindowOpacityPct = pct;
                };
                _backdropOpacityBtn.Children.Add(item);
            }

            // system backdrop (Mica / Acrylic / custom blurred host backdrop)
            _systemBackdropBtn = new TextDisplayControl("SYSTEM BACKDROP", true);
            var blurredBackdropItem = new TextDisplayControl("BLURRED", true);
            blurredBackdropItem.Clicked += (_, __) =>
            {
                _uac.ThemeConfiguration.BackdropKind = BackdropKind.Blurred;
                _mtd.ApplySystemBackdrop(BackdropKind.Blurred, this);
            };
            _systemBackdropBtn.Children.Add(blurredBackdropItem);
            var micaBackdropItem = new TextDisplayControl("MICA", true);
            micaBackdropItem.Clicked += (_, __) =>
            {
                _uac.ThemeConfiguration.BackdropKind = BackdropKind.Mica;
                _mtd.ApplySystemBackdrop(BackdropKind.Mica, this);
            };
            _systemBackdropBtn.Children.Add(micaBackdropItem);
            var acrylicBackdropItem = new TextDisplayControl("ACRYLIC", true);
            acrylicBackdropItem.Clicked += (_, __) =>
            {
                _uac.ThemeConfiguration.BackdropKind = BackdropKind.Acrylic;
                _mtd.ApplySystemBackdrop(BackdropKind.Acrylic, this);
            };
            _systemBackdropBtn.Children.Add(acrylicBackdropItem);

            // window tint
            _backdropColorBtn = new TextDisplayControl("WINDOW COLOR", true);
            var colorOptions = new (string, Color)[] {
                ("Dark Blue", Colors.DarkBlue),
                ("Dark Green", Colors.DarkGreen),
                ("Dark Red", Colors.DarkRed),
                ("Dark Violet", Colors.DarkViolet),
                ("Dark Goldenrod", Colors.DarkGoldenrod),
                ("Dark Orange", Colors.DarkOrange),
                ("Deep Pink", Colors.DeepPink),
                ("Saddle Brown", Colors.SaddleBrown),
                ("Dim Gray", Colors.DimGray),
                ("White", Colors.White),
                ("Black", Colors.Black)
            };
            foreach (var (label, tint) in colorOptions)
            {
                var item = new TextDisplayControl(label, true);
                item.Clicked += (_, __) =>
                {
                    _mtd.TintColor = tint;
                    _uac.ThemeConfiguration.WindowColor = tint;
                };
                _backdropColorBtn.Children.Add(item);
            }

            // shell selection
            _shellSelBtn = new TextDisplayControl("SHELL", true);
            foreach (Shell sh in _uac.ShellConfigurations)
            {
                var item = new TextDisplayControl(sh.Name.ToUpper(), true);
                item.Clicked += async (_, __) =>
                {
                    _terminal.Started = false;
                    _terminal.Dispose();
                    await Task.Delay(1000); // Pauses for 1 second without blocking the UI thread
                    _terminal = new ConPTYTerminal();
                    _terminal.OutputReceived += OnOutputReceived;
                    _terminal.Start(sh, _lines, _columns);
                    _uac.TerminalShell = sh;
                };
                _shellSelBtn.Children.Add(item);
            }

            // path and appearance info labels
            _shellInfoCtrl = new TextDisplayControl("", false);
            _appearanceInfoCtrl = new TextDisplayControl("", false);
            _linesColsInfoCtrl = new TextDisplayControl("", false);

            _titleBarControls.Controls.AddRange(
                [_shellInfoCtrl, _appearanceInfoCtrl, _linesColsInfoCtrl]);
            _rightButtonControls.Controls.AddRange(
                [_systemBackdropBtn, _backdropOpacityBtn, _backdropColorBtn, _glowBtn, _shellSelBtn]);
        }

        private void UpdateTitleBarLabels()
        {
            // path and appearance info labels
            _shellInfoCtrl.TextContent = $"Shell: {_uac.TerminalShell.Name}";
            _appearanceInfoCtrl.TextContent = $"{_uac.ThemeConfiguration.BackdropKind.ToString()} {_mtd.OpacityPct}% {_mtd.GetHexStringFromColor(_mtd.GetBackgroundBrush().Color)}";
            _linesColsInfoCtrl.TextContent = $"{_lines}x{_columns}";

            ModtermCanvas.Invalidate();
        }

        private async Task ConfirmResizeRestartAsync()
        {
            if (!_isResizeSessionActive || _isResizeConfirmationInProgress)
                return;

            _isResizeConfirmationInProgress = true;
            try
            {
                var dialog = new ContentDialog
                {
                    Title = "Restart shell to apply resize?",
                    Content = "Resizing requires restarting the shell. Continue?",
                    PrimaryButtonText = "OK",
                    CloseButtonText = "Cancel",
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = RootGrid.XamlRoot
                };

                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                {
                    RestartTerminalForLayoutChange();
                }
                else
                {
                    _suppressResizeHandling = true;
                    this.AppWindow.Resize(_resizeStartSize);
                    _lastWindowSize = _resizeStartSize;
                    _suppressResizeHandling = false;
                }
            }
            finally
            {
                _isResizeSessionActive = false;
                _isResizeConfirmationInProgress = false;
            }
        }

        private void RestartTerminalForLayoutChange()
        {
            _terminal.Dispose();
            _terminal = new ConPTYTerminal();
            // terminal start is deferred until the first draw pass so we can measure
            ModtermCanvas.Invalidate();
        }

        private void UpdateSelectedText()
        {
            _selectionRange = null;
            _selectedText = string.Empty;

            if (_lines <= 0 || _columns <= 0 || _measuredCharWidth <= 0)
                return;

            if (Math.Abs(_selectionStart.X - _selectionEnd.X) < 2 &&
                Math.Abs(_selectionStart.Y - _selectionEnd.Y) < 2)
                return;

            _selectionRange = new VtNetCore.VirtualTerminal.TextRange
            {
                Start = GetTextPositionFromPoint(_selectionStart),
                End = GetTextPositionFromPoint(_selectionEnd)
            };

            _selectedText = _vtController.GetText(_selectionRange);
        }

        private bool IsInTextArea(Point point)
        {
            if (_lines <= 0 || _columns <= 0 || _measuredCharWidth <= 0)
                return false;

            double lineHeight = _mtd.CurrentFontSize + _lineHeightPadding;
            double textRight = _leftTextPadding + (_columns * _measuredCharWidth);
            double textBottom = _topTextPadding + (_lines * lineHeight);

            return point.X >= _leftTextPadding &&
                point.X <= textRight &&
                point.Y >= _topTextPadding &&
                point.Y <= textBottom;
        }

        private VtNetCore.VirtualTerminal.TextPosition GetTextPositionFromPoint(Point point)
        {
            double lineHeight = _mtd.CurrentFontSize + _lineHeightPadding;
            int column = (int)Math.Floor((point.X - _leftTextPadding) / _measuredCharWidth);
            int visibleRow = (int)Math.Floor((point.Y - _topTextPadding) / lineHeight);
            int topRow = _isSelecting ? _selectionTopRow : _vtController.ViewPort.TopRow - _scrollOffset;

            column = Math.Clamp(column, 0, Math.Max(0, _columns - 1));
            visibleRow = Math.Clamp(visibleRow, 0, Math.Max(0, _lines - 1));

            return new VtNetCore.VirtualTerminal.TextPosition
            {
                Column = column,
                Row = topRow + visibleRow
            };
        }

        private void CopySelectedTextToClipboard()
        {
            if (string.IsNullOrEmpty(_selectedText))
                return;

            DataPackage dataPackage = new DataPackage();
            dataPackage.SetText(_selectedText.Replace("\n", Environment.NewLine));
            Clipboard.SetContent(dataPackage);
            Clipboard.Flush();
        }

        private async void PasteFromClipboard()
        {
            var dataPackageView = Clipboard.GetContent();
            if (dataPackageView.Contains(StandardDataFormats.Text))
            {
                string text = await dataPackageView.GetTextAsync();
                if (!string.IsNullOrEmpty(text))
                {
                    _scrollOffset = 0;
                    _terminal?.WriteInput(text);
                }
                ModtermCanvas.Invalidate();
            }
        }

        private void StartConPTY()
        {
            if (_terminal.Started)
                return;

            _terminal.OutputReceived += OnOutputReceived;
            _terminal.Start(_uac.TerminalShell, _lines, _columns);

            _vtController.ResizeView(_columns, _lines);
            _terminal?.Resize((short)_columns, (short)_lines);
        }

        private void OnOutputReceived(object? sender, string line)
        {
            // Feed all output directly to the VT parser
            if (_scrollOffset > 0 && !_isSelecting) _scrollOffset = 0;
            if (!string.IsNullOrEmpty(line))
            {
                _vtDataConsumer.Write(line);
                ModtermCanvas.Invalidate();
            }
        }

        private void InitializeFlyouts()
        {
            _flyout = new MenuFlyout();

            var copyItem = new MenuFlyoutItem { Text = "Copy" };
            copyItem.Click += (_, __) =>
            {
                CopySelectedTextToClipboard();
            };
            _flyout.Items.Add(copyItem);

            var pasteItem = new MenuFlyoutItem { Text = "Paste" };
            pasteItem.Click += (_, __) => PasteFromClipboard();
            _flyout.Items.Add(pasteItem);

            _flyout.Items.Add(new MenuFlyoutSeparator());

            // theme
            var themeItem = new MenuFlyoutSubItem { Text = "Theme" };
            foreach (var preset in _themeNames)
            {
                var item = new MenuFlyoutItem { Text = preset };
                item.Click += (_, __) =>
                {
                    // deserialize the theme config from disk and apply it
                    string themePath = Path.Combine(_userConfigDirectory, $"theme_{preset}.json");
                    ThemeConfiguration themeConfig = JsonSerializer.Deserialize<ThemeConfiguration>(File.ReadAllText(themePath)) ?? _uac.ThemeConfiguration;
                    _mtd.SetColorConfiguration(themeConfig, this);
                    SetThemeConfiguration(themeConfig);
                };
                themeItem.Items.Add(item);
            }
            _flyout.Items.Add(themeItem);

            var resetDefaultsItem = new MenuFlyoutItem { Text = "Reset Default Configuration" };
            resetDefaultsItem.Click += (_, __) => ResetDefaultConfiguration();
            _flyout.Items.Add(resetDefaultsItem);

            var editConfigItem = new MenuFlyoutItem { Text = "Edit Configuration" };
            editConfigItem.Click += (_, __) => OpenConfigurationInNotepad();
            _flyout.Items.Add(editConfigItem);

            // toggle title bar controls
            var toggleTitleBarControlsItem = new MenuFlyoutItem { Text = "Toggle Title Bar Controls" };
            toggleTitleBarControlsItem.Click += (_, __) => { _showTitleBarControls = !_showTitleBarControls; ModtermCanvas.Invalidate(); };
            _flyout.Items.Add(toggleTitleBarControlsItem);

            // toggle right button controls
            var toggleControlsItem = new MenuFlyoutItem { Text = "Toggle Right Controls" };
            toggleControlsItem.Click += (_, __) => { _showRightButtonControls = !_showRightButtonControls; ModtermCanvas.Invalidate(); };
            _flyout.Items.Add(toggleControlsItem);
            
        }
    }
}
