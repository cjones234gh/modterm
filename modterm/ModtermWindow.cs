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
using Windows.System;
using Windows.UI;
using Windows.Foundation;
using Windows.Graphics;
using System.Linq;
using System.IO;

namespace modterm
{
    public sealed partial class ModtermWindow : Window
    {
        // VtNetCore terminal state
        private VtNetCore.VirtualTerminal.VirtualTerminalController _vtController;
        private VtNetCore.XTermParser.DataConsumer _vtDataConsumer;

        // main terminal logic and state
        private ConPTYTerminal  _terminal;
        private Shell           _currentShell = new Shell();
        private int             _scrollOffset = 0;
        private Microsoft.UI.Dispatching.DispatcherQueueTimer _cursorTimer;
        private Microsoft.UI.Dispatching.DispatcherQueueTimer _resizeStopTimer;

        // modterm UI controls
        private ControlGroup            _titleBarControls;
        private ControlGroup            _rightButtonControls;
        private TextDisplayControl      _pathControl;
        private TextDisplayControl      _appearanceInfoControl;
        private TextDisplayControl      _autoThemeBtn;
        private int                     _autoThemeIndex = 0;
        private TextDisplayControl      _systemBackdropBtn;
        private TextDisplayControl      _backdropColorBtn;
        private TextDisplayControl      _backdropOpacityBtn;
        private TextDisplayControl      _fontFamilyBtn;
        private TextDisplayControl      _fontSizeBtn;
        private TextDisplayControl      _themeSelectBtn;
        private TextDisplayControl      _glowBtn;
        private TextDisplayControl      _shellSelBtn;
        

        // modterm display
        private ModtermDisplay _mtd = new ModtermDisplay();

        // background tint drift state
        private bool            _bgTintDriftEnabled = false;
        private float           _bgTintDriftSaturation = 0;
        private List<Color>     _bgTintDriftColors = new List<Color>();
        private int             _bgTintDriftColorOffset = 0;
        private int             _bgTintDriftIntervalMs = 300;
        private Microsoft.UI.Dispatching.DispatcherQueueTimer _bgTintDriftTimer;

        // VT mode: cursor visibility only
        private bool            _cursorVisible = true;
        private int             _cursorSpeed = 500;

        // context menu flyout for right-click
        private MenuFlyout _flyout;
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

            _cursorTimer = DispatcherQueue.CreateTimer();
            _resizeStopTimer = DispatcherQueue.CreateTimer();
            _bgTintDriftTimer = DispatcherQueue.CreateTimer();

            _terminal = new ConPTYTerminal();
            _flyout = new MenuFlyout();

            // default shell
            _currentShell = _shellEnv.First(item => item.Name == "bash");

            // Initialize VtNetCore terminal controller and data consumer
            _vtController = new VtNetCore.VirtualTerminal.VirtualTerminalController();
            _vtDataConsumer = new VtNetCore.XTermParser.DataConsumer(_vtController);

            // todo: load/create user config here

            // init modterm display and set default appearance config
            _mtd.Initialize();

            // set fonts until we have a config system in place
            _mtd.CurrentFont = new FontFamily("Consolas");
            _mtd.CurrentControlFont = new FontFamily("Lucida Console");
            _mtd.CurrentFontSize = 12F;
            
            // set the color config to a preset on startup
            _mtd.SetColorConfiguration("BluePunk");

            _vtController.SetRgbForegroundColor(_mtd.OutputColor.R, 
                _mtd.OutputColor.G, _mtd.OutputColor.B);

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

            // Background tint drift timer
            _bgTintDriftTimer.Interval = TimeSpan.FromMilliseconds(_bgTintDriftIntervalMs);
            _bgTintDriftTimer.Tick += (s, e) =>
            {
                _bgTintDriftColorOffset = (_bgTintDriftColorOffset + 1) % _bgTintDriftColors.Count;
                _mtd.TintColor = _bgTintDriftColors[_bgTintDriftColorOffset];
                _appearanceInfoControl.TextContent = _mtd.GetAppearanceInfo(_lines, _columns);
                ModtermCanvas.Invalidate();
            };

            this.InitializeFlyouts();
            _lastWindowSize = this.AppWindow.Size;

            // Ensure a draw pass runs so deferred ConPTY start can measure the canvas.
            ModtermCanvas.Invalidate();
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

            // next theme button for fun - cycles through color presets
            _autoThemeBtn = new TextDisplayControl("THEME >", true);
            _autoThemeBtn.Clicked += AutoThemeButton_Click;

