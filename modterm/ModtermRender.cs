using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using VtNetCore.VirtualTerminal;
using VtNetCore.XTermParser;
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
        public VirtualTerminalController VtController { get { return _vtController; } }
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
        private VirtualTerminalController _vtController = null!;
        private DataConsumer _vtDataConsumer = null!;
        private string _currentFont = "BlexMono Nerd Font Mono";
        private string _currentControlFont = "BlexMono Nerd Font Mono";
        private float _currentFontSize = 12f;
        private float _controlFontScale = 1f;
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

        public string CurrentFont 
        { 
            get 
            {
                return _currentFont;
            }
            set 
            {
                _currentFont = value; 
                _currentTextFormat.FontFamily = _currentFont; 
            }
        }

        public string CurrentControlFont 
        { 
            get { return _currentControlFont; }
            set { _currentControlFont = value; _currentControlTextFormat.FontFamily = _currentControlFont; }
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
            
            // Initialize VtNetCore terminal controller and data consumer
            _vtController = new VtNetCore.VirtualTerminal.VirtualTerminalController();
            _vtDataConsumer = new VtNetCore.XTermParser.DataConsumer(_vtController);

            _vtController.SetRgbForegroundColor(_outputColor.R,
                _outputColor.G, _outputColor.B);

            // set default values
            _currentTextFormat = new CanvasTextFormat { FontFamily = _currentFont,
                FontSize = _currentFontSize, WordWrapping = CanvasWordWrapping.NoWrap };
            _currentControlTextFormat = new CanvasTextFormat { FontFamily = _currentControlFont,
                FontSize = _currentControlFontSize, WordWrapping = CanvasWordWrapping.NoWrap };

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
            _shellInfoLabel.TextContent = $"MODTERM - Shell: {UserAppConfiguration.TerminalShell.Name ?? "Unknown"}";
            _appearanceInfoLabel.TextContent = $"Backdrop: {UserAppConfiguration.ThemeConfiguration.BackdropKind.ToString() ?? "Unknown"} {OpacityPct}% {GetHexStringFromColor(GetBackgroundBrush().Color)}";
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
            _vtController.SetRgbForegroundColor(_outputColor.R, _outputColor.G, _outputColor.B);
            _outputBlurColor = config.OutputBlurColor;
            _labelColor = config.LabelColor;
            _labelBlurColor = config.LabelBlurColor;
            _blurAmount = config.BlurAmount;
            OpacityPct = config.WindowOpacityPct;
            _windowColor = config.WindowColor;
            ApplySystemBackdrop(config.BackdropKind, wInstance);
            _backgroundBrush.Color = GetBackgroundArgb();
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

            _selectionRange = new VtNetCore.VirtualTerminal.TextRange
            {
                Start = GetTextPositionFromPoint(_selectionStart),
                End = GetTextPositionFromPoint(_selectionEnd)
            };

            _selectedText = _vtController.GetText(_selectionRange);
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

        public VtNetCore.VirtualTerminal.TextPosition GetTextPositionFromPoint(Point point)
        {
            double lineHeight = CurrentFontSize + _lineHeightPadding;
            int column = (int)Math.Floor((point.X - _leftTextPadding) / _measuredCharWidth);
            int visibleRow = (int)Math.Floor((point.Y - _topTextPadding) / lineHeight);
            int topRow = _isSelecting ? _selectionTopRow : _vtController.ViewPort.TopRow - _scrollOffset;

            column = Math.Clamp(column, 0, Math.Max(0, Columns - 1));
            visibleRow = Math.Clamp(visibleRow, 0, Math.Max(0, Lines - 1));

            return new VtNetCore.VirtualTerminal.TextPosition
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
                _vtDataConsumer.Push(data);
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
            int maxScrollOffset = Math.Max(0, _vtController.ViewPort.TopRow);
            _scrollOffset = Math.Clamp(_scrollOffset, 0, maxScrollOffset);
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
                _vtController.VisibleRows = _lines;
                _vtController.VisibleColumns = _columns;

                var terminal = ModtermWinInstance.EnsureTerminalInstanceForStart();
                if (terminal.Started)
                    return;

                terminal.Start(UserAppConfiguration.TerminalShell, Lines, Columns);

                _vtController.ResizeView(Columns, Lines);
                terminal.Resize((short)Columns, (short)Lines);
            }

            BeginEffectSequence(sender, args.DrawingSession);

            // Keep the VT controller's TopRow as the live screen position; scrollback only changes what we render.
            ClampScrollOffset();
            int topRow = _vtController.ViewPort.TopRow - _scrollOffset;
            var selectionRange = _isSelecting ? _selectionRange : null;
            double lineHeight = CurrentFontSize + _lineHeightPadding;

            EnsureTextFormats();

            for (int visibleRow = 0; visibleRow < _lines; visibleRow++)
            {
                int logicalRow = topRow + visibleRow;
                float y = _topTextPadding + (float)(visibleRow * lineHeight);
                var sourceLine = _vtController.ViewPort.GetLine(logicalRow);

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
                    var sourceChar = sourceLine != null && col < sourceLine.Count ? sourceLine[col] : null;
                    bool inverted = selectionRange != null && selectionRange.Contains(col, logicalRow);
                    var attr = sourceChar == null
                        ? _vtController.NullAttribute
                        : ((_vtController.CursorState.ReverseVideoMode ^ inverted ^ sourceChar.Attributes.Reverse)
                            ? sourceChar.Attributes.Inverse
                            : sourceChar.Attributes);

                    char displayChar = sourceChar == null ? ' ' : sourceChar.Char;

                    string combining = sourceChar?.CombiningCharacters ?? string.Empty;

                    Color fg = _outputColor;
                    if (!attr.DefaultForeground)
                    {
                        try { fg = string.IsNullOrEmpty(attr.WebColor) ? _outputColor : GetColorFromHexString(attr.WebColor); } catch { }
                    }

                    Color bg = Colors.Black;
                    if (!attr.DefaultBackground)
                    {
                        try { bg = string.IsNullOrEmpty(attr.BackgroundWebColor) ? Colors.Black : GetColorFromHexString(attr.BackgroundWebColor); } catch { }
                    }

                    if (attr.Hidden)
                        fg = bg;

                    CanvasTextFormat cellFormat = attr.Bright ? _boldTextFormat! : _normalTextFormat!;

                    // Only batch cells whose glyph is known to use the primary monospace
                    // font's natural advance (printable ASCII). Box-drawing, braille,
                    // resolved missing glyphs, and combining sequences are drawn per-cell
                    // at the measured grid so they stay column-aligned.
                    bool canBatch = combining.Length == 0 && IsSafeForBatch(displayChar);

                    bool matchesRun = runActive
                        && ReferenceEquals(runFormat, cellFormat)
                        && runFg == fg
                        && runBg == bg
                        && runFgDefault == attr.DefaultForeground
                        && runBgDefault == attr.DefaultBackground;

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
                            runFgDefault = attr.DefaultForeground;
                            runBgDefault = attr.DefaultBackground;
                            runFormat = cellFormat;
                        }
                        _runBuffer.Append(displayChar);
                    }
                    else
                    {
                        string cellText = displayChar.ToString() + combining;
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
                            attr.DefaultForeground,
                            attr.DefaultBackground,
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
            if (_cursorVisible && _scrollOffset == 0)
            {
                var cursor = _vtController.ViewPort.CursorPosition;
                float cursorX = _leftTextPadding + (float)(cursor.Column * _measuredCharWidth);
                float cursorY = (float)(cursor.Row * (CurrentFontSize + _lineHeightPadding)) + _topTextPadding;
                args.DrawingSession.DrawText("|", cursorX, cursorY, _outputColor, _currentTextFormat);
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

        // Cache of the rendered ink bounds of a full braille cell (U+28FF) per text format.
        // The reference is measured from the actually-drawn glyph, so it reflects the font
        // that really renders braille - whether that is the primary font or a DirectWrite
        // fallback (e.g. Consolas, which has no braille glyphs of its own).
        private readonly Dictionary<CanvasTextFormat, Rect> _brailleCellInkBounds = new Dictionary<CanvasTextFormat, Rect>();

        // The full braille cell glyph (all 8 dots set). Its ink bounds define the design
        // cell that every other braille glyph's dots are positioned within.
        private const string FullBrailleCell = "\u28FF";

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

            _normalTextFormat = new CanvasTextFormat
            {
                FontFamily = CurrentFont,
                FontSize = CurrentFontSize,
                FontWeight = FontWeights.Normal,
                WordWrapping = CanvasWordWrapping.NoWrap
            };
            _boldTextFormat = new CanvasTextFormat
            {
                FontFamily = CurrentFont,
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

