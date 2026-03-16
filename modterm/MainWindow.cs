using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Numerics;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Windows.UI;
using Windows.UI.Core;
using Windows.Foundation;
using Modglass;

namespace modterm
{
    public sealed partial class MainWindow : Window
    {
        // main terminal logic and state
        private ConPTYTerminal  _terminal;
        private string           _shellApplicationPath;
        private int             _scrollOffset = 0;
        private Microsoft.UI.Dispatching.DispatcherQueueTimer _cursorTimer;

        // modglass UI controls
        private ModglassCornerControlGroup    _upperRightControls;
        private ModglassCornerControlGroup    _lowerRightControls;
        private DisplayTextControl      _scrollLockControl;
        private DisplayTextControl      _pathControl;
        private DisplayTextControl      _currentTintTransparencyControl;
        private RunningGraphControl     _testRunningGraphControlR;
        private RunningGraphControl _testRunningGraphControlG;
        private RunningGraphControl _testRunningGraphControlB;

        // background tint drift state
        private bool            _bgTintDriftEnabled = false;
        private float           _bgTintDriftSaturation = 0;
        private List<Color>     _bgTintDriftColors = new List<Color>();
        private int             _bgTintDriftColorOffset = 0;
        private int             _bgTintDriftIntervalMs = 333;
        private Microsoft.UI.Dispatching.DispatcherQueueTimer _bgTintDriftTimer;

        // command line and buffer state
        private string          _commandLine = "";
        private List<string>    _commandLineHistory = new List<string>();
        private int             _commandHistorySize;
        private int             _commandHistoryIndex = 0;
        private int             _bufferSize;
        private List<string>    _bufferLines = new List<string>();
        private bool            _cursorVisible = true;
        private int             _commandLineCursorPos = 0;
        

