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
        private ConPTYTerminal  _terminal = null!;
        private Shell           _currentShell = new Shell();
        private int             _scrollOffset = 0;
        private DispatcherQueueTimer _cursorTimer = null!;
        private DispatcherQueueTimer _resizeStopTimer = null!;
        // modterm UI controls
        private ControlGroup            _titleBarControls = null!;
        private ControlGroup            _rightButtonControls = null!;
        private TextDisplayControl      _pathControl = null!;
        private TextDisplayControl      _appearanceInfoControl = null!;
        private TextDisplayControl      _autoThemeBtn = null!;
        private int                     _autoThemeIndex = 0;
        private TextDisplayControl      _systemBackdropBtn = null!;
        private TextDisplayControl      _backdropColorBtn = null!;
        private TextDisplayControl      _backdropOpacityBtn = null!;
        private TextDisplayControl      _fontFamilyBtn = null!;
        private TextDisplayControl      _fontSizeBtn = null!;
        private TextDisplayControl      _glowBtn = null!;
        private TextDisplayControl      _shellSelBtn = null!;

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
        private bool            _cursorVisible = true;
        private int             _cursorSpeed = 500;

        // context menu flyout for right-click
        private MenuFlyout _flyout = null!;
        private SizeInt32 _lastWindowSize;
        private SizeInt32 _resizeStartSize;
        private SizeInt32 _resizeEndSize;
        private bool _isResizeSessionActive = false;
        private bool _isResizeConfirmationInProgress = false;
        private bool _suppressResizeHandling = false;

        // shell definitions - TODO: move to config and add more options like env vars, starting dir, etc.
        private static string _conargs = "--headless --width [W] --height [H] -- "; 
        private List<Shell> _shellEnv = new List<Shell>()
        {
            new Shell { Name = "cmd", Path = "conhost", Arguments = _conargs + "C:\\Windows\\System32\\cmd.exe" },
            new Shell { Name = "powershell", Path = "conhost", Arguments = _conargs + "C:\\Windows\\System32\\WindowsPowerShell\\v1.0\\powershell.exe" },
            new Shell { Name = "bash", Path = "conhost", Arguments = _conargs + "C:\\Program Files\\Git\\bin\\bash.exe" },
            new Shell { Name = "wsl", Path = "conhost", Arguments = _conargs + "wsl.exe" }, 
        };

        // mouse selection state
        private bool _isSelecting = false;
        private Windows.Foundation.Point _selectionStart;
        private Windows.Foundation.Point _selectionEnd;
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

                // load the name of each theme configuration from disk so we can populate the theme menu
                // theme files are all prefixed with "theme_" so we can easily identify them and extract the theme name
                var themeFiles = Directory.GetFiles(_userConfigDirectory, "theme_*.json");
                    _themeNames = themeFiles.Select(f => Path.GetFileNameWithoutExtension(f).Substring(6)).ToList();
            }
            else
            {
                _uac = _mtd.GetDefaultAppConfiguration();
                // write to disk
                SaveConfig();

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

            _uac.PropertyChanged += (s, e) =>
            {
                SaveConfig();
            };
            _uac.ThemeConfiguration.PropertyChanged += (s, e) =>
            {
                SaveConfig();
            };

            _currentShell = _uac.TerminalShell;
            _mtd.CurrentFont = new FontFamily(_uac.TerminalFont);
            _mtd.CurrentControlFont = new FontFamily(_uac.TerminalControlFont);
            _mtd.CurrentFontSize = _uac.TerminalFontSize;
            _mtd.SetColorConfiguration(_uac.ThemeConfiguration);
            _mtd.ApplySystemBackdrop(_uac.ThemeConfiguration.BackdropKind, this);

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

            // Ensure a draw pass runs so deferred ConPTY start can measure the canvas.
            ModtermCanvas.Invalidate();
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

            // path and appearance info labels
            _pathControl = new TextDisplayControl(_currentShell.Name, false);
            _appearanceInfoControl = new TextDisplayControl(_mtd.GetAppearanceInfo(_lines, _columns), false);

            // next theme button for fun - cycles through color themes
            _autoThemeBtn = new TextDisplayControl("THEME >", true);
            _autoThemeBtn.Clicked += AutoThemeButton_Click;
            
            // font glow
            _glowBtn = new TextDisplayControl("GLOW", true);
            var glowSubAmts = new[] { 0F, 1F, 2F, 3F, 5F, 7F, 10F, 15F };
            foreach (var s in glowSubAmts)
            {
                var item = new TextDisplayControl($"{s} radius", true);
                item.Clicked += (_, __) => { 
                    _mtd.BlurAmount = s; 
                    _uac.ThemeConfiguration.BlurAmount = s; 
                    ModtermCanvas.Invalidate(); 
                };
                _glowBtn.Children.Add(item);
            }

            // window transparency
            _backdropOpacityBtn = new TextDisplayControl("WINDOW OPACITY", true);
            for (int i = 0; i <= 10; i++)
            {
                byte pct = (byte)(i * 10);
                var item = new TextDisplayControl(i == 0 ? "Transparent 0%" : $"{pct}%", true);
                item.Clicked += (_, __) => {
                    _mtd.OpacityPct = pct;
                    _uac.ThemeConfiguration.WindowOpacityPct = pct;
                    _appearanceInfoControl.TextContent = _mtd.GetAppearanceInfo(_lines, _columns);
                    ModtermCanvas.Invalidate();
                };
                _backdropOpacityBtn.Children.Add(item);
            }

            // system backdrop (Mica / Acrylic / custom blurred host backdrop)
            _systemBackdropBtn = new TextDisplayControl("SYSTEM BACKDROP", true);
            var blurredBackdropItem = new TextDisplayControl("BLURRED", true);
            blurredBackdropItem.Clicked += (_, __) => { 
                _mtd.OpacityPct = 0; 
                _uac.ThemeConfiguration.BackdropKind = BackdropKind.Blurred; 
                _mtd.ApplySystemBackdrop(BackdropKind.Blurred, this); 
            };
            _systemBackdropBtn.Children.Add(blurredBackdropItem);
            var micaBackdropItem = new TextDisplayControl("MICA", true);
            micaBackdropItem.Clicked += (_, __) => { 
                _mtd.OpacityPct = 0; 
                _uac.ThemeConfiguration.BackdropKind = BackdropKind.Mica; 
                _mtd.ApplySystemBackdrop(BackdropKind.Mica, this); 
            };
            _systemBackdropBtn.Children.Add(micaBackdropItem);
            var acrylicBackdropItem = new TextDisplayControl("ACRYLIC", true);
            acrylicBackdropItem.Clicked += (_, __) => { 
                _mtd.OpacityPct = 0; 
                _uac.ThemeConfiguration.BackdropKind = BackdropKind.Acrylic; 
                _mtd.ApplySystemBackdrop(BackdropKind.Acrylic, this); 
            };
            _systemBackdropBtn.Children.Add(acrylicBackdropItem);

            // window tint
            _backdropColorBtn = new TextDisplayControl("WINDOW COLOR", true);
            var colorOptions = new (string, Color)[] {
                ("Coral", Colors.Coral),
                ("Sea Green", Colors.MediumSeaGreen),
                ("Turquoise", Colors.DarkTurquoise),
                ("Orange", Colors.DarkOrange),
                ("Magenta", Colors.Magenta),
                ("Violet", Colors.DarkViolet),
                ("Cyan", Colors.DarkCyan),
                ("Slate", Colors.DarkSlateBlue)
            };
            foreach (var (label, tint) in colorOptions)
            {
                var item = new TextDisplayControl(label, true);
                item.Clicked += (_, __) => {
                    _mtd.TintColor = tint;
                    _uac.ThemeConfiguration.WindowColor = tint;
                    _appearanceInfoControl.TextContent = _mtd.GetAppearanceInfo(_lines, _columns);
                    ModtermCanvas.Invalidate();
                };
                _backdropColorBtn.Children.Add(item);
            }

            // font family
            _fontFamilyBtn = new TextDisplayControl("FONT", true);
            var fonts = new[] { "Cascadia Mono", "Consolas", "Courier New", "Lucida Console", "SimSun-ExtB" };
            foreach (var f in fonts)
            {
                var item = new TextDisplayControl(f, true);
                item.Clicked += (_, __) => {
                    _mtd.CurrentFont = new FontFamily(f);
                    _uac.TerminalFont = f;
                    ModtermCanvas.Invalidate();
                };
                _fontFamilyBtn.Children.Add(item);
            }

            // font size
            _fontSizeBtn = new TextDisplayControl("FONT SZ", true);
            var sizes = new[] { 8, 10, 12, 13, 14, 15, 16, 17, 19, 23 };
            foreach (var s in sizes)
            {
                var item = new TextDisplayControl($"{s} pt", true);
                item.Clicked += (_, __) => {
                    _mtd.CurrentFontSize = (float)s;
                    RestartTerminalForLayoutChange();
                };
                _fontSizeBtn.Children.Add(item);
            }

            // shell selection
            _shellSelBtn = new TextDisplayControl("SHELL", true);
            foreach (Shell sh in _shellEnv)
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

            _titleBarControls.Controls.AddRange(
                [_pathControl, _appearanceInfoControl]);
            _rightButtonControls.Controls.AddRange(
                [_autoThemeBtn, _fontFamilyBtn, _fontSizeBtn,
                _systemBackdropBtn, _backdropOpacityBtn, _backdropColorBtn, _glowBtn, _shellSelBtn]);
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

        private void AutoThemeButton_Click(object? sender, EventArgs e)
        {
            // cycle through themes
            var themes = _mtd.GetConfigurationNames();
            _autoThemeIndex = (_autoThemeIndex + 1) % themes.Count;
            _mtd.SetColorConfiguration(themes[_autoThemeIndex]);
            _appearanceInfoControl.TextContent = _mtd.GetAppearanceInfo(_lines, _columns);
            ModtermCanvas.Invalidate();
        }   

        private void UpdateSelectedText()
        {
            // TODO: this is no longer complete and broke af - also, we should just copy when the rectangle is drawn (mouse button up),
            // not wait for a ctrl-c or context copy selection.
            _selectedText = string.Empty;
        }

        private async void PasteFromClipboard()
        {
            var dataPackageView = Clipboard.GetContent();
            if (dataPackageView.Contains(StandardDataFormats.Text))
            {
                string text = await dataPackageView.GetTextAsync();
                if (!string.IsNullOrEmpty(text))
                {
                    _terminal?.WriteInput(text);
                }
                ModtermCanvas.Invalidate();
            }
        }

        private void StartConPTY()
        {
            if (_terminal.Started)
                return;

            _appearanceInfoControl.TextContent = _mtd.GetAppearanceInfo(_lines, _columns);

            _terminal.OutputReceived += OnOutputReceived;
            _terminal.Start(_currentShell, _lines, _columns);

            _vtController.ResizeView(_columns, _lines);
            _terminal?.Resize((short)_columns, (short)_lines);
        }

        private void OnOutputReceived(object? sender, string line)
        {
            // Feed all output directly to the VT parser
            if (_scrollOffset > 0) _scrollOffset = 0;
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
                //DataPackage dataPackage = new DataPackage();
                //if (!string.IsNullOrEmpty(_selectedText))
                //{
                //    dataPackage.SetText(_selectedText);
                //    Clipboard.SetContent(dataPackage);
                //}
                //else if (!string.IsNullOrEmpty(_commandLine))
                //{
                //    dataPackage.SetText(_commandLine);
                //    Clipboard.SetContent(dataPackage);
                //}
                Debug.WriteLine("Copy command executed - selection: " + _selectedText);

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
                item.Click += (_, __) => {
                    // deserialize the theme config from disk and apply it
                    string themePath = Path.Combine(_userConfigDirectory, $"theme_{preset}.json");
                    ThemeConfiguration themeConfig = JsonSerializer.Deserialize<ThemeConfiguration>(File.ReadAllText(themePath)) ?? _uac.ThemeConfiguration;
                    _mtd.SetColorConfiguration(themeConfig);
                    _uac.ThemeConfiguration = themeConfig;
                    _uac.ThemeConfiguration.PropertyChanged += (s, e) => SaveConfig();
                    _appearanceInfoControl.TextContent = _mtd.GetAppearanceInfo(_lines, _columns);
                    ModtermCanvas.Invalidate();
                };
                themeItem.Items.Add(item);
            }
            _flyout.Items.Add(themeItem);
        }        
    }
}
