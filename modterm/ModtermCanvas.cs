using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using System;
using System.Diagnostics;
using System.Text;
using Windows.System;
using Windows.UI;
using Windows.Foundation;
using System.Runtime.CompilerServices;
using Microsoft.UI.Text;

namespace modterm
{
    public sealed partial class  ModtermWindow : Window
    {
        private int _lines = 0;
        private int _columns = 0;
        private float _measuredCharWidth;

        private int _leftTextPadding = 5;
        private int _topTextPadding = 33;
        private float _lineHeightPadding = 1.0f;

        private bool _showTitleBarControls = true;

        private CanvasTextFormat? _normalTextFormat;
        private CanvasTextFormat? _boldTextFormat;
        private string? _cachedFontFamily;
        private float _cachedFontSize;

        private readonly StringBuilder _runBuffer = new StringBuilder(256);

        private void ModtermCanvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
        {
            // Do not spawn the conhost until we can measure the canvas during drawing and determine how many rows/columns we can fit
            if (!_terminal.Started)
            {
                int measuredRows = (int)((sender.ActualHeight - _topTextPadding) / (_mtd.CurrentFontSize + _lineHeightPadding));
                float measuredCharWidth = MeasureCellAdvance(args.DrawingSession, _mtd.CurrentTextFormat);
                int measuredCols = (int)((sender.ActualWidth - _leftTextPadding) / measuredCharWidth);
                _lines = measuredRows;
                _columns = measuredCols;
                _measuredCharWidth = measuredCharWidth;
                _vtController.VisibleRows = _lines;
                _vtController.VisibleColumns = _columns;
                StartConPTY();
            }

            _mtd.BeginEffectSequence(sender, args.DrawingSession, Effects.Glow);

            // Keep the VT controller's TopRow as the live screen position; scrollback only changes what we render.
            ClampScrollOffset();
            int topRow = _vtController.ViewPort.TopRow - _scrollOffset;
            var selectionRange = _isSelecting ? _selectionRange : null;
            double lineHeight = _mtd.CurrentFontSize + _lineHeightPadding;

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

                    Color fg = _mtd.OutputColor;
                    if (!attr.DefaultForeground)
                    {
                        try { fg = _mtd.GetColorFromHexString(attr.WebColor); } catch { }
                    }

                    Color bg = Colors.Black;
                    if (!attr.DefaultBackground)
                    {
                        try { bg = _mtd.GetColorFromHexString(attr.BackgroundWebColor); } catch { }
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
                        _mtd.DrawText(
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
                float cursorY = (float)(cursor.Row * (_mtd.CurrentFontSize + _lineHeightPadding)) + _topTextPadding;
                args.DrawingSession.DrawText("|", cursorX, cursorY, _mtd.OutputColor, _mtd.CurrentTextFormat);
            }

            _mtd.EndEffectSequence();

            // draw all UI controls
            if (_showTitleBarControls)
            {
                _titleBarControls?.DrawControls(sender, args.DrawingSession, _mtd);
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
                && _cachedFontFamily == _mtd.CurrentFont
                && _cachedFontSize == _mtd.CurrentFontSize)
            {
                return;
            }

            _normalTextFormat = new CanvasTextFormat
            {
                FontFamily = _mtd.CurrentFont,
                FontSize = _mtd.CurrentFontSize,
                FontWeight = FontWeights.Normal,
                WordWrapping = CanvasWordWrapping.NoWrap
            };
            _boldTextFormat = new CanvasTextFormat
            {
                FontFamily = _mtd.CurrentFont,
                FontSize = _mtd.CurrentFontSize,
                FontWeight = FontWeights.Bold,
                WordWrapping = CanvasWordWrapping.NoWrap
            };
            _cachedFontFamily = _mtd.CurrentFont;
            _cachedFontSize = _mtd.CurrentFontSize;
        }

        private void FlushRun(float y, int startCol, Color fg, Color bg, bool fgDefault, bool bgDefault, CanvasTextFormat format)
        {
            float x = _leftTextPadding + (startCol * _measuredCharWidth);
            float width = _runBuffer.Length * _measuredCharWidth;
            _mtd.DrawText(
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

        private void ModtermCanvas_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            // Forward key events as VT sequences to the shell process
            var ctrlState = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control);
            bool isCtrlPressed = ctrlState.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

            // Handle Ctrl+C (send interrupt)
            if (isCtrlPressed && e.Key == Windows.System.VirtualKey.C)
            {
                _scrollOffset = 0;
                _terminal.WriteInput("\x03");
                e.Handled = true;
                ModtermCanvas.Invalidate();
                return;
            }

            // Map special keys to VT sequences
            string vtSeq = string.Empty;
            switch (e.Key)
            {
                case Windows.System.VirtualKey.PageUp:
                    ScrollBackBy(Math.Max(1, _lines - 1));
                    e.Handled = true;
                    return;
                case Windows.System.VirtualKey.PageDown:
                    ScrollBackBy(-Math.Max(1, _lines - 1));
                    e.Handled = true;
                    return;
                case Windows.System.VirtualKey.Enter:
                    vtSeq = "\r";
                    break;
                case Windows.System.VirtualKey.Tab:
                    vtSeq = "\t";
                    break;
                case Windows.System.VirtualKey.Back:
                    vtSeq = "\x7F";
                    break;
                case Windows.System.VirtualKey.Left:
                    vtSeq = "\x1B[D";
                    break;
                case Windows.System.VirtualKey.Right:
                    vtSeq = "\x1B[C";
                    break;
                case Windows.System.VirtualKey.Up:
                    vtSeq = "\x1B[A";
                    break;
                case Windows.System.VirtualKey.Down:
                    vtSeq = "\x1B[B";
                    break;
                case Windows.System.VirtualKey.Home:
                    vtSeq = "\x1B[H";
                    break;
                case Windows.System.VirtualKey.End:
                    vtSeq = "\x1B[F";
                    break;
                case Windows.System.VirtualKey.Delete:
                    vtSeq = "\x1B[3~";
                    break;
                case Windows.System.VirtualKey.Escape:
                    vtSeq = "\x1B";
                    break;
                default:
                    var keyChar = GetCharFromVirtualKey(e.Key, e);
                    if (keyChar != null)
                    {
                        vtSeq = keyChar.ToString() ?? "";
                    }
                    //Debug.WriteLine($"Key: {e.Key}, Char: {keyChar}, Ctrl: {isCtrlPressed}");
                    break;
            }
            if (!string.IsNullOrEmpty(vtSeq))
            {
                _scrollOffset = 0;
                _terminal.WriteInput(vtSeq);
                e.Handled = true;
            }
            ModtermCanvas.Invalidate();
        }

        private void ModtermCanvas_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            Point currentPoint = e.GetCurrentPoint(ModtermCanvas).Position;
            if (!e.GetCurrentPoint(ModtermCanvas).Properties.IsLeftButtonPressed)
                return;

            _isSelecting = false;
            _selectionRange = null;
            _selectedText = "";

            if (!IsInTextArea(currentPoint))
                return;

            _isSelecting = true;
            _selectionStart = currentPoint;
            _selectionEnd = _selectionStart;
            _selectionTopRow = _vtController.ViewPort.TopRow - _scrollOffset;
            UpdateSelectedText();
            ModtermCanvas.Invalidate();
        }

