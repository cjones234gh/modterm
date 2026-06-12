
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
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
        // modterm UI controls
        private DisplayLabelGroup _titleBarControls = null!;
        private DisplayLabel _shellInfoLabel = null!;
        private DisplayLabel _appearanceInfoLabel = null!;
        private DisplayLabel _linesColsInfoLabel = null!;

        // user storage for configs, themes, etc.
        private string _userConfigDirectory = string.Empty;
        private string _userAppConfigPath = string.Empty;

        // user app configuration
        private UserAppConfiguration _uac = null!;
        private bool _saveConfiguration = true;
        private bool _showConfigLoadFailureDialog = false;

        // theme names from config directory
        private List<string> _themeNames = new List<string>();

        // modterm display
        private ModtermDisplay _mtd = new ModtermDisplay();

        // VT mode: cursor visibility only
        private bool _cursorVisible = true;
        private int _cursorSpeed = 500;

        // live reload from modtermTE
        private ConfigurationReloadListener? _configurationReloadListener;
        private DispatcherQueueTimer? _configurationReloadTimer;

        // context menu flyout for right-click
        private MenuFlyout _flyout = null!;
        private SizeInt32 _lastWindowSize;
        private SizeInt32 _resizeStartSize;
        private SizeInt32 _resizeEndSize;
        private bool _isResizeSessionActive = false;
        private bool _isResizeConfirmationInProgress = false;
        private bool _suppressResizeHandling = false;
        private bool _terminalRestartInProgress = false;

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

            _terminal = CreateTerminalInstance();

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
                _configurationReloadListener?.Dispose();
                _configurationReloadListener = null;
                PersistLastWindowLocation();
                SaveConfig();
                DisposeTerminalInstance(_terminal);
            };

            _configurationReloadListener = new ConfigurationReloadListener(
                DispatcherQueue,
                RequestConfigurationReload);

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
            string json = JsonSerializer.Serialize(configuration, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_userAppConfigPath, json);
        }

        private void WriteDefaultThemeConfigurations(bool overwriteExisting)
        {
            foreach (var themeConfig in _mtd.GetAllThemeConfigurations())
            {
                WriteThemeConfigurationToDisk(themeConfig, overwriteExisting);
            }
        }

        private void WriteThemeConfigurationToDisk(ThemeConfiguration themeConfig, bool overwriteExisting = true)
        {
            string themePath = Path.Combine(_userConfigDirectory, $"theme_{themeConfig.Name}.json");
            if (!overwriteExisting && File.Exists(themePath))
            {
                return;
            }

            string themeJson = JsonSerializer.Serialize(themeConfig, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(themePath, themeJson);
        }

        private void LoadThemeNames()
        {
            var themeFiles = Directory.GetFiles(_userConfigDirectory, "theme_*.json");
            _themeNames = themeFiles
                .Select(f => Path.GetFileNameWithoutExtension(f).Substring(6))
                .ToList();
        }

        private bool TryLoadUserConfiguration(out UserAppConfiguration configuration)
        {
            try
            {
                string json = File.ReadAllText(_userAppConfigPath);
                var loadedConfiguration = JsonSerializer.Deserialize<UserAppConfiguration>(json);
                configuration = ValidateLoadedConfiguration(loadedConfiguration);
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
                var position = _uac.LastWindowLocation;
                var size = _uac.WindowSize;
                var rectInt32 = new Windows.Graphics.RectInt32
                {
                    X = (int)position.X,
                    Y = (int)position.Y,
                    Width = (int)size.Width,
                    Height = (int)size.Height
                };
                this.AppWindow.MoveAndResize(rectInt32);
            }

            _mtd.CurrentFont = _uac.TerminalFont;
            _mtd.CurrentControlFont = _uac.LabelFont;
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
            WriteDefaultThemeConfigurations(overwriteExisting: true);
            LoadThemeNames();
            InitializeFlyouts();
            UpdateTitleBarLabels();
        }

        private void RequestConfigurationReload()
        {
            _configurationReloadTimer ??= DispatcherQueue.CreateTimer();
            _configurationReloadTimer.Interval = TimeSpan.FromMilliseconds(150);
            _configurationReloadTimer.Tick -= ConfigurationReloadTimer_Tick;
            _configurationReloadTimer.Tick += ConfigurationReloadTimer_Tick;
            _configurationReloadTimer.Stop();
            _configurationReloadTimer.Start();
        }

        private async void ConfigurationReloadTimer_Tick(DispatcherQueueTimer sender, object args)
        {
            sender.Stop();
            await ReloadConfigurationFromDiskAsync();
        }

        private async Task ReloadConfigurationFromDiskAsync()
        {
            if (!File.Exists(_userAppConfigPath))
            {
                return;
            }

            var previousConfiguration = _uac;
            var currentWindowSize = new Size(this.AppWindow.Size.Width, this.AppWindow.Size.Height);
            var currentWindowLocation = new Point(this.AppWindow.Position.X, this.AppWindow.Position.Y);
            if (TryLoadUserConfiguration(out var loadedConfiguration))
            {
                _saveConfiguration = true;
                SetUserConfiguration(loadedConfiguration);
                _uac.WindowSize = currentWindowSize;
                _uac.LastWindowLocation = currentWindowLocation;
                ApplyCurrentUserConfiguration(applyWindowBounds: false);
                InitializeModtermControls();
                LoadThemeNames();
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
            _uac.WindowSize = currentWindowSize;
            _uac.LastWindowLocation = currentWindowLocation;
            ApplyCurrentUserConfiguration(applyWindowBounds: false);
            InitializeModtermControls();
            LoadThemeNames();
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
            UpdateTitleBarLabels();
        }

        private void ThemeConfiguration_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
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
            PersistWindowSize();

            _resizeStopTimer.Stop();
            _resizeStopTimer.Start();
        }

        private void PersistWindowSize()
        {
            _uac.WindowSize = new Size(
                this.AppWindow.Size.Width,
                this.AppWindow.Size.Height);
        }

        private void PersistLastWindowLocation()
        {
            if (!_saveConfiguration)
            {
                return;
            }

            _uac.LastWindowLocation = new Point(
                this.AppWindow.Position.X,
                this.AppWindow.Position.Y);
        }

        private void InitializeModtermControls()
        {
            // ui control groups
            _titleBarControls = new DisplayLabelGroup(
                DisplayLabelGroup.LabelDock.Top, _mtd.ControlPadding);

            // path and appearance info labels
            _shellInfoLabel = new DisplayLabel("", true);
            _appearanceInfoLabel = new DisplayLabel("", true);
            _linesColsInfoLabel = new DisplayLabel("", true);

            _titleBarControls.Labels.AddRange(
                [_shellInfoLabel, _appearanceInfoLabel, _linesColsInfoLabel]);
        }

        private void UpdateTitleBarLabels()
        {
            // path and appearance info labels
            _shellInfoLabel.TextContent = $"MODTERM - Shell: {_uac.TerminalShell.Name}";
            _appearanceInfoLabel.TextContent = $"Backdrop: {_uac.ThemeConfiguration.BackdropKind.ToString()} {_mtd.OpacityPct}% {_mtd.GetHexStringFromColor(_mtd.GetBackgroundBrush().Color)}";
            _linesColsInfoLabel.TextContent = $"{_lines}x{_columns}";

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
            ReplaceTerminalInstance(startImmediately: false);
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

            _terminal.Start(_uac.TerminalShell, _lines, _columns);

            _vtController.ResizeView(_columns, _lines);
            _terminal?.Resize((short)_columns, (short)_lines);
        }

        private ConPTYTerminal CreateTerminalInstance()
        {
            var terminal = new ConPTYTerminal();
            terminal.OutputReceived += OnOutputReceived;
            terminal.TerminalExited += OnTerminalExited;
            return terminal;
        }

        private void DisposeTerminalInstance(ConPTYTerminal? terminal)
        {
            if (terminal is null)
                return;

            terminal.OutputReceived -= OnOutputReceived;
            terminal.TerminalExited -= OnTerminalExited;
            terminal.Dispose();
        }

        private void ReplaceTerminalInstance(bool startImmediately, Shell? shellOverride = null)
        {
            _terminalRestartInProgress = true;
            try
            {
                var nextShell = shellOverride ?? _uac.TerminalShell;
                var previousTerminal = _terminal;
                _terminal = CreateTerminalInstance();
                DisposeTerminalInstance(previousTerminal);

                if (startImmediately)
                {
                    _terminal.Start(nextShell, _lines, _columns);
                    _vtController.ResizeView(_columns, _lines);
                    _terminal.Resize((short)_columns, (short)_lines);
                }
                else
                {
                    // terminal start is deferred until the first draw pass so we can measure
                    ModtermCanvas.Invalidate();
                }
            }
            finally
            {
                _terminalRestartInProgress = false;
            }
        }

        private Task SwitchTerminalShellAsync(Shell shell)
        {
            ReplaceTerminalInstance(startImmediately: true, shellOverride: shell);
            return Task.CompletedTask;
        }

        private void OnTerminalExited(object? sender, EventArgs e)
        {
            if (_terminalRestartInProgress || !ReferenceEquals(sender, _terminal))
                return;

            DispatcherQueue.TryEnqueue(() =>
            {
                if (_terminalRestartInProgress || !ReferenceEquals(sender, _terminal))
                    return;

                Close();
            });
        }

        private void OnOutputReceived(object? sender, byte[] data)
        {
            // Feed raw PTY bytes to the VT parser (preserves UTF-8 split across reads).
            if (_scrollOffset > 0 && !_isSelecting) _scrollOffset = 0;
            if (data is { Length: > 0 })
            {
                _vtDataConsumer.Push(data);
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

            var windowOpacityItem = new MenuFlyoutSubItem { Text = "Window Opacity" };
            for (int i = 0; i <= 10; i++)
            {
                int pct = i * 10;
                string label = pct switch
                {
                    0 => "0 (transparent)",
                    100 => "100 (opaque)",
                    _ => pct.ToString()
                };
                var item = new MenuFlyoutItem { Text = label };
                item.Click += (_, __) =>
                {
                    _mtd.OpacityPct = pct;
                    _uac.ThemeConfiguration.WindowOpacityPct = pct;
                    UpdateTitleBarLabels();
                };
                windowOpacityItem.Items.Add(item);
            }
            _flyout.Items.Add(windowOpacityItem);

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

            var reloadConfigItem = new MenuFlyoutItem { Text = "Reload Configuration" };
            reloadConfigItem.Click += async (_, __) => await ReloadConfigurationFromDiskAsync();
            _flyout.Items.Add(reloadConfigItem);
        }
    }
}
