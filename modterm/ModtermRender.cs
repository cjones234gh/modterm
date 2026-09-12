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
using Microsoft.UI.Text;
using Windows.Graphics.DirectX;
using modterm.Ghostty;

namespace modterm
{
    public partial class ModtermRender
    {
        public ModtermWindow ModtermWinInstance { get; set; } = null!;
        public int Lines { get { return _lines; } }
        public int Columns { get { return _columns; } }
        public int ScrollOffset { get { return _scrollOffset; } set { _scrollOffset = value; } }
        internal GhosttyVtSession Terminal { get { return _terminal; } }
        public int TopRow { get { return 0; } }
        internal uint CellWidthPixels => (uint)Math.Max(1, Math.Round(_measuredCharWidth));
        internal uint CellHeightPixels => (uint)Math.Max(1, Math.Round(CurrentFontSize + _lineHeightPadding));
        internal uint PaddingLeft => (uint)Math.Max(0, _leftTextPadding);
        internal uint PaddingTop => (uint)Math.Max(0, _topTextPadding);
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
        private GhosttyVtSession _terminal = null!;
        private readonly GhosttyFrame _frame = new();
        private readonly GhosttyColorRgb[] _nativePalette = new GhosttyColorRgb[256];
        private readonly Dictionary<(uint Id, ulong Generation), CanvasBitmap> _kittyBitmaps = new();
        private string _currentFont = BundledFonts.BlexMonoNerdFontFamilyName;
        private string _currentControlFont = BundledFonts.BlexMonoNerdFontFamilyName;
        private string _currentCursorStyle = TerminalCursorStyles.Solid;
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
        private CanvasTextFormat? _italicTextFormat;
        private CanvasTextFormat? _boldItalicTextFormat;
        private string? _cachedFontFamily;
        private float _cachedFontSize;
        private float _boldHorizontalScale = 1f;
        private bool _boldAdvanceReady;
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
                _italicTextFormat = null;
                _boldItalicTextFormat = null;
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

        public string CurrentCursorStyle
        {
            get => _currentCursorStyle;
            set
            {
                _currentCursorStyle = TerminalCursorStyles.Normalize(value);
                _terminal?.SetDefaultCursor(TerminalCursorStyles.IsUnderline(_currentCursorStyle), blink: true);
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
            
            GhosttyPngDecoder.Install();
            _terminal = new GhosttyVtSession(80, 25, GhosttyVtSession.DefaultScrollbackLines);
            _terminal.WritePty += bytes => ModtermWinInstance.ConPtyTerminal?.WriteInput(bytes);
            _terminal.ClipboardText += text =>
            {
                ModtermWinInstance.DispatcherQueue.TryEnqueue(() =>
                {
                    DataPackage dataPackage = new DataPackage();
                    dataPackage.SetText(text.Replace("\n", Environment.NewLine));
                    Clipboard.SetContent(dataPackage);
                    Clipboard.Flush();
                });
            };
            GhosttyVtSession.FillDefaultPalette(_nativePalette);

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
                if (_frame.CursorBlinking)
                    _cursorVisible = !_cursorVisible;
                else
                    _cursorVisible = true;
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
            PushPaletteToEmulator();
        }

        private void ApplyTerminalPaletteOverrides(Dictionary<string, Color>? palette)
        {
            Array.Clear(_terminalPaletteOverrides, 0, _terminalPaletteOverrides.Length);
            GhosttyVtSession.FillDefaultPalette(_nativePalette);
            if (palette is not null && palette.Count > 0)
            {
                for (int i = 0; i < TerminalPalette.StandardNames.Length; i++)
                {
                    if (TerminalPalette.TryGetColor(palette, i, out Color color))
                    {
                        _terminalPaletteOverrides[i] = color;
                        _nativePalette[i] = GhosttyColorRgb.FromBytes(color.R, color.G, color.B);
                    }
                }
            }

            PushPaletteToEmulator();
        }

        private void PushPaletteToEmulator()
        {
            if (_terminal is null)
                return;

            GhosttyColorRgb fg = GhosttyColorRgb.FromBytes(_outputColor.R, _outputColor.G, _outputColor.B);
            Color window = GetBackgroundArgb();
            GhosttyColorRgb bg = window.A == 0
                ? GhosttyColorRgb.FromBytes(0, 0, 0)
                : GhosttyColorRgb.FromBytes(window.R, window.G, window.B);
            _terminal.ApplyPalette(_nativePalette, fg, bg);
            _terminal.SetDefaultCursor(TerminalCursorStyles.IsUnderline(_currentCursorStyle), blink: true);
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
            {
                _terminal.ClearSelection();
                return;
            }

            _selectionRange = new TextRange
            {
                Start = GetTextPositionFromPoint(_selectionStart),
                End = GetTextPositionFromPoint(_selectionEnd)
            };

            _terminal.SetSelection(
                _selectionRange.Start.Column,
                _selectionRange.Start.Row,
                _selectionRange.End.Column,
                _selectionRange.End.Row);
            _selectedText = GetSelectedText();
        }

        private string GetSelectedText()
        {
            return _terminal?.GetSelectedText() ?? string.Empty;
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

        /// <summary>
        /// Maps a canvas point to a 0-based viewport cell. Returns false when the
        /// grid is not ready. When <paramref name="clamp"/> is true, out-of-range
        /// points are snapped to the nearest cell (used for drag tracking).
        /// </summary>
        public bool TryGetViewportCell(Point point, bool clamp, out int col, out int row)
        {
            col = 0;
            row = 0;
            if (_lines <= 0 || _columns <= 0 || _measuredCharWidth <= 0)
                return false;

            double lineHeight = CurrentFontSize + _lineHeightPadding;
            col = (int)Math.Floor((point.X - _leftTextPadding) / _measuredCharWidth);
            row = (int)Math.Floor((point.Y - _topTextPadding) / lineHeight);

            if (clamp)
            {
                col = Math.Clamp(col, 0, Math.Max(0, _columns - 1));
                row = Math.Clamp(row, 0, Math.Max(0, _lines - 1));
                return true;
            }

            if (col < 0 || row < 0 || col >= _columns || row >= _lines)
                return false;

            return true;
        }

        public TextPosition GetTextPositionFromPoint(Point point)
        {
            double lineHeight = CurrentFontSize + _lineHeightPadding;
            int column = (int)Math.Floor((point.X - _leftTextPadding) / _measuredCharWidth);
            int visibleRow = (int)Math.Floor((point.Y - _topTextPadding) / lineHeight);
            int topRow = 0;
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
                    _terminal.ScrollToBottom();
                    ModtermWinInstance.ConPtyTerminal?.WriteInput(_terminal.EncodePaste(text));
                }
                ModtermWinInstance.InvalidateModtermCanvas();
            }
        }

