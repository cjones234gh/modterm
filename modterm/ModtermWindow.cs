using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;

using Windows.UI;
using Windows.Foundation;
using Windows.Graphics;
using System.Linq;
using System.IO;
using System.Text.Json;

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
        private ControlGroup _titleBarControls = null!;
        private ControlGroup _rightButtonControls = null!;
        private TextDisplayControl _pathControl = null!;
        private TextDisplayControl _themeInfoCtrl = null!;
        private TextDisplayControl _backdropInfoCtrl = null!;
        private TextDisplayControl _opacityInfoCtrl = null!;
        private TextDisplayControl _colorInfoCtrl = null!;
        private TextDisplayControl _linesInfoCtrl = null!;
        private TextDisplayControl _columnsInfoCtrl = null!;
        private TextDisplayControl _systemBackdropBtn = null!;
        private TextDisplayControl _backdropColorBtn = null!;
        private TextDisplayControl _backdropOpacityBtn = null!;
        private TextDisplayControl _glowBtn = null!;
        private TextDisplayControl _shellSelBtn = null!;

        // user storage for configs, themes, etc.
        private string _userConfigDirectory = string.Empty;
        private string _userAppConfigPath = string.Empty;

        // user app configuration
        private UserAppConfiguration _uac;

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

            _cursorTimer = DispatcherQueue.CreateTimer();
            _resizeStopTimer = DispatcherQueue.CreateTimer();

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
                // Load user config from file
                string json = File.ReadAllText(_userAppConfigPath);
                _uac = JsonSerializer.Deserialize<UserAppConfiguration>(json) ?? _mtd.GetDefaultAppConfiguration();                
            }
            else
            {
                _uac = _mtd.GetDefaultAppConfiguration();
                // write to disk
                SaveConfig();
                UpdateTitleBarLabels();

                // write the theme configurations to disk as well so users can edit or add to them if they want
                foreach (var themeConfig in _mtd.GetAllColorConfigurations())
                {
                    string themePath = Path.Combine(_userConfigDirectory, $"theme_{themeConfig.Name}.json");
                    if (!File.Exists(themePath))
                    {
                        string themeJson = JsonSerializer.Serialize(themeConfig, new JsonSerializerOptions { WriteIndented = true });
                        File.WriteAllText(themePath, themeJson);
                    }
                }
            }
            // load the name of each theme configuration from disk so we can populate the theme menu
            // theme files are all prefixed with "theme_" so we can easily identify them and extract the theme name
            var themeFiles = Directory.GetFiles(_userConfigDirectory, "theme_*.json");
            _themeNames = themeFiles.Select(f => Path.GetFileNameWithoutExtension(f).Substring(6)).ToList();

            _uac.PropertyChanged += (s, e) => { SaveConfig(); UpdateTitleBarLabels(); };
            _uac.ThemeConfiguration.PropertyChanged += (s, e) => { SaveConfig(); UpdateTitleBarLabels(); };

            var loc = _uac.WindowLocation;
            var rectInt32 = new Windows.Graphics.RectInt32
            {
                X = (int)loc.X,
                Y = (int)loc.Y,
                Width = (int)loc.Width,
                Height = (int)loc.Height
            };
            this.AppWindow.MoveAndResize(rectInt32);

            _mtd.CurrentFont = _uac.TerminalFont;
            _mtd.CurrentControlFont = _uac.TerminalControlFont;
            _mtd.CurrentFontSize = _uac.TerminalFontSize;
            _mtd.SetColorConfiguration(_uac.ThemeConfiguration, this);

            // all modterm-style labels and flyout controls
            InitializeModtermControls();

            // window setup
            this.AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;
            this.AppWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
            this.AppWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            this.SetTitleBar(AppTitleBar);

            this.SizeChanged += MainWindow_SizeChanged;
            this.Closed += (s, e) => _terminal?.Dispose();

            RootGrid.Background = _mtd.GetBackgroundBrush();
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
            string json = JsonSerializer.Serialize(_uac, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_userAppConfigPath, json);
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
            _pathControl = new TextDisplayControl("", false);
            _themeInfoCtrl = new TextDisplayControl("", false);
            _backdropInfoCtrl = new TextDisplayControl("", false);
            _opacityInfoCtrl = new TextDisplayControl("", false);
            _colorInfoCtrl = new TextDisplayControl("", false);
            _linesInfoCtrl = new TextDisplayControl("", false);
            _columnsInfoCtrl = new TextDisplayControl("", false);

            _titleBarControls.Controls.AddRange(
                [_pathControl, _themeInfoCtrl, _backdropInfoCtrl, _opacityInfoCtrl, _colorInfoCtrl, _linesInfoCtrl, _columnsInfoCtrl]);
            _rightButtonControls.Controls.AddRange(
                [_systemBackdropBtn, _backdropOpacityBtn, _backdropColorBtn, _glowBtn, _shellSelBtn]);
        }

        private void UpdateTitleBarLabels()
        {
            // path and appearance info labels
            _pathControl.TextContent = $"Shell: {_uac.TerminalShell.Name}";
            _themeInfoCtrl.TextContent = $"Theme: {_uac.ThemeConfiguration.Name}";
            _backdropInfoCtrl.TextContent = $"System Backdrop: {_uac.ThemeConfiguration.BackdropKind.ToString()}";
            _opacityInfoCtrl.TextContent = $"Opacity: {_mtd.OpacityPct}%";
            _colorInfoCtrl.TextContent = $"Color: {_mtd.GetHexStringFromColor(_mtd.GetBackgroundBrush().Color)}";
            _linesInfoCtrl.TextContent = $"Lines: {_lines}";
            _columnsInfoCtrl.TextContent = $"Columns: {_columns}";

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
                    _uac.ThemeConfiguration = themeConfig;
                };
                themeItem.Items.Add(item);
            }
            _flyout.Items.Add(themeItem);

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
