using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Text;
using System.Numerics;
using Windows.Foundation;
using Windows.UI;
using Windows.ApplicationModel.DataTransfer;
using Microsoft.UI.Dispatching;
using System.Windows;
using Microsoft.UI.Text;

namespace modterm
{
    public partial class ModtermRender
    {
        public ModtermWindow ModtermWinInstance { get; set; } = null!;
        public int Lines { get { return _lines; } }
        public int Columns { get { return _columns; } }
        public int ScrollOffset { get { return _scrollOffset; } set { _scrollOffset = value; } }
        public XtermSharp.Terminal Terminal { get { return _terminal; } }
        public int TopRow { get { return _terminal.Buffer.YBase; } }
        public UserAppConfiguration UserAppConfiguration { get; set; } = null!;
        public bool IsSelecting { get { return _isSelecting; } set { _isSelecting = value; } }
        public TextRange? SelectionRange { get { return _selectionRange; } set { _selectionRange = value; } }
        public string SelectedText { get { return _selectedText; } set { _selectedText = value; } }
        public Point SelectionStart { get { return _selectionStart; } set { _selectionStart = value; } }
        public Point SelectionEnd { get { return _selectionEnd; } set { _selectionEnd = value; } }
        public int SelectionTopRow { get { return _selectionTopRow; } set { _selectionTopRow = value; } }

        private DisplayLabelGroup _titleBarLabels = null!;
        private DisplayLabel _shellInfoLabel = null!;
        private DisplayLabel _appearanceInfoLabel = null!;
        private DisplayLabel _linesColsInfoLabel = null!;
        private DispatcherQueueTimer _cursorTimer = null!;
        private int _cursorSpeed = 500;
        private bool _cursorVisible = true;
        private XtermSharp.Terminal _terminal = null!;
        // DECSCNM (screen-wide reverse video) is not tracked by XtermSharp; kept false.
        private bool _screenReverse = false;
        private string _currentFont = BundledFonts.BlexMonoNerdFontFamilyName;
        private string _currentControlFont = BundledFonts.BlexMonoNerdFontFamilyName;
        private float _currentFontSize = 12f;
        private float _controlFontScale = 0.777f;
        private float _currentControlFontSize = 12f;
        private float _controlPadding = 5f;
        private Color _labelColor;
        private Color _labelBlurColor;
        private Color _outputColor;
        private Color _outputBlurColor;
        private int _opacityPct;
        private int _scrollOffset = 0;
        private bool _isSelecting = false;
        private float _blurAmount;
        private TextRange? _selectionRange;
        private string _selectedText = string.Empty;
        private Point _selectionStart;
        private Point _selectionEnd;
        private int _selectionTopRow = 0;
        private byte _alpha;
        private Color _windowColor;
        private Color?[] _terminalPaletteOverrides = new Color?[16];
        private SolidColorBrush _backgroundBrush = new SolidColorBrush(Colors.Red);
        private bool _effectSequenceStarted = false;
        private List<DrawTextCall> _effectSequence = new List<DrawTextCall>();
        private CanvasDrawingSession _drawSession = null!;
        private CanvasControl _sender = null!;

        private CanvasTextFormat _currentTextFormat = null!;
        private CanvasTextFormat _currentControlTextFormat = null!;

        private int _lines = 0;
        private int _columns = 0;
        private float _measuredCharWidth;

        private int _leftTextPadding = 5;
        private int _topTextPadding = 28;//33;
        private float _lineHeightPadding = 1.0f;

        private CanvasTextFormat? _normalTextFormat;
        private CanvasTextFormat? _boldTextFormat;
        private string? _cachedFontFamily;
        private float _cachedFontSize;
        private readonly StringBuilder _runBuffer = new StringBuilder(256);
        private readonly string FullBrailleCell = "\u28FF";
        private readonly Dictionary<CanvasTextFormat, Rect> _brailleCellInkBounds = new Dictionary<CanvasTextFormat, Rect>();

        public string CurrentFont 
        { 
            get 
            {
                return _currentFont;
            }
            set 
            {
                _currentFont = value;
                _currentTextFormat.FontFamily = BundledFonts.ResolveFontFamily(_currentFont);
                _normalTextFormat = null;
                _boldTextFormat = null;
            }
        }