        public void OnOutputReceived(object? sender, byte[] data)
        {
            if (_scrollOffset > 0 && !_isSelecting)
            {
                _scrollOffset = 0;
                _terminal.ScrollToBottom();
            }
            if (data is { Length: > 0 })
            {
                _terminal.Write(data);
                ModtermWinInstance.InvalidateModtermCanvas();
            }
        }

        public void ScrollBackBy(int rows)
        {
            if (rows == 0)
                return;

            _terminal.ScrollBackBy(rows);
            _scrollOffset = _terminal.IsScrolledBack ? 1 : 0;
            ModtermWinInstance.InvalidateModtermCanvas();
        }

        public void FollowLiveOutput()
        {
            _scrollOffset = 0;
            _terminal?.ScrollToBottom();
        }

        public void ClearHostSelection()
        {
            _isSelecting = false;
            _selectionRange = null;
            _selectedText = string.Empty;
            _terminal?.ClearSelection();
        }

        public void ResizeEmulatorToGrid()
        {
            if (_terminal is null || _columns <= 0 || _lines <= 0 || _measuredCharWidth <= 0)
                return;

            _terminal.Resize(_columns, _lines, CellWidthPixels, CellHeightPixels);
        }

        private void ClampScrollOffset()
        {
            _scrollOffset = _terminal.IsScrolledBack ? Math.Max(1, _scrollOffset) : 0;
        }

