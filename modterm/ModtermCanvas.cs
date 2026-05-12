using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using System;
using System.Diagnostics;
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

        private bool _showRightButtonControls = true;
        private bool _showTitleBarControls = true;

        private void ModtermCanvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
        {
            // Do not spawn the conhost until we can measure the canvas during drawing and determine how many rows/columns we can fit
            if (!_terminal.Started)
            {
                int measuredRows = (int)((sender.ActualHeight - _topTextPadding) / (_mtd.CurrentFontSize + 2));
                float measuredCharWidth = MeasureCharWidth(sender, args);
                int measuredCols = (int)(sender.ActualWidth / measuredCharWidth);
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
            var pageSpans = _vtController.ViewPort.GetPageSpans(topRow, _lines, _columns);
            double y = _topTextPadding;
            foreach (var row in pageSpans)
            {
                float x = _leftTextPadding;
                int col = 0; // Reset column at the start of each row
                foreach (var span in row.Spans)
                {
                    //Debug.WriteLine($"span.BackgroundColor: {span.BackgroundColor}, span.ForgroundColor: {span.ForgroundColor}");
                    Color fg = _mtd.OutputColor;
                    if (!span.ForegroundIsDefault)
                    {
                        try { fg = _mtd.GetColorFromHexString(span.ForgroundColor); } catch { }
                    }

                    Color bg = Colors.Black;
                    if (!span.BackgroundIsDefault)
                    {
                        try { bg = _mtd.GetColorFromHexString(span.BackgroundColor); } catch { }
                    }

                    if (span.Hidden)
                    {
                        fg = bg; // Set foreground to background color to hide text
                    }

                    // Draw the text span at the correct column position
                    x = _leftTextPadding + (col * _measuredCharWidth);
                    CanvasTextFormat textFormat = _mtd.GetTextFormat();
                    textFormat.FontWeight = span.Bold ? FontWeights.Bold : FontWeights.Normal;
                    
                    _mtd.DrawText(span.Text, x, (float)y, (float)(span.Text.Length * _measuredCharWidth), fg, bg, 
                        textFormat, span.ForegroundIsDefault, span.BackgroundIsDefault);
                    
                    // Advance col by the number of characters in the span
                    col += span.Text.Length;
                }
                y += _mtd.CurrentFontSize + 2;
            }

            // Debug: Draw grid lines for columns and rows
            //DrawDebugGrid(sender, args);

            // Draw blinking cursor only on the live viewport.
            if (_cursorVisible && _scrollOffset == 0)
            {
                var cursor = _vtController.ViewPort.CursorPosition;
                float cursorX = _leftTextPadding + (float)(cursor.Column * _measuredCharWidth);
                float cursorY = (float)(cursor.Row * (_mtd.CurrentFontSize + 2)) + _topTextPadding;
                args.DrawingSession.DrawText("|", cursorX, cursorY, _mtd.OutputColor, _mtd.GetTextFormat());
            }

            _mtd.EndEffectSequence();

            // Visual selection highlight (simple semi-transparent overlay)
            if (!string.IsNullOrEmpty(_selectedText) && _isSelecting)
            {
                double x = Math.Min(_selectionStart.X, _selectionEnd.X);
                double yy = Math.Min(_selectionStart.Y, _selectionEnd.Y);
                double width = Math.Abs(_selectionEnd.X - _selectionStart.X);
                double height = Math.Abs(_selectionEnd.Y - _selectionStart.Y);
                if (width > 0 && height > 0)
                {
                    args.DrawingSession.FillRectangle(
                        new Windows.Foundation.Rect(x, yy, width, height),
                        Color.FromArgb(80, 0, 120, 255));
                }
            }

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

        // Measure the width of a typical monospace character for accurate column calculation
        private float MeasureCharWidth(CanvasControl sender, CanvasDrawEventArgs args)
        {
            // Use 'W' as a typical wide character for monospace fonts
            using (var layout = new CanvasTextLayout(args.DrawingSession, "W", _mtd.GetTextFormat(), 9999, 9999))
            {
                return (float)layout.DrawBounds.Width * 1.1f; // add a small buffer to prevent clipping
            }
        }

        // Debug method to draw grid lines for columns and rows
        private void DrawDebugGrid(CanvasControl sender, CanvasDrawEventArgs args)
        {
            // Draw debug grid lines for columns
            for (int c = 0; c < _columns; c++)
            {
                float x = _leftTextPadding + (c * _measuredCharWidth);
                args.DrawingSession.DrawLine(x, 0, x, (float)sender.ActualHeight, Colors.Red, 0.5f);
                if (x > sender.ActualWidth)
                { Debug.WriteLine("Column " + c + " x=" + x + " exceeds canvas width " + sender.ActualWidth); break; }
            }
            // Draw debug grid lines for rows
            for (int r = 0; r < _lines; r++)
            {
                float yLine = r * (_mtd.CurrentFontSize + 2);
                args.DrawingSession.DrawLine(0, yLine, (float)sender.ActualWidth, yLine, Colors.Blue, 0.5f);
                if (yLine > sender.ActualHeight)
                { Debug.WriteLine("Row " + r + " y=" + yLine + " exceeds canvas height " + sender.ActualHeight); break; }
            }
        }

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

            _isSelecting = true;
            _selectionStart = currentPoint;
            _selectionEnd = _selectionStart;
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
            _isSelecting = false;
            UpdateSelectedText();

            Point currentPoint = e.GetCurrentPoint(ModtermCanvas).Position;

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
