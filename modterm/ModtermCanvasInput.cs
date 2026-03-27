using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Modglass;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.System;

namespace modterm
{
    public sealed partial class MainWindow : Window
    {
        private void ModtermCanvas_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            // Forward key events as VT sequences to the shell process
            var ctrlState = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control);
            bool isCtrlPressed = ctrlState.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

            // Handle Ctrl+C (send interrupt)
            if (isCtrlPressed && e.Key == Windows.System.VirtualKey.C)
            {
                _terminal.WriteInput("\x03");
                return;
            }

            // Map special keys to VT sequences
            string vtSeq = null;
            switch (e.Key)
            {
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
                        vtSeq = keyChar.ToString();
                    break;
            }
            if (!string.IsNullOrEmpty(vtSeq))
            {
                _terminal.WriteInput(vtSeq);
            }
            ModtermCanvas.Invalidate();
        }

        private void ModtermCanvas_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (_scrollLockControl.Location.Contains(e.GetCurrentPoint(ModtermCanvas).Position))
            {
                _scrollLockControl.IsPressed = true;
                ModtermCanvas.Invalidate();
            }
            if (e.GetCurrentPoint(ModtermCanvas).Properties.IsLeftButtonPressed)
            {
                _isSelecting = true;
                _selectionStart = e.GetCurrentPoint(ModtermCanvas).Position;
                _selectionEnd = _selectionStart;
                _selectedText = "";
                ModtermCanvas.Invalidate();
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

            foreach (var control in _upperRightControls.Controls)
            {
                control.IsHovered = control.Location.Contains(currentPoint);
            }
            foreach (var control in _lowerRightControls.Controls)
            {
                control.IsHovered = control.Location.Contains(currentPoint);
            }

            ModtermCanvas.Invalidate();

        }

        private void ModtermCanvas_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (_scrollLockControl.Location.Contains(e.GetCurrentPoint(ModtermCanvas).Position))
            {
                _scrollLockControl.IsPressed = false;
                _scrollLockControl.IsEngaged = !_scrollLockControl.IsEngaged;
                Debug.WriteLine("Scroll Lock toggled: " + _scrollLockControl.IsEngaged);
                ModtermCanvas.Invalidate();
            }
            _isSelecting = false;
            UpdateSelectedText();
            ModtermCanvas.Invalidate();
        }

        private void ModtermCanvas_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            _flyout.ShowAt(ModtermCanvas, e.GetPosition(ModtermCanvas));
        }

        private void ModtermCanvas_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            int delta = e.GetCurrentPoint(ModtermCanvas).Properties.MouseWheelDelta;
            int step = delta > 0 ? 1 : -1;                 // scroll up = positive delta
            //_scrollOffset = Math.Clamp(_scrollOffset + step, 0, Math.Max(0, _bufferLines.Count - 1));
            ModtermCanvas.Invalidate();
        }

        private void MainWindow_SizeChanged(object sender, Microsoft.UI.Xaml.WindowSizeChangedEventArgs args)
        {
            // Calculate new rows/columns based on font size and canvas size
            DetermineRowsAndColumns();
            if (_columns < 1) _columns = 1;
            if (_lines < 1) _lines = 1;
            _vtController.ResizeView(_columns, _lines);
            _terminal?.Resize((short)_columns, (short)_lines);
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