        public void ResetEmulator()
        {
            if (_terminal is null)
                return;

            _scrollOffset = 0;
            _isSelecting = false;
            _selectionRange = null;
            _selectedText = string.Empty;
            _terminal.Reset();
            foreach (CanvasBitmap bitmap in _kittyBitmaps.Values)
                bitmap.Dispose();
            _kittyBitmaps.Clear();
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

            _terminal.Resize(cols, rows, CellWidthPixels, CellHeightPixels);
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

        public void DrawText(string text, float x, float y, float width, Color color, Color bgColor, CanvasTextFormat textFormat, bool foregroundIsDefault = false, bool backgroundIsDefault = false, bool fitToCell = false, float cellHeight = 0f, float horizontalScale = 1f, int underline = 0, bool strikethrough = false)
        {
            _effectSequence.Add(new DrawTextCall(text, x, y, width, color, bgColor, textFormat, foregroundIsDefault, backgroundIsDefault, fitToCell, cellHeight, horizontalScale, underline, strikethrough));
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
                double startLineHeight = CurrentFontSize + _lineHeightPadding;
                _terminal.Resize(_columns, _lines, (uint)Math.Max(1, _measuredCharWidth), (uint)Math.Max(1, startLineHeight));
                PushPaletteToEmulator();

                var terminal = ModtermWinInstance.EnsureTerminalInstanceForStart();
                if (terminal.Started)
                    return;

                ModtermWinInstance.TryStartCurrentShell(terminal, Lines, Columns);
            }

            BeginEffectSequence(sender, args.DrawingSession);

            ClampScrollOffset();
            double lineHeight = CurrentFontSize + _lineHeightPadding;

            EnsureTextFormats();
            EnsureBoldAdvanceScale(args.DrawingSession);
            _terminal.Capture(_frame);

            DrawKittyImages(args.DrawingSession, lineHeight, belowText: true);
            DrawCapturedCells((float)lineHeight);
            EndEffectSequence();

            DrawKittyImages(args.DrawingSession, lineHeight, belowText: false);
            DrawBlinkingCursor(args.DrawingSession, lineHeight);
            _titleBarLabels?.DrawLabels(sender, args.DrawingSession, this);
        }

        private void DrawCapturedCells(float lineHeight)
        {
            int rows = Math.Min(_lines, _frame.Rows);
            int cols = Math.Min(_columns, _frame.Cols);
            if (rows <= 0 || cols <= 0 || _frame.Cells.Length == 0)
                return;

            for (int visibleRow = 0; visibleRow < rows; visibleRow++)
            {
                float y = _topTextPadding + (visibleRow * lineHeight);
                bool runActive = false;
                int runStartCol = 0;
                Color runFg = default;
                Color runBg = default;
                bool runFgDefault = false;
                bool runBgDefault = false;
                bool runStrikethrough = false;
                int runUnderline = 0;
                CanvasTextFormat? runFormat = null;
                _runBuffer.Clear();

                for (int col = 0; col < cols; col++)
                {
                    GhosttyCell cell = _frame.Cells[visibleRow * _frame.Cols + col];
                    if (cell.Width == 0)
                    {
                        if (runActive)
                        {
                            FlushRun(y, runStartCol, runFg, runBg, runFgDefault, runBgDefault, runFormat!, lineHeight, runUnderline, runStrikethrough);
                            runActive = false;
                        }
                        continue;
                    }

                    ResolveCapturedCellColors(in cell, out Color fg, out Color bg, out bool fgDefault, out bool bgDefault);
                    if (cell.Invisible)
                        fg = bg;
                    if (cell.Faint)
                        fg = Color.FromArgb((byte)Math.Max(40, fg.A / 2), fg.R, fg.G, fg.B);

                    string runeString = string.IsNullOrEmpty(cell.Text) ? " " : cell.Text;
                    char displayChar = runeString.Length == 1 ? runeString[0] : '\uFFFF';
                    CanvasTextFormat cellFormat = FormatFor(cell.Bold, cell.Italic);
                    bool canBatch = runeString.Length == 1 && IsSafeForBatch(displayChar);

                    bool matchesRun = runActive
                        && ReferenceEquals(runFormat, cellFormat)
                        && runFg == fg
                        && runBg == bg
                        && runFgDefault == fgDefault
                        && runBgDefault == bgDefault
                        && runUnderline == cell.Underline
                        && runStrikethrough == cell.Strikethrough;

                    if (runActive && (!canBatch || !matchesRun))
                    {
                        FlushRun(y, runStartCol, runFg, runBg, runFgDefault, runBgDefault, runFormat!, lineHeight, runUnderline, runStrikethrough);
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
                            runUnderline = cell.Underline;
                            runStrikethrough = cell.Strikethrough;
                            runFormat = cellFormat;
                        }
                        _runBuffer.Append(displayChar);
                    }
                    else
                    {
                        float cellX = _leftTextPadding + (col * _measuredCharWidth);
                        bool fitToCell = runeString.Length == 1 && IsBrailleChar(displayChar);
                        DrawText(
                            runeString,
                            cellX,
                            y,
                            _measuredCharWidth * Math.Max(1, (int)cell.Width),
                            fg,
                            bg,
                            cellFormat,
                            fgDefault,
                            bgDefault,
                            fitToCell,
                            lineHeight,
                            HorizontalScaleFor(cellFormat),
                            cell.Underline,
                            cell.Strikethrough);
                    }
                }

                if (runActive)
                    FlushRun(y, runStartCol, runFg, runBg, runFgDefault, runBgDefault, runFormat!, lineHeight, runUnderline, runStrikethrough);
            }
        }