        public string CurrentControlFont 
        { 
            get { return _currentControlFont; }
            set
            {
                _currentControlFont = value;
                _currentControlTextFormat.FontFamily = BundledFonts.ResolveFontFamily(_currentControlFont);
            }
        }
        
        public float CurrentFontSize
        {
            get
            {
                return _currentFontSize;
            }
            set
            {
                _currentFontSize = value;
                _currentControlFontSize = CurrentFontSize * _controlFontScale;
                _currentTextFormat.FontSize = _currentFontSize;
                _currentControlTextFormat.FontSize = _currentControlFontSize;
                _controlPadding = _currentControlFontSize / 1.75f;
            }
        }

        public CanvasTextFormat CurrentControlTextFormat 
        {
            get { return _currentControlTextFormat; } 
            set { _currentControlTextFormat = value; }
        }

        public int OpacityPct
        {
            get
            {
                return _opacityPct;
            }
            set
            {
                _opacityPct = value;
                _alpha = (byte)(_opacityPct * 2.55);
                _backgroundBrush.Color = GetBackgroundArgb();
            }
        }

        public void Initialize()
        {
            
            // Initialize the XtermSharp terminal engine. Size is corrected on first draw
            // once the canvas measures how many rows/columns fit.
            // ConvertEol must be false: ConPTY moves the cursor down a row (keeping the
            // column) with a bare LF and emits an explicit CR when it wants column 0.
            // Converting LF to CR+LF pulls the cursor to column 0 mid-frame, which made
            // ECH erase box borders (btop) and misplace delta rows (gitui).
            _terminal = new XtermSharp.Terminal(
                new ModtermTerminalDelegate(ModtermWinInstance),
                new XtermSharp.TerminalOptions { Cols = 80, Rows = 25, Scrollback = 5000, ConvertEol = false });

            // set default values
            _currentTextFormat = new CanvasTextFormat
            {
                FontFamily = BundledFonts.ResolveFontFamily(_currentFont),
                FontSize = _currentFontSize,
                WordWrapping = CanvasWordWrapping.NoWrap
            };
            _currentControlTextFormat = new CanvasTextFormat
            {
                FontFamily = BundledFonts.ResolveFontFamily(_currentControlFont),
                FontSize = _currentControlFontSize,
                WordWrapping = CanvasWordWrapping.NoWrap
            };

            _cursorTimer = ModtermWinInstance.DispatcherQueue!.CreateTimer();
            _cursorTimer.Interval = TimeSpan.FromMilliseconds(_cursorSpeed);
            _cursorTimer.Tick += (s, e) =>
            {
                _cursorVisible = !_cursorVisible;
                ModtermWinInstance.InvalidateModtermCanvas();
            };
            _cursorTimer.Start();

            InitializeDisplayLabels();
        }

        public void InitializeDisplayLabels()
        {
            // title bar labels
            _titleBarLabels = new DisplayLabelGroup(
                DisplayLabelGroup.LabelDock.Top, _controlPadding);

            _shellInfoLabel = new DisplayLabel("", true);
            _appearanceInfoLabel = new DisplayLabel("", true);
            _linesColsInfoLabel = new DisplayLabel("", true);

            _titleBarLabels.Labels.AddRange(
                [_shellInfoLabel, _appearanceInfoLabel, _linesColsInfoLabel]);
        }

        public void UpdateTitleBarLabels()
        {
            // path and appearance info labels
            _shellInfoLabel.TextContent = $"shell: {UserAppConfiguration.TerminalShell.Name ?? "unknown"}";
            _appearanceInfoLabel.TextContent = $"{UserAppConfiguration.ThemeConfiguration.BackdropKind.ToString() ?? "unknown"}/#{_alpha:X2}{GetHexStringFromColor(GetBackgroundBrush().Color).Substring(1)}";
            _linesColsInfoLabel.TextContent = $"{_lines}x{_columns}";

            ModtermWinInstance.InvalidateModtermCanvas();
        }

        public void ApplySystemBackdrop(BackdropKind kind, Window wInstance)
        {
            SystemBackdrop backdrop = kind switch
            {
                BackdropKind.Blurred => new BlurredBackdrop(),
                BackdropKind.Mica => new MicaBackdrop(),
                BackdropKind.Acrylic => new DesktopAcrylicBackdrop(),
                _ => new BlurredBackdrop()
            };
            wInstance.SystemBackdrop = backdrop;
        }

