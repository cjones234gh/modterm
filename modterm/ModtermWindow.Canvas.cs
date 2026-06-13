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


        
        
       

        private void ModtermCanvas_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            // Forward key events as VT sequences to the shell process
            var ctrlState = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control);
            bool isCtrlPressed = ctrlState.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

            // Handle Ctrl+C (send interrupt)
            if (isCtrlPressed && e.Key == Windows.System.VirtualKey.C)
            {
                _mtr.ScrollOffset = 0;
                ConPtyTerminal.WriteInput("\x03");
                e.Handled = true;
                ModtermCanvas.Invalidate();
                return;
            }

            // Map special keys to VT sequences
            string vtSeq = string.Empty;
            switch (e.Key)
            {
                case Windows.System.VirtualKey.PageUp:
                    _mtr.ScrollBackBy(Math.Max(1, _mtr.Lines - 1));
                    e.Handled = true;
                    return;
                case Windows.System.VirtualKey.PageDown:
                    _mtr.ScrollBackBy(-Math.Max(1, _mtr.Lines - 1));
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
                    vtSeq = GetFunctionKeySequence(e.Key) ?? string.Empty;
                    if (string.IsNullOrEmpty(vtSeq))
                    {
                        var keyChar = GetCharFromVirtualKey(e.Key, e);
                        if (keyChar != null)
                        {
                            vtSeq = keyChar.ToString() ?? "";
                        }
                    }
                    //Debug.WriteLine($"Key: {e.Key}, Char: {keyChar}, Ctrl: {isCtrlPressed}");
                    break;
            }
            if (!string.IsNullOrEmpty(vtSeq))
            {
                _mtr.ScrollOffset = 0;
                ConPtyTerminal.WriteInput(vtSeq);
                e.Handled = true;
            }
            ModtermCanvas.Invalidate();
        }

        private void ModtermCanvas_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            Point currentPoint = e.GetCurrentPoint(ModtermCanvas).Position;
            if (!e.GetCurrentPoint(ModtermCanvas).Properties.IsLeftButtonPressed)
                return;

            _mtr.IsSelecting = false;
            _mtr.SelectionRange = null;
            _mtr.SelectedText = "";

            if (!_mtr.IsInTextArea(currentPoint))
                return;

            _mtr.IsSelecting = true;
            _mtr.SelectionStart = currentPoint;
            _mtr.SelectionEnd = _mtr.SelectionStart;
            _mtr.SelectionTopRow = _mtr.VtController.ViewPort.TopRow - _mtr.ScrollOffset;
            _mtr.UpdateSelectedText();
            ModtermCanvas.Invalidate();
        }

        private void ModtermCanvas_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (!_mtr.IsSelecting)
                return;

            _mtr.SelectionEnd = e.GetCurrentPoint(ModtermCanvas).Position;
            _mtr.UpdateSelectedText();
            ModtermCanvas.Invalidate();
        }

        private void ModtermCanvas_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (!_mtr.IsSelecting)
                return;

            _mtr.SelectionEnd = e.GetCurrentPoint(ModtermCanvas).Position;
            _mtr.UpdateSelectedText();
            _mtr.IsSelecting = false;
            _mtr.CopySelectedTextToClipboard();
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
            int rowsPerNotch = Math.Max(1, _mtr.Lines / 10);
            int rows = notches * rowsPerNotch * (delta > 0 ? 1 : -1);

            _mtr.ScrollBackBy(rows);
            e.Handled = true;
        }




        // xterm / Windows Console VT sequences for function keys.
        private static string? GetFunctionKeySequence(VirtualKey key)
        {
            return key switch
            {
                VirtualKey.F1 => "\x1BOP",
                VirtualKey.F2 => "\x1BOQ",
                VirtualKey.F3 => "\x1BOR",
                VirtualKey.F4 => "\x1BOS",
                VirtualKey.F5 => "\x1B[15~",
                VirtualKey.F6 => "\x1B[17~",
                VirtualKey.F7 => "\x1B[18~",
                VirtualKey.F8 => "\x1B[19~",
                VirtualKey.F9 => "\x1B[20~",
                VirtualKey.F10 => "\x1B[21~",
                VirtualKey.F11 => "\x1B[23~",
                VirtualKey.F12 => "\x1B[24~",
                _ => null
            };
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
                _mtr.PasteFromClipboard();
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