        private CanvasTextFormat FormatFor(bool bold, bool italic)
        {
            if (bold && italic)
                return _boldItalicTextFormat!;
            if (bold)
                return _boldTextFormat!;
            if (italic)
                return _italicTextFormat!;
            return _normalTextFormat!;
        }

        private void ResolveCapturedCellColors(
            in GhosttyCell cell,
            out Color fg,
            out Color bg,
            out bool fgDefault,
            out bool bgDefault)
        {
            fgDefault = cell.FgDefault && !cell.Selected;
            bgDefault = cell.BgDefault && !cell.Selected;
            fg = fgDefault ? _outputColor : Color.FromArgb(255, cell.Fg.R, cell.Fg.G, cell.Fg.B);
            bg = bgDefault ? Colors.Transparent : Color.FromArgb(255, cell.Bg.R, cell.Bg.G, cell.Bg.B);

            if (cell.Selected)
            {
                Color selectedFg = bgDefault ? InverseForegroundColor() : (bg.A == 0 ? InverseForegroundColor() : bg);
                Color selectedBg = fgDefault ? Opaque(_outputColor) : Opaque(fg);
                fg = selectedFg;
                bg = selectedBg;
                fgDefault = false;
                bgDefault = false;
            }
        }

        private void DrawKittyImages(CanvasDrawingSession ds, double lineHeight, bool belowText)
        {
            if (_frame.Images.Count == 0)
                return;

            foreach (KittyImageBlit blit in _frame.Images)
            {
                if (blit.BelowText != belowText)
                    continue;
                if (blit.Rgba.Length == 0 || blit.ImageWidth <= 0 || blit.ImageHeight <= 0)
                    continue;

                CanvasBitmap bitmap = GetOrCreateKittyBitmap(ds, blit);
                float x = _leftTextPadding + (blit.ViewportColumn * _measuredCharWidth) + blit.XOffset;
                float y = _topTextPadding + (float)(blit.ViewportRow * lineHeight) + blit.YOffset;
                float width = blit.PixelWidth > 0 ? blit.PixelWidth : blit.ImageWidth;
                float height = blit.PixelHeight > 0 ? blit.PixelHeight : blit.ImageHeight;
                var dest = new Rect(x, y, width, height);
                if (blit.SourceWidth > 0 && blit.SourceHeight > 0)
                {
                    var source = new Rect(blit.SourceX, blit.SourceY, blit.SourceWidth, blit.SourceHeight);
                    ds.DrawImage(bitmap, dest, source);
                }
                else
                {
                    ds.DrawImage(bitmap, dest);
                }
            }
        }

        private CanvasBitmap GetOrCreateKittyBitmap(ICanvasResourceCreator resourceCreator, KittyImageBlit blit)
        {
            var key = (blit.ImageId, blit.Generation);
            if (_kittyBitmaps.TryGetValue(key, out CanvasBitmap? cached) && cached != null)
                return cached;

            CanvasBitmap bitmap = CanvasBitmap.CreateFromBytes(
                resourceCreator,
                blit.Rgba,
                blit.ImageWidth,
                blit.ImageHeight,
                DirectXPixelFormat.R8G8B8A8UIntNormalized);
            _kittyBitmaps[key] = bitmap;
            if (_kittyBitmaps.Count > 64)
            {
                List<(uint Id, ulong Generation)> stale = new();
                foreach (var existing in _kittyBitmaps.Keys)
                {
                    if (existing != key)
                        stale.Add(existing);
                    if (stale.Count > 32)
                        break;
                }
                foreach (var item in stale)
                {
                    if (_kittyBitmaps.Remove(item, out CanvasBitmap? old))
                        old.Dispose();
                }
            }

            return bitmap;
        }