        private Color GetBackgroundArgb()
        {
            return _windowColor == Colors.Transparent
                ? Colors.Transparent
                : Color.FromArgb(_alpha, _windowColor.R, _windowColor.G, _windowColor.B);
        }

        public void SetColorConfiguration(ThemeConfiguration config, Window wInstance)
        {
            _outputColor = config.OutputColor;
            _outputBlurColor = config.OutputBlurColor;
            _labelColor = config.LabelColor;
            _labelBlurColor = config.LabelBlurColor;
            _blurAmount = config.BlurAmount;
            OpacityPct = config.WindowOpacityPct;
            _windowColor = config.WindowColor;
            ApplyTerminalPaletteOverrides(config.Palette);
            ApplySystemBackdrop(config.BackdropKind, wInstance);
            _backgroundBrush.Color = GetBackgroundArgb();
        }

        private void ApplyTerminalPaletteOverrides(Dictionary<string, Color>? palette)
        {
            Array.Clear(_terminalPaletteOverrides, 0, _terminalPaletteOverrides.Length);
            if (palette is null || palette.Count == 0)
            {
                return;
            }

            for (int i = 0; i < TerminalPalette.StandardNames.Length; i++)
            {
                if (TerminalPalette.TryGetColor(palette, i, out Color color))
                {
                    _terminalPaletteOverrides[i] = color;
                }
            }
        }

        public SolidColorBrush GetBackgroundBrush()
        {
            return _backgroundBrush;
        }

        public void UpdateSelectedText()
        {
            _selectionRange = null;
            _selectedText = string.Empty;

            if (Lines <= 0 || Columns <= 0 || _measuredCharWidth <= 0)
                return;

            if (Math.Abs(_selectionStart.X - _selectionEnd.X) < 2 &&
                Math.Abs(_selectionStart.Y - _selectionEnd.Y) < 2)
                return;

            _selectionRange = new TextRange
            {
                Start = GetTextPositionFromPoint(_selectionStart),
                End = GetTextPositionFromPoint(_selectionEnd)
            };

            _selectedText = GetText(_selectionRange);
        }

        // Stream-based (reading-order) text extraction across the selection span, matching
        // the behavior of the previous engine. Rows are absolute buffer indices.
        private string GetText(TextRange range)
        {
            int startCol = range.Start.Column;
            int startRow = range.Start.Row;
            int endCol = range.End.Column;
            int endRow = range.End.Row;

            if (startRow > endRow || (startRow == endRow && startCol > endCol))
            {
                (startCol, endCol) = (endCol, startCol);
                (startRow, endRow) = (endRow, startRow);
            }

            var lines = _terminal.Buffer.Lines;
            if (startRow < 0 || startRow >= lines.Length)
                return string.Empty;

            var sb = new StringBuilder();
            for (int row = startRow; row <= endRow && row < lines.Length; row++)
            {
                int c0 = (row == startRow) ? startCol : 0;
                int c1 = (row == endRow) ? endCol : _columns - 1;

                if (row != startRow)
                    sb.Append('\n');

                var line = lines[row];
                if (line == null)
                    continue;

                for (int i = c0; i <= c1 && i < line.Length; i++)
                {
                    var cd = line[i];
                    sb.Append(cd.Code == 0 ? ' ' : (char)(uint)cd.Rune);
                }
            }

            return sb.ToString();
        }

        public bool IsInTextArea(Point point)
        {
            if (_lines <= 0 || _columns <= 0 || _measuredCharWidth <= 0)
                return false;

            double lineHeight = CurrentFontSize + _lineHeightPadding;
            double textRight = _leftTextPadding + (_columns * _measuredCharWidth);
            double textBottom = _topTextPadding + (_lines * lineHeight);

            return point.X >= _leftTextPadding &&
                point.X <= textRight &&
                point.Y >= _topTextPadding &&
                point.Y <= textBottom;
        }

        public TextPosition GetTextPositionFromPoint(Point point)
        {
            double lineHeight = CurrentFontSize + _lineHeightPadding;
            int column = (int)Math.Floor((point.X - _leftTextPadding) / _measuredCharWidth);
            int visibleRow = (int)Math.Floor((point.Y - _topTextPadding) / lineHeight);
            int topRow = _isSelecting ? _selectionTopRow : _terminal.Buffer.YBase - _scrollOffset;

            column = Math.Clamp(column, 0, Math.Max(0, Columns - 1));
            visibleRow = Math.Clamp(visibleRow, 0, Math.Max(0, Lines - 1));

            return new TextPosition
            {
                Column = column,
                Row = topRow + visibleRow
            };
        }