        // context menu flyout for right-click and shell definitions
        private MenuFlyout _flyout;
        private Dictionary<string, string> _shellEnv = new Dictionary<string, string>()
        {
            { "cmd", "C:\\Windows\\System32\\cmd.exe" },
            { "powershell", "C:\\Windows\\System32\\WindowsPowerShell\\v1.0\\powershell.exe" },
            { "pwsh", "C:\\Program Files\\PowerShell\\7\\pwsh.exe" },
            { "bash", "C:\\Program Files\\Git\\usr\\bin\\bash.exe" },
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

            _commandHistorySize = 200;
            _bufferSize = 1000;
            _terminal = new ConPTYTerminal();
            _flyout = new MenuFlyout();
            _shellApplicationPath = _shellEnv.ContainsKey("bash") ? _shellEnv["bash"] :
                (_shellEnv.ContainsKey("cmd") ? _shellEnv["cmd"] : "cmd.exe");

            // todo: load/create user config here
            ModglassDisplay.Initialize();

            // set the color config to a preset on startup
            ModglassDisplay.SetColorConfiguration("Glowmancer");

            // the launch shell
            _shellApplicationPath = _shellEnv["bash"];

            // ui controls and dock groups
            _upperRightControls = new ModglassCornerControlGroup(
                ModglassCornerControlGroup.CornerGroupDock.UpperRightHorizontal);
            _lowerRightControls = new ModglassCornerControlGroup(
                ModglassCornerControlGroup.CornerGroupDock.LowerRightVertical);

            _testRunningGraphControlR = new RunningGraphControl(
                new Rect(0, 0, 120, 120), 1000, 0, 255);
            
            _testRunningGraphControlG = new RunningGraphControl(
                new Rect(0, 0, 120, 120), 1000, 0, 255);
            
            _testRunningGraphControlB = new RunningGraphControl(
                new Rect(0, 0, 120, 120), 1000, 0, 255);
            
            _scrollLockControl = new DisplayTextControl
                (new Rect(0, 0, 0, 0), "S C R L K");

            _pathControl = new DisplayTextControl(
                new Rect(0, 0, 0, 0), _shellApplicationPath);   

            _currentTintTransparencyControl = new DisplayTextControl(
                new Rect(0, 0, 0, 0), GetCurrentTintTransparencyInfo());

            _lowerRightControls.Controls.AddRange(
                [_testRunningGraphControlB, _testRunningGraphControlG, _testRunningGraphControlR]);
            
            _upperRightControls.Controls.AddRange(
                [_scrollLockControl, _pathControl, _currentTintTransparencyControl]);

            // modglass style window setup
            this.AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;
            this.AppWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
            this.AppWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            this.SetTitleBar(AppTitleBar);

            this.SizeChanged += MainWindow_SizeChanged;
            this.Closed += (s, e) => _terminal?.Dispose();

            this.Activated += (s, e) =>
            {
                if (!_terminal.Started)
                {
                    StartConPTY();
                    Debug.WriteLine("Window activated, terminal started");
                }
            };

            RootGrid.Background = ModglassDisplay.GetBackgroundBrush();
            RootGrid.KeyDown += ModtermCanvas_KeyDown;

            ModtermCanvas.Draw += this.ModtermCanvas_Draw;
            ModtermCanvas.RightTapped += this.ModtermCanvas_RightTapped;

            // Mouse support
            ModtermCanvas.PointerWheelChanged += this.ModtermCanvas_PointerWheelChanged;
            ModtermCanvas.PointerPressed += this.ModtermCanvas_PointerPressed;
            ModtermCanvas.PointerMoved += this.ModtermCanvas_PointerMoved;
            ModtermCanvas.PointerReleased += this.ModtermCanvas_PointerReleased;

            // Blinking cursor
            _cursorTimer.Interval = TimeSpan.FromMilliseconds(500);
            _cursorTimer.Tick += (s, e) =>
            {
                _cursorVisible = !_cursorVisible;
                ModtermCanvas.Invalidate();
            };
            _cursorTimer.Start();

            // Background tint drift timer
            _bgTintDriftTimer.Interval = TimeSpan.FromMilliseconds(_bgTintDriftIntervalMs); /*/ 3 colors per second / 1 minute loop /*/
            _bgTintDriftTimer.Tick += (s, e) =>
            {
                _bgTintDriftColorOffset = (_bgTintDriftColorOffset + 1) % _bgTintDriftColors.Count;
                ModglassDisplay.TintColor = _bgTintDriftColors[_bgTintDriftColorOffset];
                _currentTintTransparencyControl.TextContent = GetCurrentTintTransparencyInfo();
                _testRunningGraphControlR.DataPoints.Add(ModglassDisplay.TintColor.R); // example of using the current tint color to drive a graph control
                _testRunningGraphControlG.DataPoints.Add(ModglassDisplay.TintColor.G); // example of using the current tint color to drive a graph control
                _testRunningGraphControlB.DataPoints.Add(ModglassDisplay.TintColor.B); // example of using the current tint color to drive a graph control
                ModtermCanvas.Invalidate();
            };

            this.InitializeFlyouts();
            
        }

        private void UpdateSelectedText()
        {
            // TODO: this is broke af - also, we should just copy when the rectangle is drawn (mouse button up),
            // not wait for a ctrl-c or context copy selection.
            int startLine = Math.Max(0, _bufferLines.Count - 1 - _scrollOffset - 
                (int)(_selectionStart.Y / (ModglassDisplay.CurrentFontSize + 5)));
            int endLine = Math.Max(0, _bufferLines.Count - 1 - _scrollOffset - 
                (int)(_selectionEnd.Y / (ModglassDisplay.CurrentFontSize + 5)));

            if (startLine > endLine) (startLine, endLine) = (endLine, startLine);

            var selectedLines = new List<string>();
            for (int i = Math.Min(startLine, endLine); i <= Math.Max(startLine, endLine); i++)
            {
                if (i < _bufferLines.Count)
                    selectedLines.Add(_bufferLines[i]);
            }
            _selectedText = string.Join("\r\n", selectedLines);
        }