        private void DrawBlinkingCursor(CanvasDrawingSession ds, double lineHeight)
        {
            if (_scrollOffset != 0 || !_frame.CursorVisible || !_frame.CursorInViewport)
                return;
            if (_frame.CursorBlinking && !_cursorVisible)
                return;
            if (_lines <= 0 || _columns <= 0 || _measuredCharWidth <= 0)
                return;

            int col = Math.Clamp(_frame.CursorX, 0, _columns - 1);
            int row = _frame.CursorY;
            if (row < 0 || row >= _lines)
                return;

            float x = _leftTextPadding + (col * _measuredCharWidth);
            float y = _topTextPadding + (float)(row * lineHeight);
            float height = (float)lineHeight;

            GhosttyCell cell = default;
            if (row < _frame.Rows && col < _frame.Cols && _frame.Cells.Length > row * _frame.Cols + col)
                cell = _frame.Cells[row * _frame.Cols + col];

            ResolveCapturedCellColors(in cell, out Color fg, out Color bg, out bool fgDefault, out bool bgDefault);
            Color fill = fgDefault ? _outputColor : fg;
            Color glyph = bgDefault || bg.A == 0 ? InverseForegroundColor() : bg;

            if (TerminalCursorStyles.IsUnderline(_currentCursorStyle)
                || _frame.CursorStyle == GhosttyRenderStateCursorVisualStyle.Underline)
            {
                const float underlineThickness = 2f;
                ds.FillRectangle(x, y + height - underlineThickness, _measuredCharWidth, underlineThickness, fill);
                return;
            }

            if (_frame.CursorStyle == GhosttyRenderStateCursorVisualStyle.Bar)
            {
                ds.FillRectangle(x, y, 2f, height, fill);
                return;
            }

            ds.FillRectangle(x, y, _measuredCharWidth, height, fill);

            string runeString = string.IsNullOrEmpty(cell.Text) ? " " : cell.Text;
            if (string.IsNullOrWhiteSpace(runeString))
                return;

            CanvasTextFormat format = FormatFor(cell.Bold, cell.Italic);
            DrawScaledText(ds, runeString.Replace(' ', '\u00A0'), x, y, glyph, format, HorizontalScaleFor(format));
        }

        public void DrawModtermLabel(CanvasControl sender, CanvasDrawingSession cds, DisplayLabel label)
        {
            DrawModtermLabels(sender, cds, new[] { label });
        }

        public void DrawModtermLabels(CanvasControl sender, CanvasDrawingSession cds, IReadOnlyList<DisplayLabel> labels)
        {
            if (labels.Count == 0)
                return;

            using var commandList = new CanvasCommandList(sender);
            using (var clds = commandList.CreateDrawingSession())
            {
                foreach (DisplayLabel label in labels)
                {
                    clds.DrawText(
                        label.TextContent,
                        (float)label.Location.X + _controlPadding,
                        (float)label.Location.Y + _controlPadding / 4,
                        _labelColor,
                        _currentControlTextFormat);
                }
            }

            DrawGlowComposite(cds, commandList, _labelBlurColor);
        }