        public void CopySelectedTextToClipboard()
        {
            if (string.IsNullOrEmpty(_selectedText))
                return;

            DataPackage dataPackage = new DataPackage();
            dataPackage.SetText(_selectedText.Replace("\n", Environment.NewLine));
            Clipboard.SetContent(dataPackage);
            Clipboard.Flush();
        }

        public async void PasteFromClipboard()
        {
            var dataPackageView = Clipboard.GetContent();
            if (dataPackageView.Contains(StandardDataFormats.Text))
            {
                string text = await dataPackageView.GetTextAsync();
                if (!string.IsNullOrEmpty(text))
                {
                    _scrollOffset = 0;
                    ModtermWinInstance.ConPtyTerminal?.WriteInput(text);
                }
                ModtermWinInstance.InvalidateModtermCanvas();
            }
        }

        public void OnOutputReceived(object? sender, byte[] data)
        {
            // Feed raw PTY bytes to the VT parser (preserves UTF-8 split across reads).
            if (_scrollOffset > 0 && !_isSelecting) _scrollOffset = 0;
            if (data is { Length: > 0 })
            {
                _terminal.Feed(data, data.Length);
                ModtermWinInstance.InvalidateModtermCanvas();
            }
        }

        public void ScrollBackBy(int rows)
        {
            if (rows == 0)
                return;

            int previousOffset = _scrollOffset;
            _scrollOffset += rows;
            ClampScrollOffset();

            if (_scrollOffset != previousOffset)
                ModtermWinInstance.InvalidateModtermCanvas();
        }

        private void ClampScrollOffset()
        {
            int maxScrollOffset = Math.Max(0, _terminal.Buffer.YBase);
            _scrollOffset = Math.Clamp(_scrollOffset, 0, maxScrollOffset);
        }

        /// <summary>
        /// Recomputes the row/column grid from the current canvas size and applies it live,
        /// resizing both the emulator buffer and the pseudo console without restarting the
        /// shell. The shell receives the new dimensions via ConPTY and repaints.
        /// </summary>
        public void ResizeToCanvas(double actualWidth, double actualHeight)
        {
            if (_measuredCharWidth <= 0 || !ModtermWinInstance.ConPtyTerminal.Started)
                return;

            double lineHeight = CurrentFontSize + _lineHeightPadding;
            int rows = (int)((actualHeight - _topTextPadding) / lineHeight);
            int cols = (int)((actualWidth - _leftTextPadding) / _measuredCharWidth);

            if (rows <= 0 || cols <= 0)
                return;
            if (rows == _lines && cols == _columns)
                return;

            _lines = rows;
            _columns = cols;

            // Reflow the emulator buffer first, then notify the pseudo console so the shell
            // sees the new size and redraws against the reflowed contents.
            _terminal.Resize(cols, rows);
            ModtermWinInstance.ConPtyTerminal.Resize((short)cols, (short)rows);

            _scrollOffset = 0;
            UpdateTitleBarLabels();
            ModtermWinInstance.InvalidateModtermCanvas();
        }

        public void BeginEffectSequence(CanvasControl sender, CanvasDrawingSession ds)
        {
            _drawSession = ds;
            if (_effectSequenceStarted)
            {
                throw new InvalidOperationException("Effect sequence already started.");
            }
            else
            {
                _effectSequenceStarted = true;
                _effectSequence.Clear();
            }
        }

        public void EndEffectSequence()
        {
            if (_effectSequenceStarted)
            {
                DrawEffectSequence();
                _effectSequenceStarted = false;
                _effectSequence.Clear();
            }
            else
            {
                throw new InvalidOperationException("Effect sequence was not started.");
            }
        }

        public void DrawText(string text, float x, float y, float width, Color color, Color bgColor, CanvasTextFormat textFormat, bool foregroundIsDefault = false, bool backgroundIsDefault = false, bool fitToCell = false, float cellHeight = 0f)
        {
            _effectSequence.Add(new DrawTextCall(text, x, y, width, color, bgColor, textFormat, foregroundIsDefault, backgroundIsDefault, fitToCell, cellHeight));
        }