        private async void PasteFromClipboard()
        {
            var dataPackageView = Clipboard.GetContent();
            if (dataPackageView.Contains(StandardDataFormats.Text))
            {
                string text = await dataPackageView.GetTextAsync();
                text = text.Replace("\r\n", "\n").Replace("\r", "\n");
                foreach (char c in text)
                {
                    if (c == '\n')
                    {
                        _terminal?.WriteInput(_commandLine + "\n");
                        _bufferLines.Add("> " + _commandLine);
                        if (_bufferLines.Count > _bufferSize) _bufferLines.RemoveAt(0);
                        if (_commandLine != string.Empty)
                            _commandLineHistory.Add(_commandLine);
                        _commandLine = "";
                    }
                    else
                    {
                        _commandLine += c;
                    }
                }
                ModtermCanvas.Invalidate();
            }
        }

        private void StartConPTY()
        {
            _terminal.OutputReceived += OnOutputReceived;
            _terminal.Start(_shellApplicationPath, "");
        }

        private void OnOutputReceived(object? sender, string line)
        {
            // Reset scroll when new output arrives (optional quality-of-life)
            if (_scrollOffset > 0) _scrollOffset = 0;

            if (string.IsNullOrWhiteSpace(line)) return;
            var lines = line.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var l in lines)
            {
                _bufferLines.Add(l);
                if (_bufferLines.Count > _bufferSize)
                    _bufferLines.RemoveAt(0);
                ModtermCanvas.Invalidate();
            }
        }

        private List<(string Text, Color Color)> ParseAnsiSegments(string line, Color defaultColor)
        {
            var segments = new List<(string, Color)>();
            var currentColor = defaultColor;
            var sb = new StringBuilder();

            int i = 0;
            while (i < line.Length)
            {
                if (line[i] == '\x1B' && i + 1 < line.Length && line[i + 1] == '[')
                {
                    // Flush pending text
                    if (sb.Length > 0)
                    {
                        segments.Add((sb.ToString(), currentColor));
                        sb.Clear();
                    }

                    // Parse escape sequence
                    i += 2; // skip \x1B[
                    string code = "";
                    while (i < line.Length && line[i] != 'm')
                    {
                        code += line[i];
                        i++;
                        }
                    if (i < line.Length) i++; // skip 'm'

                    // === Apply color codes ===
                    if (code == "0" || code == "39" || code == "")
                    {
                        currentColor = defaultColor;
                    }
                    else if (code.StartsWith("38;2;")) // 24-bit truecolor
                    {
                        var rgb = code.Substring(5).Split(';');
                        if (rgb.Length == 3 &&
                            byte.TryParse(rgb[0], out byte r) &&
                            byte.TryParse(rgb[1], out byte g) &&
                            byte.TryParse(rgb[2], out byte b))
                        {
                            currentColor = Color.FromArgb(255, r, g, b);
                        }
                    }
                    else if (code.StartsWith("38;5;")) // 256-color (basic mapping)
                    {
                        if (int.TryParse(code.Substring(5), out int n))
                        {
                            currentColor = n switch
                            {
                                0 => Color.FromArgb(255, 0, 0, 0),
                                1 => Color.FromArgb(255, 128, 0, 0),
                                2 => Color.FromArgb(255, 0, 128, 0),
                                3 => Color.FromArgb(255, 128, 128, 0),
                                4 => Color.FromArgb(255, 0, 0, 128),
                                5 => Color.FromArgb(255, 128, 0, 128),
                                6 => Color.FromArgb(255, 0, 128, 128),
                                7 => Color.FromArgb(255, 192, 192, 192),
                                8 => Color.FromArgb(255, 128, 128, 128),
                                9 => Color.FromArgb(255, 255, 0, 0),
                                10 => Color.FromArgb(255, 0, 255, 0),
                                11 => Color.FromArgb(255, 255, 255, 0),
                                12 => Color.FromArgb(255, 0, 0, 255),
                                13 => Color.FromArgb(255, 255, 0, 255),
                                14 => Color.FromArgb(255, 0, 255, 255),
                                15 => Color.FromArgb(255, 255, 255, 255),
                                _ => defaultColor // fallback for other 256 colors
                            };
                        }
                    }
                    else if (int.TryParse(code, out int basic) && basic >= 30 && basic <= 37)
                    {
                        currentColor = basic switch
                        {
                            30 => Color.FromArgb(255, 0, 0, 0),
                            31 => Color.FromArgb(255, 255, 85, 85),
                            32 => Color.FromArgb(255, 85, 255, 85),
                            33 => Color.FromArgb(255, 255, 255, 85),
                            34 => Color.FromArgb(255, 85, 85, 255),
                            35 => Color.FromArgb(255, 255, 85, 255),
                            36 => Color.FromArgb(255, 85, 255, 255),
                            37 => Color.FromArgb(255, 255, 255, 255),
                            _ => defaultColor
                        };
                    }
                    else if (int.TryParse(code, out int bright) && bright >= 90 && bright <= 97)
                    {
                        currentColor = bright switch
                        {
                            90 => Color.FromArgb(255, 128, 128, 128),
                            91 => Color.FromArgb(255, 255, 85, 85),
                            92 => Color.FromArgb(255, 85, 255, 85),
                            93 => Color.FromArgb(255, 255, 255, 85),
                            94 => Color.FromArgb(255, 85, 85, 255),
                            95 => Color.FromArgb(255, 255, 85, 255),
                            96 => Color.FromArgb(255, 85, 255, 255),
                            97 => Color.FromArgb(255, 255, 255, 255),
                            _ => defaultColor
                        };
                    }
                    continue;
                }

                sb.Append(line[i]);
                i++;
            }

            if (sb.Length > 0)
                segments.Add((sb.ToString(), currentColor));

            return segments;
        }