        private void DrawEffectSequence()
        {
            using var backgrounds = new CanvasCommandList(_sender);
            using var defaultGlyphs = new CanvasCommandList(_sender);
            using var coloredGlyphs = new CanvasCommandList(_sender);

            bool hasBackgrounds = false;
            bool hasDefaultGlyphs = false;
            bool hasColoredGlyphs = false;

            using (var backgroundDs = backgrounds.CreateDrawingSession())
            using (var defaultDs = defaultGlyphs.CreateDrawingSession())
            using (var coloredDs = coloredGlyphs.CreateDrawingSession())
            {
                foreach (DrawTextCall call in _effectSequence)
                {
                    if (!call.BackgroundIsDefault)
                    {
                        backgroundDs.FillRectangle(call.X, call.Y, call.Width, call.Height, call.BackgroundColor);
                        hasBackgrounds = true;
                    }

                    bool glowUsesThemeColor = call.ForegroundIsDefault || call.Color == _outputColor;
                    CanvasDrawingSession glyphDs;
                    if (glowUsesThemeColor)
                    {
                        glyphDs = defaultDs;
                        hasDefaultGlyphs = true;
                    }
                    else
                    {
                        glyphDs = coloredDs;
                        hasColoredGlyphs = true;
                    }

                    if (call.FitToCell)
                        DrawGlyphFitted(glyphDs, call, call.Color);
                    else
                        DrawGlyphOnGrid(glyphDs, call, call.Color, replaceSpaces: true);

                    if (call.Underline > 0)
                    {
                        float thickness = call.Underline >= 2 ? 2f : 1f;
                        glyphDs.FillRectangle(call.X, call.Y + call.Height - thickness - 1f, call.Width, thickness, call.Color);
                    }
                    if (call.Strikethrough)
                        glyphDs.FillRectangle(call.X, call.Y + (call.Height * 0.55f), call.Width, 1f, call.Color);
                }
            }

            using var effects = new EffectDisposer();
            var output = effects.Track(new CompositeEffect { Mode = CanvasComposite.SourceOver });

            ICanvasImage? glyphGlowSource = null;
            if (hasDefaultGlyphs && _outputBlurColor != _outputColor)
            {
                var tinted = effects.Track(CreateRgbReplaceEffect(defaultGlyphs, _outputBlurColor));
                if (hasColoredGlyphs)
                {
                    var merged = effects.Track(new CompositeEffect { Mode = CanvasComposite.SourceOver });
                    merged.Sources.Add(tinted);
                    merged.Sources.Add(coloredGlyphs);
                    glyphGlowSource = merged;
                }
                else
                {
                    glyphGlowSource = tinted;
                }
            }
            else if (hasDefaultGlyphs && hasColoredGlyphs)
            {
                var merged = effects.Track(new CompositeEffect { Mode = CanvasComposite.SourceOver });
                merged.Sources.Add(defaultGlyphs);
                merged.Sources.Add(coloredGlyphs);
                glyphGlowSource = merged;
            }
            else if (hasDefaultGlyphs)
            {
                glyphGlowSource = defaultGlyphs;
            }
            else if (hasColoredGlyphs)
            {
                glyphGlowSource = coloredGlyphs;
            }

            if (hasBackgrounds)
            {
                if (_blurAmount > 0f)
                    output.Sources.Add(effects.Track(CreateBlur(backgrounds)));
                output.Sources.Add(backgrounds);
            }

            if (glyphGlowSource != null && _blurAmount > 0f)
                output.Sources.Add(effects.Track(CreateBlur(glyphGlowSource)));

            if (hasDefaultGlyphs)
                output.Sources.Add(defaultGlyphs);
            if (hasColoredGlyphs)
                output.Sources.Add(coloredGlyphs);

            if (output.Sources.Count == 0)
                return;

            _drawSession.DrawImage(
                output.Sources.Count == 1 ? (ICanvasImage)output.Sources[0] : output);
        }

        private void DrawGlowComposite(CanvasDrawingSession ds, ICanvasImage source, Color glowColor)
        {
            if (_blurAmount <= 0f)
            {
                ds.DrawImage(source);
                return;
            }

            using var glow = new ShadowEffect
            {
                Source = source,
                BlurAmount = _blurAmount,
                ShadowColor = glowColor,
                Optimization = EffectOptimization.Speed
            };
            using var composite = new CompositeEffect { Mode = CanvasComposite.SourceOver };
            composite.Sources.Add(glow);
            composite.Sources.Add(source);
            ds.DrawImage(composite);
        }

        private GaussianBlurEffect CreateBlur(ICanvasImage source)
        {
            return new GaussianBlurEffect
            {
                Source = source,
                BlurAmount = _blurAmount,
                BorderMode = EffectBorderMode.Soft,
                Optimization = EffectOptimization.Speed
            };
        }

        private static ColorMatrixEffect CreateRgbReplaceEffect(ICanvasImage source, Color color)
        {
            float r = color.R / 255f;
            float g = color.G / 255f;
            float b = color.B / 255f;
            return new ColorMatrixEffect
            {
                Source = source,
                AlphaMode = CanvasAlphaMode.Premultiplied,
                ClampOutput = true,
                ColorMatrix = new Matrix5x4
                {
                    M41 = r,
                    M42 = g,
                    M43 = b,
                    M44 = 1f
                }
            };
        }

        private sealed class EffectDisposer : IDisposable
        {
            private readonly List<IDisposable> _items = new List<IDisposable>(8);

            public T Track<T>(T item) where T : IDisposable
            {
                _items.Add(item);
                return item;
            }