        public void ModtermCanvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
        {
            _sender = sender;
            // Do not spawn the conhost until we can measure the canvas during drawing and determine how many rows/columns we can fit
            if (!ModtermWinInstance.ConPtyTerminal.Started)
            {
                int measuredRows = (int)((sender.ActualHeight - _topTextPadding) / (CurrentFontSize + _lineHeightPadding));
                float measuredCharWidth = MeasureCellAdvance(args.DrawingSession, _currentTextFormat);
                if (measuredRows <= 0 || measuredCharWidth <= 0 || float.IsNaN(measuredCharWidth) || float.IsInfinity(measuredCharWidth))
                    return;

                int measuredCols = (int)((sender.ActualWidth - _leftTextPadding) / measuredCharWidth);
                if (measuredCols <= 0)
                    return;

                _lines = measuredRows;
                _columns = measuredCols;
                _measuredCharWidth = measuredCharWidth;
                _terminal.Resize(_columns, _lines);

                var terminal = ModtermWinInstance.EnsureTerminalInstanceForStart();
                if (terminal.Started)
                    return;

                terminal.Start(UserAppConfiguration.TerminalShell, Lines, Columns);

                _terminal.Resize(Columns, Lines);
                // CreatePseudoConsole already sized the PTY; avoid an immediate redundant resize.
            }

            BeginEffectSequence(sender, args.DrawingSession);

            // Keep the VT controller's TopRow as the live screen position; scrollback only changes what we render.
            ClampScrollOffset();
            int topRow = _terminal.Buffer.YBase - _scrollOffset;
            var selectionRange = _isSelecting ? _selectionRange : null;
            double lineHeight = CurrentFontSize + _lineHeightPadding;

            EnsureTextFormats();

            for (int visibleRow = 0; visibleRow < _lines; visibleRow++)
            {
                int logicalRow = topRow + visibleRow;
                float y = _topTextPadding + (float)(visibleRow * lineHeight);
                var sourceLine = (logicalRow >= 0 && logicalRow < _terminal.Buffer.Lines.Length)
                    ? _terminal.Buffer.Lines[logicalRow]
                    : null;

                // Run-batching state: accumulate contiguous cells with identical SGR
                // attributes so the row can be drawn as a small number of DrawText calls
                // instead of one per cell. Unsafe glyphs (box-drawing, combining marks,
                // etc.) break the run and are drawn individually to preserve alignment.
                bool runActive = false;
                int runStartCol = 0;
                Color runFg = default;
                Color runBg = default;
                bool runFgDefault = false;
                bool runBgDefault = false;
                CanvasTextFormat? runFormat = null;
                _runBuffer.Clear();

                for (int col = 0; col < _columns; col++)
                {
                    XtermSharp.CharData cd = (sourceLine != null && col < sourceLine.Length)
                        ? sourceLine[col]
                        : XtermSharp.CharData.Null;

                    XtermAttr.Decode(cd.Attribute, out int fgIdx, out int bgIdx, out XtermSharp.FLAGS flags);

                    bool inverted = selectionRange != null && selectionRange.Contains(col, logicalRow);
                    bool cellReverse = (flags & XtermSharp.FLAGS.INVERSE) != 0;
                    // screen-reverse (DECSCNM) XOR selection XOR per-cell reverse
                    bool swap = _screenReverse ^ inverted ^ cellReverse;

                    bool fgDefault = XtermAttr.IsDefault(fgIdx);
                    bool bgDefault = XtermAttr.IsDefault(bgIdx);

                    Color fg = fgDefault ? _outputColor : ResolvePaletteColor(fgIdx, _outputColor);
                    Color bg = bgDefault ? Colors.Black : ResolvePaletteColor(bgIdx, Colors.Black);

                    if (swap)
                    {
                        (fg, bg) = (bg, fg);
                        (fgDefault, bgDefault) = (bgDefault, fgDefault);

                        // Selection inversion swaps default fg/bg indices too, which would
                        // suppress the highlight fill. Treat the inverted colors as explicit.
                        if (inverted)
                        {
                            fgDefault = false;
                            bgDefault = false;
                        }
                    }

                    if ((flags & XtermSharp.FLAGS.INVISIBLE) != 0)
                        fg = bg;

                    // A cell holds a single code point (no multi-codepoint combining marks).
                    string runeString = cd.Code == 0 ? " " : RuneToString((uint)cd.Rune);
                    char displayChar = runeString.Length == 1 ? runeString[0] : '\uFFFF';
                    string combining = string.Empty;

                    CanvasTextFormat cellFormat = (flags & XtermSharp.FLAGS.BOLD) != 0 ? _boldTextFormat! : _normalTextFormat!;

                    // Only batch cells whose glyph is known to use the primary monospace
                    // font's natural advance (printable ASCII). Box-drawing, braille,
                    // resolved missing glyphs, and combining sequences are drawn per-cell
                    // at the measured grid so they stay column-aligned.
                    bool canBatch = combining.Length == 0 && IsSafeForBatch(displayChar);

                    bool matchesRun = runActive
                        && ReferenceEquals(runFormat, cellFormat)
                        && runFg == fg
                        && runBg == bg
                        && runFgDefault == fgDefault
                        && runBgDefault == bgDefault;

                    if (runActive && (!canBatch || !matchesRun))
                    {
                        FlushRun(y, runStartCol, runFg, runBg, runFgDefault, runBgDefault, runFormat!);
                        runActive = false;
                    }

                    if (canBatch)
                    {
                        if (!runActive)
                        {
                            runActive = true;
                            runStartCol = col;
                            runFg = fg;
                            runBg = bg;
                            runFgDefault = fgDefault;
                            runBgDefault = bgDefault;
                            runFormat = cellFormat;
                        }
                        _runBuffer.Append(displayChar);
                    }
                    else
                    {
                        string cellText = runeString + combining;
                        float cellX = _leftTextPadding + (col * _measuredCharWidth);
                        // Braille patterns are absent from typical monospace fonts (Consolas),
                        // so they resolve through font fallback whose glyph cell is taller than
                        // our row. Scale those to the grid cell so TUI braille graphs stay within
                        // their rows instead of bleeding vertically.
                        bool fitToCell = IsBrailleChar(displayChar);
                        DrawText(
                            cellText,
                            cellX,
                            y,
                            _measuredCharWidth,
                            fg,
                            bg,
                            cellFormat,
                            fgDefault,
                            bgDefault,
                            fitToCell,
                            (float)lineHeight);
                    }
                }

                if (runActive)
                {
                    FlushRun(y, runStartCol, runFg, runBg, runFgDefault, runBgDefault, runFormat!);
                }
            }

            // Draw blinking cursor only on the live viewport.
            if (_cursorVisible && _scrollOffset == 0 && !_terminal.CursorHidden)
            {
                float cursorX = _leftTextPadding + (float)(_terminal.Buffer.X * _measuredCharWidth);
                float cursorY = (float)(_terminal.Buffer.Y * (CurrentFontSize + _lineHeightPadding)) + _topTextPadding + 2;
                //args.DrawingSession.DrawText("|", cursorX, cursorY, _outputColor, _currentTextFormat);
                args.DrawingSession.FillRectangle(cursorX, cursorY, _measuredCharWidth, (float)lineHeight, _outputColor);
            }

            EndEffectSequence();

            // draw all UI controls
            _titleBarLabels?.DrawLabels(sender, args.DrawingSession, this);
        }