        private void InitializeFlyouts()
        {
            _flyout = new MenuFlyout();

            var copyItem = new MenuFlyoutItem { Text = "Copy" };
            copyItem.Click += (_, __) =>
            {
                DataPackage dataPackage = new DataPackage();
                if (!string.IsNullOrEmpty(_selectedText))
                {
                    dataPackage.SetText(_selectedText);
                    Clipboard.SetContent(dataPackage);
                }
                else if (!string.IsNullOrEmpty(_commandLine))
                {
                    dataPackage.SetText(_commandLine);
                    Clipboard.SetContent(dataPackage);
                }

            };
            _flyout.Items.Add(copyItem);

            var pasteItem = new MenuFlyoutItem { Text = "Paste" };
            pasteItem.Click += (_, __) => PasteFromClipboard();
            _flyout.Items.Add(pasteItem);
            _flyout.Items.Add(new MenuFlyoutSeparator());

            // theme
            var themeSub = new MenuFlyoutSubItem { Text = "Theme" };
            foreach (var preset in ModglassDisplay.GetConfigurationNames())
            {
                var item = new MenuFlyoutItem { Text = preset };
                item.Click += (_, __) => { 
                    ModglassDisplay.SetColorConfiguration(preset); 
                    _bgTintDriftEnabled = false; 
                    _bgTintDriftTimer.Stop();
                    _currentTintTransparencyControl.TextContent = GetCurrentTintTransparencyInfo();
                    ModtermCanvas.Invalidate(); };
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
                    ModglassDisplay.TransparencyPct = pct;
                    _currentTintTransparencyControl.TextContent = GetCurrentTintTransparencyInfo();
                    ModtermCanvas.Invalidate(); };
                transSub.Items.Add(item);
            }
            _flyout.Items.Add(transSub);
    
