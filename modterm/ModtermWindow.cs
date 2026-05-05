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
        private TextDisplayControl      _sysbackdropBtn;
        private TextDisplayControl      _backdropColorBtn;
        private TextDisplayControl      _backdropOpacityBtn;
        private TextDisplayControl      _glowBtn;
        

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
            _mtd.CurrentFontSize = 12F;
            _mtd.CurrentControlFont = new FontFamily("Cascadia Mono");
            _mtd.CurrentControlFontSize = 9.5f;


            // set the color config to a preset on startup
            _mtd.SetColorConfiguration("BluePunk");

            _vtController.SetRgbForegroundColor(_mtd.OutputColor.R, 
                _mtd.OutputColor.G, _mtd.OutputColor.B);

            // ui controls and dock groups
            _titleBarControls = new ControlGroup(
                ControlGroup.ControlDock.Top, _mtd.ControlPadding);
            _rightButtonControls = new ControlGroup(
                ControlGroup.ControlDock.Right, _mtd.ControlPadding);

            _pathControl = new TextDisplayControl(_currentShell.Path, false);   

            _appearanceInfoControl = new TextDisplayControl(_mtd.GetAppearanceInfo(_lines, _columns), false);

            _autoThemeBtn = new TextDisplayControl("THEME", true);
            _autoThemeBtn.Clicked += AutoThemeButton_Click;
            _sysbackdropBtn = new TextDisplayControl("BACKDROP", true);
            _backdropOpacityBtn = new TextDisplayControl("OPACITY", true);
            _backdropColorBtn = new TextDisplayControl("BACKCOLOR", true);
            _glowBtn = new TextDisplayControl("GLOW", true);

            _titleBarControls.Controls.AddRange(
                [_pathControl, _appearanceInfoControl]);
            _rightButtonControls.Controls.AddRange(
                [_autoThemeBtn, _sysbackdropBtn, _backdropOpacityBtn, _backdropColorBtn, _glowBtn]);

            // modglass style window setup
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

            // theme
            var themeSub = new MenuFlyoutSubItem { Text = "Theme" };
            foreach (var preset in _mtd.GetConfigurationNames())
            {
                var item = new MenuFlyoutItem { Text = preset };
                item.Click += (_, __) => { 
                    _mtd.SetColorConfiguration(preset); 
                    _bgTintDriftEnabled = false; 
                    _bgTintDriftTimer.Stop();
                    _appearanceInfoControl.TextContent = _mtd.GetAppearanceInfo(_lines, _columns);
                    ModtermCanvas.Invalidate();
                };
                themeSub.Items.Add(item);
            }
            _flyout.Items.Add(themeSub);

            // window transparency
            var transSub = new MenuFlyoutSubItem { Text = "Transparency" };
            for (int i = 0; i <= 10; i++)
            {
                byte pct = (byte)(i * 10);
                var item = new MenuFlyoutItem { Text = i == 0 ? "Transparent (0%)" : $"{pct}%" };
                item.Click += (_, __) => { 
                    _mtd.TransparencyPct = pct;
                    _appearanceInfoControl.TextContent = _mtd.GetAppearanceInfo(_lines, _columns);
                    ModtermCanvas.Invalidate(); };
                transSub.Items.Add(item);
            }
            _flyout.Items.Add(transSub);

            // system backdrop (Mica / Acrylic / custom blurred host backdrop)
            var backdropSub = new MenuFlyoutSubItem { Text = "System Backdrop" };
            var blurredBackdropItem = new MenuFlyoutItem { Text = "Blurred" };
            blurredBackdropItem.Click += (_, __) => _mtd.ApplySystemBackdrop(BackdropKind.Blurred, this);
            backdropSub.Items.Add(blurredBackdropItem);
            var micaBackdropItem = new MenuFlyoutItem { Text = "Mica" };
            micaBackdropItem.Click += (_, __) => _mtd.ApplySystemBackdrop(BackdropKind.Mica, this);
            backdropSub.Items.Add(micaBackdropItem);
            var acrylicBackdropItem = new MenuFlyoutItem { Text = "Acrylic" };
            acrylicBackdropItem.Click += (_, __) => _mtd.ApplySystemBackdrop(BackdropKind.Acrylic, this);
            backdropSub.Items.Add(acrylicBackdropItem);
            _flyout.Items.Add(backdropSub);
    
            // window tint
            var tintSub = new MenuFlyoutSubItem { Text = "Tint" };
            var tintOptions = new (string, Color)[] {
                ("Transparent", Colors.Transparent),
                ("Snow White", Colors.White),
                ("Pitch Black", Colors.Black),
                ("Alice Blue", Colors.AliceBlue),
                ("Coral", Colors.Coral),
                ("Medium Purple", Colors.MediumPurple),
                ("Medium Sea Green", Colors.MediumSeaGreen),
                ("Gold", Colors.Gold),
                ("Deep Pink", Colors.DeepPink),
                ("Crimson", Colors.Crimson),
                ("Dark Turquoise", Colors.DarkTurquoise),
                ("Magenta", Colors.Magenta),
                ("Dark Violet", Colors.DarkViolet),
                ("Dark Cyan", Colors.DarkCyan),
                ("Dark Goldenrod", Colors.DarkGoldenrod),
                ("Dark Slate Blue", Colors.DarkSlateBlue)
            };
            foreach (var (label, tint) in tintOptions)
            {
                var item = new MenuFlyoutItem { Text = label };
                item.Click += (_, __) => { 
                    _mtd.TintColor = tint; 
                    _bgTintDriftEnabled = false; 
                    _bgTintDriftTimer.Stop();
                    _appearanceInfoControl.TextContent = _mtd.GetAppearanceInfo(_lines, _columns);
                    ModtermCanvas.Invalidate(); };
                tintSub.Items.Add(item);
            }

            // Drift option with saturation sub-flyout
            var driftSub = new MenuFlyoutSubItem { Text = "Drift" };
            for (int i = 1; i <= 10; i++)
            {
                int sat = i * 10;
                var item = new MenuFlyoutItem { Text = $"{sat}%" };
                item.Click += (_, __) => {
                    _bgTintDriftEnabled = true;
                    _bgTintDriftSaturation = (float)sat / 100f;
                    _bgTintDriftColors.Clear();
                    _bgTintDriftColors = _mtd.GetColorWheelProgression(0.5, _bgTintDriftSaturation, 720);
                    _bgTintDriftColorOffset = 0;
                    _bgTintDriftTimer.Start();
                    ModtermCanvas.Invalidate();
                };
                driftSub.Items.Add(item);
            }
            tintSub.Items.Add(driftSub);
            _flyout.Items.Add(tintSub);

            _flyout.Items.Add(new MenuFlyoutSeparator());

            // font family
            var fontSub = new MenuFlyoutSubItem { Text = "Font Family" };
            var fonts = new[] { "Cascadia Mono", "Consolas", "Courier New", "Lucida Console", "Segoe UI Mono", "SimSun-ExtB" };
            foreach (var f in fonts)
            {
                var item = new MenuFlyoutItem { Text = f };
                item.Click += (_, __) => { _mtd.CurrentFont = new FontFamily(f); ModtermCanvas.Invalidate(); };
                fontSub.Items.Add(item);
            }
            _flyout.Items.Add(fontSub);

            // font size
            var sizeSub = new MenuFlyoutSubItem { Text = "Font Size" };
            var sizes = new[] { 8, 10, 12, 13.5, 14.5, 15.5, 16.5, 17.5 };
            foreach (var s in sizes)
            {
                var item = new MenuFlyoutItem { Text = $"{s} pt" };
                item.Click += (_, __) => { 
                    _mtd.CurrentFontSize = (float)s;
                    RestartTerminalForLayoutChange();
                };
                sizeSub.Items.Add(item);
            }
            _flyout.Items.Add(sizeSub);

            // control font family
            var controlFontSub = new MenuFlyoutSubItem { Text = "Control Font Family" };
            var controlFonts = new[] { "Cascadia Mono", "Consolas", "Courier New", "Lucida Console", "Segoe UI Mono", "SimSun-ExtB" };
            foreach (var f in controlFonts)
            {
                var item = new MenuFlyoutItem { Text = f };
                item.Click += (_, __) => { _mtd.CurrentControlFont = new FontFamily(f); ModtermCanvas.Invalidate(); };
                controlFontSub.Items.Add(item);
            }
            _flyout.Items.Add(controlFontSub);

            // control font size
            var controlSizeSub = new MenuFlyoutSubItem { Text = "Control Font Size" };
            var controlSizes = new[] { 8, 9, 10, 10.5, 11, 11.5, 12, 12.5, 13, 14 };
            foreach (var s in controlSizes)
            {
                var item = new MenuFlyoutItem { Text = $"{s} pt" };
                item.Click += (_, __) => { _mtd.CurrentControlFontSize = (float)s; ModtermCanvas.Invalidate(); };
                controlSizeSub.Items.Add(item);
            }
            _flyout.Items.Add(controlSizeSub);

            // font glow
            var glowSub = new MenuFlyoutSubItem { Text = "UI Glow" };
            var glowSubAmts = new[] { 0F, 1F, 2F, 3F, 5F, 7F, 10F, 15F };
            foreach (var s in glowSubAmts)
            {
                var item = new MenuFlyoutItem { Text = $"{s} radius" };
                item.Click += (_, __) => { _mtd.BlurAmount = s; ModtermCanvas.Invalidate(); };
                glowSub.Items.Add(item);
            }
            _flyout.Items.Add(glowSub);

            // input color
            var inputColorSub = new MenuFlyoutSubItem { Text = "Input Color" };
            foreach (var (name, col) in _colorOptions)
            {
                var item = new MenuFlyoutItem { Text = name };
                item.Click += (_, __) => { _mtd.InputColor = col; ModtermCanvas.Invalidate(); };
                inputColorSub.Items.Add(item);
            }
            _flyout.Items.Add(inputColorSub);

            // output color
            var outputColorSub = new MenuFlyoutSubItem { Text = "Output Color" };
            foreach (var (name, col) in _colorOptions)
            {
                var item = new MenuFlyoutItem { Text = name };
                item.Click += (_, __) => { _mtd.OutputColor = col; ModtermCanvas.Invalidate(); };
                outputColorSub.Items.Add(item);
            }
            _flyout.Items.Add(outputColorSub);

            // shell selection
            var shellSub = new MenuFlyoutSubItem { Text = "Shell" };
            foreach (Shell sh in _shellEnv)
            {
                var item = new MenuFlyoutItem { Text = sh.Name };
                item.Click += async (_, __) =>
                {
                    _terminal.Started = false;
                    _terminal.Dispose();
                    await Task.Delay(1000); // Pauses for 1 second without blocking the UI thread
                    _terminal = new ConPTYTerminal();
                    _terminal.OutputReceived += OnOutputReceived;
                    _terminal.Start(sh, _lines, _columns);
                };
                shellSub.Items.Add(item);
            }
            _flyout.Items.Add(shellSub);
        }

        private readonly (string Name, Color Color)[] _colorOptions = new[]
        {
            ("White", Colors.White),
            ("OG", Color.FromArgb(255, 0, 238, 255)),
            ("Cyan", Colors.Cyan),
            ("Bright Violet", Color.FromArgb(255, 187, 68, 255)),
            ("Dim Violet", Color.FromArgb(255, 136, 0, 204)),
            ("Bright Azure", Color.FromArgb(255, 68, 153, 255)),
            ("Dim Azure", Color.FromArgb(255, 0, 102, 204)),
            ("Bright Verdant", Color.FromArgb(255, 85, 255, 136)),
            ("Dim Verdant", Color.FromArgb(255, 0, 170, 85)),
            ("Bright Sunny", Color.FromArgb(255, 255, 255, 102)),
            ("Dim Sunny", Color.FromArgb(255, 204, 204, 0)),
            ("Bright Citrus", Color.FromArgb(255, 255, 187, 85)),
            ("Dim Citrus", Color.FromArgb(255, 204, 136, 34)),
            ("Bright Ember", Color.FromArgb(255, 255, 102, 102)),
            ("Dim Ember", Color.FromArgb(255, 204, 51, 51))
        };

        
    }
}