        public void DrawModtermLabel(CanvasControl sender, CanvasDrawingSession cds, DisplayLabel label)
        {
            Color labelColor = _labelColor;
            Color labelBlurColor = _labelBlurColor;

            // blur layer - draw text content
            using (var commandList = new CanvasCommandList(sender))
            {
                using (var clds = commandList.CreateDrawingSession())
                {
                    clds.DrawText(label.TextContent, (float)label.Location.X + _controlPadding,
                        (float)label.Location.Y + _controlPadding / 4, labelBlurColor, _currentControlTextFormat);
                }

                var blurEffect = new GaussianBlurEffect { Source = commandList, BlurAmount = _blurAmount };
                cds.DrawImage(blurEffect);
            }

            
            // sharp layer - draw text content
            cds.DrawText(label.TextContent, (float)label.Location.X + _controlPadding,
                (float)label.Location.Y + _controlPadding / 4, labelColor, _currentControlTextFormat);

        }

        private void DrawEffectSequence()
        {
            // Blurred glow layer
            using (var commandList = new CanvasCommandList(_sender))
            {
                using (var clds = commandList.CreateDrawingSession())
                {
                    foreach (DrawTextCall call in _effectSequence)
                    {
                        // draw a background rectangle only for explicit cell backgrounds (not SGR default / host fill)
                        if (!call.BackgroundIsDefault)
                        {
                            clds.FillRectangle(call.X, call.Y, call.Width, call.Height, call.BackgroundColor);
                        }
                    }
                }
                var blurEffect = new GaussianBlurEffect { Source = commandList, BlurAmount = _blurAmount };
                _drawSession.DrawImage(blurEffect);
            }

            // Sharp layer
            foreach (DrawTextCall call in _effectSequence)
            {
                if (!call.BackgroundIsDefault)
                {
                    _drawSession.FillRectangle(call.X, call.Y, call.Width, call.Height, call.BackgroundColor);
                }
            }

            // Blurred glow layer
            using (var commandList = new CanvasCommandList(_sender))
            {
                using (var clds = commandList.CreateDrawingSession())
                {
                    foreach (DrawTextCall call in _effectSequence)
                    {
                        Color glyphColor = (call.ForegroundIsDefault || call.Color == _outputColor)
                            ? _outputBlurColor
                            : call.Color;

                        if (call.FitToCell)
                        {
                            DrawGlyphFitted(clds, call, glyphColor);
                        }
                        else
                        {
                            clds.DrawText(call.Text, call.X, call.Y, glyphColor, call.TextFormat);
                        }
                    }
                }
                var blurEffect = new GaussianBlurEffect { Source = commandList, BlurAmount = _blurAmount };
                _drawSession.DrawImage(blurEffect);
            }

            // Sharp layer
            foreach (DrawTextCall call in _effectSequence)
            {
                if (call.FitToCell)
                {
                    DrawGlyphFitted(_drawSession, call, call.Color);
                }
                else
                {
                    _drawSession.DrawText(call.Text.Replace(' ', '\u00A0'), call.X, call.Y, call.Color, call.TextFormat);
                }
            }
        }

