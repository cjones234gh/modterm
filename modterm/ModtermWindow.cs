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
        private bool _resizeNeeded = false;

        // modterm UI controls
        private ModtermControlGroup    _titleBarControls;
        private TextDisplayControl      _pathControl;
        private TextDisplayControl      _appearanceInfoControl;
        private TextDisplayControl      _autoThemeButton;
        private int                     _autoThemeIndex = 0;

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

        // context menu flyout for right-click and shell definitions
        private MenuFlyout _flyout;
        private static string _conargs = " --width [W] --height [H] -- "; // <-- TODO move into config and add more options like env vars, starting dir, etc.
        private List<Shell> _shellEnv = new List<Shell>()
        {
            new Shell { Name = "cmd", Path = "C:\\Windows\\System32\\cmd.exe" },
            new Shell { Name = "powershell", Path = "conhost", Arguments = _conargs + "C:\\Windows\\System32\\WindowsPowerShell\\v1.0\\powershell.exe" },
            new Shell { Name = "pwsh core", Path = "conhost", Arguments = _conargs + "C:\\Program Files\\PowerShell\\7\\pwsh.exe" },
            new Shell { Name = "bash", Path = "conhost", Arguments = _conargs + "C:\\Program Files\\Git\\bin\\bash.exe" },
            new Shell { Name = "wsl", Path = "conhost", Arguments = _conargs + "wsl.exe" }, 
            //new Shell
            //{
            //    Name = "wsl (pty-only debug)",
            //    Path = "wsl.exe",
            //    Arguments = "-e bash -i",
            //    LaunchMode = ConPtyLaunchMode.PseudoConsoleOnly
            //},
        };

        // mouse selection state
        private bool _isSelecting = false;
        private Windows.Foundation.Point _selectionStart;
        private Windows.Foundation.Point _selectionEnd;
        private string _selectedText = "";

        private void InitializeApplication()
        {

            _cursorTimer = DispatcherQueue.CreateTimer();
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
            _mtd.CurrentFontSize = 12f;
            _mtd.CurrentControlFont = new FontFamily("Cascadia Mono");
            _mtd.CurrentControlFontSize = 9.5f;


            // set the color config to a preset on startup
            _mtd.SetColorConfiguration("Aqua");
            ControlCanvas.Invalidate();

            _vtController.SetRgbForegroundColor(_mtd.OutputColor.R, 
                _mtd.OutputColor.G, _mtd.OutputColor.B);

            // ui controls and dock groups
            _titleBarControls = new ModtermControlGroup(
                ModtermControlGroup.CornerGroupDock.UpperCenterHorizontal);

            _pathControl = new TextDisplayControl(_currentShell.Path, false);   

            _appearanceInfoControl = new TextDisplayControl(_mtd.GetAppearanceInfo(_lines, _columns), false);

            _autoThemeButton = new TextDisplayControl("T H E M E ->", true);
            _autoThemeButton.Clicked += AutoThemeButton_Click;

            _titleBarControls.Controls.AddRange(
                [_pathControl, _appearanceInfoControl, _autoThemeButton]);

            // modglass style window setup
            this.AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;
            this.AppWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
            this.AppWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            this.SetTitleBar(AppTitleBar);

            this.SizeChanged += MainWindow_SizeChanged;
            this.Closed += (s, e) => _terminal?.Dispose();

            RootGrid.Background = _mtd.GetBackgroundBrush();
            RootGrid.KeyDown += ModtermCanvas_KeyDown;

            ControlCanvas.Draw += this.ControlCanvas_Draw;

            ModtermCanvas.Draw += this.ModtermCanvas_Draw;
            ModtermCanvas.RightTapped += this.ModtermCanvas_RightTapped;

            // Mouse support
            ModtermCanvas.PointerWheelChanged += this.ModtermCanvas_PointerWheelChanged;
            ModtermCanvas.PointerPressed += this.ModtermCanvas_PointerPressed;
            ModtermCanvas.PointerMoved += this.ModtermCanvas_PointerMoved;
            ModtermCanvas.PointerReleased += this.ModtermCanvas_PointerReleased;

            ControlCanvas.PointerPressed += this.ControlCanvas_PointerPressed;
            ControlCanvas.PointerReleased += this.ControlCanvas_PointerReleased;
            ControlCanvas.PointerMoved += this.ControlCanvas_PointerMoved;

            // Blinking cursor
            _cursorTimer.Interval = TimeSpan.FromMilliseconds(_cursorSpeed);
            _cursorTimer.Tick += (s, e) =>
            {
                _cursorVisible = !_cursorVisible;
                ModtermCanvas.Invalidate();
            };
            _cursorTimer.Start();

            // Background tint drift timer
            _bgTintDriftTimer.Interval = TimeSpan.FromMilliseconds(_bgTintDriftIntervalMs);
            _bgTintDriftTimer.Tick += (s, e) =>
            {
                _bgTintDriftColorOffset = (_bgTintDriftColorOffset + 1) % _bgTintDriftColors.Count;
                _mtd.TintColor = _bgTintDriftColors[_bgTintDriftColorOffset];
                _appearanceInfoControl.TextContent = _mtd.GetAppearanceInfo(_lines, _columns);
                ControlCanvas.Invalidate();
            };

            this.InitializeFlyouts();

            // Ensure a draw pass runs so deferred ConPTY start can measure the canvas.
            ModtermCanvas.Invalidate();
        }

        private void MainWindow_SizeChanged(object sender, WindowSizeChangedEventArgs e)
        {
            ResizeTerminal();
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

            if (_lines == 0 || _columns == 0)
            {
                // Set a default size if not already set by a drawing event
                _lines = 24;
                _columns = 80;
            }

            ConPTYTerminal.ClampTerminalDimensions(ref _lines, ref _columns);
            // First layout sync after start (Resize clamps sub-minimum requests).
            _resizeNeeded = true;

            _terminal.OutputReceived += OnOutputReceived;
            _terminal.Start(_currentShell, _lines, _columns);

            _vtController.ResizeView(_columns, _lines);
            _terminal?.Resize((short)_columns, (short)_lines);
        }

        // Ensure ConPTY and VT buffer are resized with accurate values after window/canvas resize
        public void ResizeTerminal()
        {
            _resizeNeeded = true;            
        }

        private void OnOutputReceived(object? sender, string line)
        {
            //Debug.WriteLine($"Unescaped (raw) output: [{line}] ");
            // for now, replace ANSI 0m, default color, with ANSI version of _mtd.OutputColor in this line
            // is there a way to set the default color in the VT parser so we don't have to do this replacement on every line?
            line = line.Replace("\x1B[0m", $"\x1B[38;2;{_mtd.OutputColor.R};{_mtd.OutputColor.G};{_mtd.OutputColor.B}m");
            //Debug.WriteLine($"After default color   : [{line.Replace("\r\n", "ENDL")}] ");
            //Debug.WriteLine("Output received: " + line);

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
                    ControlCanvas.Invalidate();
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
                    ModtermCanvas.Invalidate(); };
                sizeSub.Items.Add(item);
            }
            _flyout.Items.Add(sizeSub);

            // control font family
            var controlFontSub = new MenuFlyoutSubItem { Text = "Control Font Family" };
            var controlFonts = new[] { "Cascadia Mono", "Consolas", "Courier New", "Lucida Console", "Segoe UI Mono", "SimSun-ExtB" };
            foreach (var f in controlFonts)
            {
                var item = new MenuFlyoutItem { Text = f };
                item.Click += (_, __) => { _mtd.CurrentControlFont = new FontFamily(f); ControlCanvas.Invalidate(); };
                controlFontSub.Items.Add(item);
            }
            _flyout.Items.Add(controlFontSub);

            // control font size
            var controlSizeSub = new MenuFlyoutSubItem { Text = "Control Font Size" };
            var controlSizes = new[] { 8, 9, 10, 10.5, 11, 11.5, 12, 12.5, 13, 14 };
            foreach (var s in controlSizes)
            {
                var item = new MenuFlyoutItem { Text = $"{s} pt" };
                item.Click += (_, __) => { _mtd.CurrentControlFontSize = (float)s; ControlCanvas.Invalidate(); };
                controlSizeSub.Items.Add(item);
            }
            _flyout.Items.Add(controlSizeSub);

            // font glow
            var glowSub = new MenuFlyoutSubItem { Text = "UI Glow" };
            var glowSubAmts = new[] { 0F, 1F, 2F, 3F, 5F, 7F, 10F, 15F };
            foreach (var s in glowSubAmts)
            {
                var item = new MenuFlyoutItem { Text = $"{s} radius" };
                item.Click += (_, __) => { _mtd.BlurAmount = s; ModtermCanvas.Invalidate(); ControlCanvas.Invalidate(); };
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
                    if (_lines == 0 || _columns == 0)
                    {
                        // Set default size if not already set by a drawing event
                        _lines = 24;
                        _columns = 80;
                    }
                    ConPTYTerminal.ClampTerminalDimensions(ref _lines, ref _columns);
                    _resizeNeeded = true;
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