            public void Dispose()
            {
                for (int i = _items.Count - 1; i >= 0; i--)
                    _items[i].Dispose();
                _items.Clear();
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

        private void EnsureBoldAdvanceScale(CanvasDrawingSession ds)
        {
            if (_boldAdvanceReady)
                return;
            if (_boldTextFormat is null || _measuredCharWidth <= 0)
            {
                _boldHorizontalScale = 1f;
                return;
            }

            float boldAdvance = MeasureCellAdvance(ds, _boldTextFormat);
            if (boldAdvance <= 0.01f)
            {
                _boldHorizontalScale = 1f;
                _boldAdvanceReady = true;
                return;
            }

            float scale = _measuredCharWidth / boldAdvance;
            _boldHorizontalScale = Math.Abs(scale - 1f) < 0.005f ? 1f : scale;
            _boldAdvanceReady = true;
        }

        private float HorizontalScaleFor(CanvasTextFormat format)
            => ReferenceEquals(format, _boldTextFormat) || ReferenceEquals(format, _boldItalicTextFormat)
                ? _boldHorizontalScale
                : 1f;

        private void DrawGlyphOnGrid(CanvasDrawingSession ds, DrawTextCall call, Color color, bool replaceSpaces)
        {
            string text = replaceSpaces ? call.Text.Replace(' ', '\u00A0') : call.Text;
            DrawScaledText(ds, text, call.X, call.Y, color, call.TextFormat, call.HorizontalScale);
        }

        private static void DrawScaledText(
            CanvasDrawingSession ds,
            string text,
            float x,
            float y,
            Color color,
            CanvasTextFormat format,
            float horizontalScale)
        {
            if (horizontalScale == 1f || horizontalScale <= 0f)
            {
                ds.DrawText(text, x, y, color, format);
                return;
            }

            Matrix3x2 prior = ds.Transform;
            ds.Transform = Matrix3x2.CreateScale(horizontalScale, 1f, new Vector2(x, y)) * prior;
            ds.DrawText(text, x, y, color, format);
            ds.Transform = prior;
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
            _italicTextFormat = new CanvasTextFormat
            {
                FontFamily = resolvedFontFamily,
                FontSize = CurrentFontSize,
                FontWeight = FontWeights.Normal,
                FontStyle = Windows.UI.Text.FontStyle.Italic,
                WordWrapping = CanvasWordWrapping.NoWrap
            };
            // Bundled bold is a separate TTF; FontWeight.Bold against the regular file
            // synthesizes a wider outline that walks off the cell grid.
            _boldTextFormat = new CanvasTextFormat
            {
                FontFamily = BundledFonts.ResolveFontFamily(CurrentFont, bold: true),
                FontSize = CurrentFontSize,
                FontWeight = BundledFonts.BundledBoldUsesDedicatedFace(CurrentFont)
                    ? FontWeights.Normal
                    : FontWeights.Bold,
                WordWrapping = CanvasWordWrapping.NoWrap
            };
            _boldItalicTextFormat = new CanvasTextFormat
            {
                FontFamily = BundledFonts.ResolveFontFamily(CurrentFont, bold: true),
                FontSize = CurrentFontSize,
                FontWeight = BundledFonts.BundledBoldUsesDedicatedFace(CurrentFont)
                    ? FontWeights.Normal
                    : FontWeights.Bold,
                FontStyle = Windows.UI.Text.FontStyle.Italic,
                WordWrapping = CanvasWordWrapping.NoWrap
            };
            _cachedFontFamily = CurrentFont;
            _cachedFontSize = CurrentFontSize;
            _boldHorizontalScale = 1f;
            _boldAdvanceReady = false;
        }

        private void FlushRun(float y, int startCol, Color fg, Color bg, bool fgDefault, bool bgDefault, CanvasTextFormat format, float lineHeight, int underline = 0, bool strikethrough = false)
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
                bgDefault,
                fitToCell: false,
                cellHeight: lineHeight,
                horizontalScale: HorizontalScaleFor(format),
                underline,
                strikethrough);
            _runBuffer.Clear();
        }

        private Color InverseForegroundColor()
        {
            if (_windowColor.A != 0 && _windowColor != Colors.Transparent)
                return Opaque(_windowColor);

            return PerceivedLuminance(_outputColor) > 128
                ? Color.FromArgb(255, 16, 16, 16)
                : Color.FromArgb(255, 240, 240, 240);
        }

        private static Color Opaque(Color color)
            => Color.FromArgb(255, color.R, color.G, color.B);

        private static int PerceivedLuminance(Color color)
            => (int)((color.R * 299 + color.G * 587 + color.B * 114) / 1000);

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