         private float MeasureCellAdvance(CanvasDrawingSession ds, CanvasTextFormat format)
        {
            const int sampleLength = 32;
            using var layout = new CanvasTextLayout(ds, new string('0', sampleLength), format, 9999, 9999);
            float total = 0;
            foreach (var cluster in layout.ClusterMetrics)
                total += cluster.Width;
            return total / sampleLength;
        }

        private void EnsureTextFormats()
        {
            if (_normalTextFormat != null
                && _cachedFontFamily == CurrentFont
                && _cachedFontSize == CurrentFontSize)
            {
                return;
            }

            string resolvedFontFamily = BundledFonts.ResolveFontFamily(CurrentFont);
            _normalTextFormat = new CanvasTextFormat
            {
                FontFamily = resolvedFontFamily,
                FontSize = CurrentFontSize,
                FontWeight = FontWeights.Normal,
                WordWrapping = CanvasWordWrapping.NoWrap
            };
            _boldTextFormat = new CanvasTextFormat
            {
                FontFamily = resolvedFontFamily,
                FontSize = CurrentFontSize,
                FontWeight = FontWeights.Bold,
                WordWrapping = CanvasWordWrapping.NoWrap
            };
            _cachedFontFamily = CurrentFont;
            _cachedFontSize = CurrentFontSize;
        }

        private void FlushRun(float y, int startCol, Color fg, Color bg, bool fgDefault, bool bgDefault, CanvasTextFormat format)
        {
            float x = _leftTextPadding + (startCol * _measuredCharWidth);
            float width = _runBuffer.Length * _measuredCharWidth;
            DrawText(
                _runBuffer.ToString(),
                x,
                y,
                width,
                fg,
                bg,
                format,
                fgDefault,
                bgDefault);
            _runBuffer.Clear();
        }

        // Printable ASCII is rendered natively by the primary monospace font, so glyph
        // advances are known to equal _measuredCharWidth and runs stay column-aligned
        // when drawn as a single string. Anything outside this range may trigger font
        // fallback with a different advance, so we render those per-cell on the grid.
        private static bool IsSafeForBatch(char c) => c >= 0x20 && c <= 0x7E;

        // Braille Patterns block (U+2800-U+28FF), used by TUI apps (btop, etc.) for fine graphs.
        private static bool IsBrailleChar(char c) => c >= '\u2800' && c <= '\u28FF';      