            // font glow
            _glowBtn = new TextDisplayControl("GLOW", true);
            var glowSubAmts = new[] { 0F, 1F, 2F, 3F, 5F, 7F, 10F, 15F };
            foreach (var s in glowSubAmts)
            {
                var item = new TextDisplayControl($"{s} radius", true);
                item.Clicked += (_, __) => { _mtd.BlurAmount = s; ModtermCanvas.Invalidate(); };
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
                    _appearanceInfoControl.TextContent = _mtd.GetAppearanceInfo(_lines, _columns);
                    ModtermCanvas.Invalidate();
                };
                _backdropOpacityBtn.Children.Add(item);
            }

            // system backdrop (Mica / Acrylic / custom blurred host backdrop)
            _systemBackdropBtn = new TextDisplayControl("SYSTEM BACKDROP", true);
            var blurredBackdropItem = new TextDisplayControl("BLURRED", true);
            blurredBackdropItem.Clicked += (_, __) => { _mtd.OpacityPct = 0; _mtd.ApplySystemBackdrop(BackdropKind.Blurred, this); };
            _systemBackdropBtn.Children.Add(blurredBackdropItem);
            var micaBackdropItem = new TextDisplayControl("MICA", true);
            micaBackdropItem.Clicked += (_, __) => { _mtd.OpacityPct = 0; _mtd.ApplySystemBackdrop(BackdropKind.Mica, this); };
            _systemBackdropBtn.Children.Add(micaBackdropItem);
            var acrylicBackdropItem = new TextDisplayControl("ACRYLIC", true);
            acrylicBackdropItem.Clicked += (_, __) => { _mtd.OpacityPct = 0; _mtd.ApplySystemBackdrop(BackdropKind.Acrylic, this); };
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
                    _bgTintDriftEnabled = false;
                    _bgTintDriftTimer.Stop();
                    _appearanceInfoControl.TextContent = _mtd.GetAppearanceInfo(_lines, _columns);
                    ModtermCanvas.Invalidate();
                };
                _backdropColorBtn.Children.Add(item);
            }

            // theme
            _themeSelectBtn = new TextDisplayControl("SEL THEME", true);
            foreach (var preset in _mtd.GetConfigurationNames())
            {
                var item = new TextDisplayControl(preset, true);
                item.Clicked += (_, __) => {
                    _mtd.SetColorConfiguration(preset);
                    _bgTintDriftEnabled = false;
                    _bgTintDriftTimer.Stop();
                    _appearanceInfoControl.TextContent = _mtd.GetAppearanceInfo(_lines, _columns);
                    ModtermCanvas.Invalidate();
                };
                _themeSelectBtn.Children.Add(item);
            }

            // font family
            _fontFamilyBtn = new TextDisplayControl("FONT", true);
            var fonts = new[] { "Cascadia Mono", "Consolas", "Courier New", "Lucida Console", "SimSun-ExtB" };
            foreach (var f in fonts)
            {
                var item = new TextDisplayControl(f, true);
                item.Clicked += (_, __) => {
                    _mtd.CurrentFont = new FontFamily(f);
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
                };
                _shellSelBtn.Children.Add(item);
            }

            _titleBarControls.Controls.AddRange(
                [_pathControl, _appearanceInfoControl]);
            _rightButtonControls.Controls.AddRange(
                [_autoThemeBtn, _themeSelectBtn, _fontFamilyBtn, _fontSizeBtn,
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

        private void AutoThemeButton_Click(object sender, EventArgs e)
        {
            // cycle through color presets for fun
            var presets = _mtd.GetConfigurationNames();
            _autoThemeIndex = (_autoThemeIndex + 1) % presets.Count;
            _mtd.SetColorConfiguration(presets[_autoThemeIndex]);
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
                       
                
            // Drift option with saturation sub-flyout
            var driftSub = new MenuFlyoutItem { Text = "Drifting Tint Color" };
            driftSub.Click += (_, __) => {
                    _bgTintDriftEnabled = true;
                    _bgTintDriftSaturation = 0.5f;
                    _bgTintDriftColors.Clear();
                    _bgTintDriftColors = _mtd.GetColorWheelProgression(0.5, _bgTintDriftSaturation, 720);
                    _bgTintDriftColorOffset = 0;
                    _bgTintDriftTimer.Start();
                    ModtermCanvas.Invalidate();
                };
            _flyout.Items.Add(driftSub);

            _flyout.Items.Add(new MenuFlyoutSeparator());
        }        
    }
}
