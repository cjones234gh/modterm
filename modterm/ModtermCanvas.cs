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

        private bool _showRightButtonControls = true;
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
                    bool wasResolved = TryResolveMissingGlyph(logicalRow, col, displayChar, out char resolvedDisplay);
                    if (wasResolved)
                        displayChar = resolvedDisplay;

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
                    bool canBatch = !wasResolved
                        && combining.Length == 0
                        && IsSafeForBatch(displayChar);

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
            if (_showRightButtonControls)
            {
                _rightButtonControls?.DrawControls(sender, args.DrawingSession, _mtd);
            }
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

        private char GetCellCharAt(int logicalRow, int col)
        {
            if (col < 0 || col >= _columns)
                return ' ';
            var line = _vtController.ViewPort.GetLine(logicalRow);
            if (line == null || col >= line.Count)
                return ' ';
            return line[col].Char;
        }

        private bool RowLooksLikeBorder(int logicalRow)
        {
            var line = _vtController.ViewPort.GetLine(logicalRow);
            if (line == null)
                return false;

            int boxCount = 0;
            int limit = Math.Min(line.Count, _columns);
            for (int c = 0; c < limit; c++)
            {
                if (IsBoxDrawingChar(line[c].Char))
                    boxCount++;
            }
            return boxCount >= 2;
        }

        private bool TryResolveMissingGlyph(int logicalRow, int col, char rawChar, out char displayChar)
        {
            displayChar = rawChar;
            if (!IsMissingGlyph(rawChar) || !RowLooksLikeBorder(logicalRow))
                return false;

            GetMissingGlyphRunBounds(logicalRow, col, out int runStart, out _);

            // A run of ?? (or more) in the PTY buffer is one missing glyph — render only the first cell.
            if (col != runStart)
            {
                displayChar = ' ';
                return true;
            }

            displayChar = ResolveMissingBoxChar(logicalRow, col);
            return true;
        }

        private void GetMissingGlyphRunBounds(int logicalRow, int col, out int runStart, out int runEnd)
        {
            runStart = col;
            while (runStart > 0 && IsMissingGlyph(GetCellCharAt(logicalRow, runStart - 1)))
                runStart--;

            runEnd = col;
            while (runEnd + 1 < _columns && IsMissingGlyph(GetCellCharAt(logicalRow, runEnd + 1)))
                runEnd++;
        }

        private bool RowHasHorizontalBoxChar(int logicalRow)
        {
            var line = _vtController.ViewPort.GetLine(logicalRow);
            if (line == null)
                return false;

            int limit = Math.Min(line.Count, _columns);
            for (int c = 0; c < limit; c++)
            {
                if (IsHorizontalBox(line[c].Char))
                    return true;
            }
            return false;
        }

        private bool ColumnHasVerticalStreakAt(int col, int nearLogicalRow)
        {
            int count = 0;
            int start = Math.Max(0, nearLogicalRow - 20);
            int end = nearLogicalRow + 20;
            for (int r = start; r <= end; r++)
            {
                char c = GetCellCharAt(r, col);
                if (IsVerticalBox(c) || IsMissingGlyph(c))
                    count++;
            }
            return count >= 3;
        }

        private char ResolveMissingBoxChar(int logicalRow, int col)
        {
            char up = GetCellCharAt(logicalRow - 1, col);
            char down = GetCellCharAt(logicalRow + 1, col);
            char left = GetCellCharAt(logicalRow, col - 1);
            char right = GetCellCharAt(logicalRow, col + 1);

            bool rowHasHorizontal = RowHasHorizontalBoxChar(logicalRow);
            bool columnHasVertical = ColumnHasVerticalStreakAt(col, logicalRow);

            if (rowHasHorizontal && columnHasVertical)
                return '\u253C';

            bool verticalContext =
                IsVerticalBox(up) || IsVerticalBox(down) ||
                IsMissingGlyph(up) || IsMissingGlyph(down);
            bool horizontalContext =
                IsHorizontalBox(left) || IsHorizontalBox(right) ||
                IsMissingGlyph(left) || IsMissingGlyph(right);

            if (verticalContext && !horizontalContext)
                return '\u2502';

            if (horizontalContext && !verticalContext)
                return '\u2500';

            if (columnHasVertical)
                return '\u2502';

            return rowHasHorizontal ? '\u2500' : '\u2502';
        }

        private static bool IsMissingGlyph(char c) => c == '?' || c == '\uFFFD';

        private static bool IsBoxDrawingChar(char c) =>
            (c >= '\u2500' && c <= '\u257F') || (c >= '\u2550' && c <= '\u256F');

        private static bool IsHorizontalBox(char c) =>
            c == '\u2500' || c == '\u2501' || c == '\u2550' || c == '\u2551' || c == '-';

        private static bool IsVerticalBox(char c) =>
            c == '\u2502' || c == '\u2503' || c == '\u2551' || c == '|';

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

        private static bool HasExpandableFlyout(ModtermControl control)
            => control.Children is { Count: > 0 };

        private void ModtermCanvas_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            Point currentPoint = e.GetCurrentPoint(ModtermCanvas).Position;
            if (!e.GetCurrentPoint(ModtermCanvas).Properties.IsLeftButtonPressed)
                return;

            _isSelecting = false;
            _selectionRange = null;
            _selectedText = "";

            // Flyout child (expanded parent)
            foreach (var control in _rightButtonControls.Controls)
            {
                if (!HasExpandableFlyout(control) || !control.IsEngaged)
                    continue;
                foreach (var child in control.Children)
                {
                    if (!child.Location.Contains(currentPoint) || !child.Interactive)
                        continue;

                    ClearRightDockExceptFlyoutChild(control, child);
                    child.IsPressed = true;
                    ModtermCanvas.Invalidate();
                    return;
                }
            }

            // Top-level right-dock controls
            foreach (var control in _rightButtonControls.Controls)
            {
                if (!control.Location.Contains(currentPoint) || !control.Interactive)
                    continue;

                foreach (var other in _rightButtonControls.Controls)
                {
                    if (other == control)
                        continue;
                    other.IsPressed = false;
                    if (HasExpandableFlyout(other))
                    {
                        other.IsEngaged = false;
                        foreach (var ch in other.Children)
                        {
                            ch.IsPressed = false;
                            ch.IsEngaged = false;
                        }
                    }
                    else
                        other.IsEngaged = false;
                }

                control.IsPressed = true;
                if (!HasExpandableFlyout(control))
                    control.IsEngaged = true;

                ModtermCanvas.Invalidate();
                return;
            }

            // Click outside controls: dismiss flyouts
            foreach (var c in _rightButtonControls.Controls)
            {
                c.IsPressed = false;
                if (HasExpandableFlyout(c))
                {
                    c.IsEngaged = false;
                    foreach (var ch in c.Children)
                    {
                        ch.IsPressed = false;
                        ch.IsEngaged = false;
                    }
                }
                else
                    c.IsEngaged = false;
            }

            if (!IsInTextArea(currentPoint))
            {
                ModtermCanvas.Invalidate();
                return;
            }

            _isSelecting = true;
            _selectionStart = currentPoint;
            _selectionEnd = _selectionStart;
            _selectionTopRow = _vtController.ViewPort.TopRow - _scrollOffset;
            UpdateSelectedText();
            ModtermCanvas.Invalidate();
        }

        private void ClearRightDockExceptFlyoutChild(ModtermControl flyoutParent, ModtermControl activeChild)
        {
            foreach (var c in _rightButtonControls.Controls)
            {
                if (c == flyoutParent)
                {
                    foreach (var ch in c.Children)
                    {
                        if (ch != activeChild)
                        {
                            ch.IsPressed = false;
                            ch.IsEngaged = false;
                        }
                    }
                    continue;
                }

                c.IsPressed = false;
                if (HasExpandableFlyout(c))
                {
                    c.IsEngaged = false;
                    foreach (var ch in c.Children)
                    {
                        ch.IsPressed = false;
                        ch.IsEngaged = false;
                    }
                }
                else
                    c.IsEngaged = false;
            }
        }

        private void ModtermCanvas_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            Point currentPoint = e.GetCurrentPoint(ModtermCanvas).Position;

            if (_isSelecting)
            {
                _selectionEnd = currentPoint;
                UpdateSelectedText();
                ModtermCanvas.Invalidate();
            }

            foreach (var control in _rightButtonControls.Controls)
            {
                control.IsHovered = control.Location.Contains(currentPoint);
                if (HasExpandableFlyout(control) && control.IsEngaged)
                {
                    foreach (var child in control.Children)
                        child.IsHovered = child.Location.Contains(currentPoint);
                }
                else if (HasExpandableFlyout(control))
                {
                    foreach (var child in control.Children)
                        child.IsHovered = false;
                }
            }

            ModtermCanvas.Invalidate();
        }

        private void ModtermCanvas_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            Point currentPoint = e.GetCurrentPoint(ModtermCanvas).Position;

            bool wasSelecting = _isSelecting;
            if (wasSelecting)
            {
                _selectionEnd = currentPoint;
                UpdateSelectedText();
                _isSelecting = false;
                CopySelectedTextToClipboard();
                ModtermCanvas.Invalidate();
                return;
            }

            // Finish flyout child click
            foreach (var control in _rightButtonControls.Controls)
            {
                if (!HasExpandableFlyout(control) || !control.IsEngaged)
                    continue;
                foreach (var child in control.Children)
                {
                    if (!child.IsPressed)
                        continue;
                    bool over = child.Location.Contains(currentPoint);
                    child.IsPressed = false;
                    child.IsEngaged = false;
                    if (over)
                        child.HandleClick();
                    control.IsEngaged = false;
                    ModtermCanvas.Invalidate();
                    return;
                }
            }

            // Expandable anchor: toggle flyout or cancel press outside own bounds
            foreach (var control in _rightButtonControls.Controls)
            {
                if (!HasExpandableFlyout(control) || !control.IsPressed)
                    continue;
                control.IsPressed = false;
                if (control.Location.Contains(currentPoint))
                    control.IsEngaged = !control.IsEngaged;
                else
                    control.IsEngaged = false;
                ModtermCanvas.Invalidate();
                return;
            }

            foreach (var control in _rightButtonControls.Controls)
            {
                if (HasExpandableFlyout(control))
                    continue;
                if (control.IsEngaged)
                {
                    control.IsPressed = false;
                    control.IsEngaged = false;
                    if (control.Location.Contains(currentPoint))
                        control.HandleClick();
                }
            }
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