        private Rect GetBrailleCellInkBounds(ICanvasResourceCreator resourceCreator, CanvasTextFormat format)
        {
            if (_brailleCellInkBounds.TryGetValue(format, out Rect cached))
                return cached;

            using var refLayout = new CanvasTextLayout(resourceCreator, FullBrailleCell, format, 0f, 0f)
            {
                WordWrapping = CanvasWordWrapping.NoWrap
            };

            // DrawBounds is the ink rectangle of the glyph as actually rasterized, including
            // any fallback substitution - unlike LayoutBounds, which uses the primary font's
            // (e.g. Consolas) line metrics and underestimates a taller fallback glyph.
            Rect bounds = refLayout.DrawBounds;

            // Avoid unbounded growth across font/size changes (only ~2 live formats normally).
            if (_brailleCellInkBounds.Count > 16)
                _brailleCellInkBounds.Clear();

            _brailleCellInkBounds[format] = bounds;
            return bounds;
        }

        // Draws a single braille glyph scaled to fill exactly one grid cell. The full-cell
        // ink reference (measured from the real, possibly fallback, font) is mapped onto the
        // terminal cell; because every braille glyph shares the same pen origin and design
        // cell, each glyph's dots land correctly and stay confined to their row instead of
        // overflowing vertically into neighbouring rows.
        private void DrawGlyphFitted(CanvasDrawingSession ds, DrawTextCall call, Color color)
        {
            Rect reference = GetBrailleCellInkBounds(ds, call.TextFormat);
            if (reference.Width <= 0 || reference.Height <= 0)
            {
                ds.DrawText(call.Text, call.X, call.Y, color, call.TextFormat);
                return;
            }

            using var layout = new CanvasTextLayout(ds, call.Text, call.TextFormat, 0f, 0f)
            {
                WordWrapping = CanvasWordWrapping.NoWrap
            };

            float scaleX = call.Width / (float)reference.Width;
            float scaleY = call.CellHeight / (float)reference.Height;

            Matrix3x2 prior = ds.Transform;
            ds.Transform =
                Matrix3x2.CreateTranslation((float)-reference.Left, (float)-reference.Top) *
                Matrix3x2.CreateScale(scaleX, scaleY) *
                Matrix3x2.CreateTranslation(call.X, call.Y);

            ds.DrawTextLayout(layout, 0f, 0f, color);
            ds.Transform = prior;
        }

        // Maps a 0-255 palette index to its RGB color; default/inverted-default sentinels
        // (256/257) fall back to the supplied theme color. Indices 0-15 may be overridden
        // by the active theme's optional Palette dictionary.
        private Color ResolvePaletteColor(int index, Color themeDefault)
        {
            if (index >= 0 && index < _terminalPaletteOverrides.Length
                && _terminalPaletteOverrides[index] is Color overrideColor)
            {
                return overrideColor;
            }

            var palette = XtermSharp.Color.DefaultAnsiColors;
            if (index >= 0 && index < palette.Count)
            {
                var c = palette[index];
                return Color.FromArgb(255, c.Red, c.Green, c.Blue);
            }

            return themeDefault;
        }

        // Converts a Unicode code point to a string, handling astral (supplementary) planes.
        private static string RuneToString(uint codePoint)
        {
            try
            {
                return char.ConvertFromUtf32((int)codePoint);
            }
            catch (ArgumentOutOfRangeException)
            {
                return "\uFFFD";
            }
        }

        public static string GetHexStringFromColor(Color color)
        {
            return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        }

        public static Color GetColorFromHexString(string hex)
        {
            if (hex.StartsWith("#")) hex = hex.Substring(1);
            byte a = 255, r = 0, g = 0, b = 0;
            if (hex.Length == 8)
            {
                a = Convert.ToByte(hex.Substring(0, 2), 16);
                r = Convert.ToByte(hex.Substring(2, 2), 16);
                g = Convert.ToByte(hex.Substring(4, 2), 16);
                b = Convert.ToByte(hex.Substring(6, 2), 16);
            }
            else if (hex.Length == 6)
            {
                r = Convert.ToByte(hex.Substring(0, 2), 16);
                g = Convert.ToByte(hex.Substring(2, 2), 16);
                b = Convert.ToByte(hex.Substring(4, 2), 16);
            }
            return Color.FromArgb(a, r, g, b);
        }
    }
}