        private void ModtermCanvas_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (!_isSelecting)
                return;

            _selectionEnd = e.GetCurrentPoint(ModtermCanvas).Position;
            UpdateSelectedText();
            ModtermCanvas.Invalidate();
        }

        private void ModtermCanvas_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (!_isSelecting)
                return;

            _selectionEnd = e.GetCurrentPoint(ModtermCanvas).Position;
            UpdateSelectedText();
            _isSelecting = false;
            CopySelectedTextToClipboard();
            ModtermCanvas.Invalidate();
        }

        private void ModtermCanvas_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            _flyout.ShowAt(ModtermCanvas, e.GetPosition(ModtermCanvas));
        }

        private void ModtermCanvas_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            int delta = e.GetCurrentPoint(ModtermCanvas).Properties.MouseWheelDelta;
            int notches = Math.Max(1, Math.Abs(delta) / 120);
            int rowsPerNotch = Math.Max(1, _lines / 10);
            int rows = notches * rowsPerNotch * (delta > 0 ? 1 : -1);

            ScrollBackBy(rows);
            e.Handled = true;
        }

        private void ScrollBackBy(int rows)
        {
            if (rows == 0)
                return;

            int previousOffset = _scrollOffset;
            _scrollOffset += rows;
            ClampScrollOffset();

            if (_scrollOffset != previousOffset)
                ModtermCanvas.Invalidate();
        }

        private void ClampScrollOffset()
        {
            int maxScrollOffset = Math.Max(0, _vtController.ViewPort.TopRow);
            _scrollOffset = Math.Clamp(_scrollOffset, 0, maxScrollOffset);
        }


        private char? GetCharFromVirtualKey(Windows.System.VirtualKey key, KeyRoutedEventArgs e)
        {
            Windows.UI.Core.CoreVirtualKeyStates shiftState =
                    Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(
                        Windows.System.VirtualKey.Shift);

            bool isShiftPressed = shiftState.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

            // Handle SHIFT + INSERT for paste
            if (e.Key == VirtualKey.Insert && isShiftPressed)
            {
                PasteFromClipboard();
                e.Handled = true;
                return null;
            }

            // handle a-z
            if (key >= Windows.System.VirtualKey.A && key <= Windows.System.VirtualKey.Z)
            {
                char baseChar = (char)('a' + (key - Windows.System.VirtualKey.A));
                if (isShiftPressed)
                {
                    baseChar = char.ToUpper(baseChar);
                }
                return baseChar;
            }
            // handle 0-9
            if (key >= Windows.System.VirtualKey.Number0 && key <= Windows.System.VirtualKey.Number9)
            {
                char baseChar = (char)('0' + (key - Windows.System.VirtualKey.Number0));
                if (isShiftPressed)
                {
                    // Handle shifted number keys for common symbols
                    switch (baseChar)
                    {
                        case '1': baseChar = '!'; break;
                        case '2': baseChar = '@'; break;
                        case '3': baseChar = '#'; break;
                        case '4': baseChar = '$'; break;
                        case '5': baseChar = '%'; break;
                        case '6': baseChar = '^'; break;
                        case '7': baseChar = '&'; break;
                        case '8': baseChar = '*'; break;
                        case '9': baseChar = '('; break;
                        case '0': baseChar = ')'; break;
                    }
                }
                return baseChar;
            }
            else
            {
                // Handle some common punctuation keys
                switch (key)
                {
                    case Windows.System.VirtualKey.Space: return ' ';
                    case (VirtualKey)188: return isShiftPressed ? '<' : ',';
                    case (VirtualKey)190: return isShiftPressed ? '>' : '.';
                    case (VirtualKey)189: return isShiftPressed ? '_' : '-';
                    case (VirtualKey)187: return isShiftPressed ? '+' : '=';
                    case (VirtualKey)191: return isShiftPressed ? '?' : '/';
                    case (VirtualKey)186: return isShiftPressed ? ':' : ';';
                    case (VirtualKey)222: return isShiftPressed ? '"' : '\'';
                    case (VirtualKey)219: return isShiftPressed ? '{' : '[';
                    case (VirtualKey)221: return isShiftPressed ? '}' : ']';
                    case (VirtualKey)220: return isShiftPressed ? '|' : '\\';
                    case (VirtualKey)192: return isShiftPressed ? '~' : '`';
                }
            }
            return null;
        }

    }
}