            // window tint
            var tintSub = new MenuFlyoutSubItem { Text = "Tint" };
            var tintOptions = new (string, Color)[] {
                ("Transparent", Colors.Transparent), ("Snow White", Colors.White),
                ("Pitch Black", Colors.Black),
                ("Violet", Color.FromArgb(255,153,0,255)), ("Azure", Colors.Blue),
                ("Verdant", Colors.Lime), ("Sunny", Colors.Yellow),
                ("Citrus", Color.FromArgb(255,255,153,0)), ("Ember", Colors.Red)
            };
            foreach (var (label, tint) in tintOptions)
            {
                var item = new MenuFlyoutItem { Text = label };
                item.Click += (_, __) => { 
                    ModglassDisplay.TintColor = tint; 
                    _bgTintDriftEnabled = false; 
                    _bgTintDriftTimer.Stop();
                    _currentTintTransparencyControl.TextContent = GetCurrentTintTransparencyInfo();
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
                    _bgTintDriftIntervalMs = 333; // 3 colors per second
                    _bgTintDriftColors = ModglassDisplay.GetColorWheelProgression(0.5, _bgTintDriftSaturation, 720);
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
            var fonts = new[] { "Cascadia Mono", "Consolas", "Courier New", "Lucida Console", "Segoe UI Mono" };
            foreach (var f in fonts)
            {
                var item = new MenuFlyoutItem { Text = f };
                item.Click += (_, __) => { ModglassDisplay.CurrentFont = new FontFamily(f); ModtermCanvas.Invalidate(); };
                fontSub.Items.Add(item);
            }
            _flyout.Items.Add(fontSub);

            // font size
            var sizeSub = new MenuFlyoutSubItem { Text = "Font Size" };
            var sizes = new[] { 13.5, 14.5, 15.5, 16.5, 17.5 };
            foreach (var s in sizes)
            {
                var item = new MenuFlyoutItem { Text = $"{s} pt" };
                item.Click += (_, __) => { 
                    ModglassDisplay.CurrentFontSize = (float)s;
                    ModtermCanvas.Invalidate(); };
                sizeSub.Items.Add(item);
            }
            _flyout.Items.Add(sizeSub);

            // font glow
            var glowSub = new MenuFlyoutSubItem { Text = "UI Glow" };
            var glowSubAmts = new[] { 0F, 1F, 2F, 3F, 5F, 7F, 10F, 15F };
            foreach (var s in glowSubAmts)
            {
                var item = new MenuFlyoutItem { Text = $"{s} radius" };
                item.Click += (_, __) => { ModglassDisplay.BlurAmount = s; ModtermCanvas.Invalidate(); };
                glowSub.Items.Add(item);
            }
            _flyout.Items.Add(glowSub);

            // input color
            var inputColorSub = new MenuFlyoutSubItem { Text = "Input Color" };
            foreach (var (name, col) in _colorOptions)
            {
                var item = new MenuFlyoutItem { Text = name };
                item.Click += (_, __) => { ModglassDisplay.InputColor = col; ModtermCanvas.Invalidate(); };
                inputColorSub.Items.Add(item);
            }
            _flyout.Items.Add(inputColorSub);

            // output color
            var outputColorSub = new MenuFlyoutSubItem { Text = "Output Color" };
            foreach (var (name, col) in _colorOptions)
            {
                var item = new MenuFlyoutItem { Text = name };
                item.Click += (_, __) => { ModglassDisplay.OutputColor = col; ModtermCanvas.Invalidate(); };
                outputColorSub.Items.Add(item);
            }
            _flyout.Items.Add(outputColorSub);

            // shell selection
            var shellSub = new MenuFlyoutSubItem { Text = "Shell" };
            foreach (var (name, shell) in _shellEnv)
            {
                var item = new MenuFlyoutItem { Text = name };
                item.Click += async (_, __) =>
                {
                    _terminal.Started = false;
                    _terminal.Dispose();
                    await Task.Delay(1000); // Pauses for 1 second without blocking the UI thread
                    _terminal = new ConPTYTerminal();
                    _terminal.OutputReceived += OnOutputReceived;
                    _terminal.Start(shell, "");
                };
                shellSub.Items.Add(item);
            }
            _flyout.Items.Add(shellSub);
        }

        private string GetColorHexString(Color color) 
        {
            return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        }

        private string GetCurrentTintTransparencyInfo()
        {
            return 
                $"Tint: {GetColorHexString(ModglassDisplay.TintColor)} @ {ModglassDisplay.TransparencyPct}% transparency " +
                $"Color {_bgTintDriftColorOffset} of {_bgTintDriftColors.Count} set."; 
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
