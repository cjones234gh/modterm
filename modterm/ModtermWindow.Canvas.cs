using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using System;
using System.Text;
using Windows.Foundation;
using Windows.System;
using Windows.UI.Core;
using XtermSharp;

namespace modterm
{
    public sealed partial class  ModtermWindow : Window
    {
        private void ModtermCanvas_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            _keyDownSentToPty = false;

            bool ctrl = IsKeyDown(VirtualKey.Control);
            bool alt = IsKeyDown(VirtualKey.Menu);
            bool shift = IsKeyDown(VirtualKey.Shift);
            bool capsLock = IsCapsLockOn();

            if (VtUserInput.IsAltKey(e.Key))
            {
                // Swallow bare Alt so Windows does not enter menu-accelerator mode
                // and eat the following Alt+letter (Fresh and similar TUIs).
                e.Handled = true;
                return;
            }

            if (VtUserInput.IsModifierOnly(e.Key))
                return;

            // Let the system keep Alt+F4 (close) and Alt+Space (window menu).
            if (alt && !ctrl && e.Key is VirtualKey.F4 or VirtualKey.Space)
                return;

            if (e.Key == VirtualKey.Insert && shift && !ctrl && !alt)
            {
                _mtr.PasteFromClipboard();
                e.Handled = true;
                _keyDownSentToPty = true;
                return;
            }

            if (e.Key is VirtualKey.PageUp or VirtualKey.PageDown)
            {
                if (shift || ShouldScrollWithPagingKeys())
                {
                    int direction = e.Key == VirtualKey.PageUp ? 1 : -1;
                    _mtr.ScrollBackBy(direction * Math.Max(1, _mtr.Lines - 1));
                    e.Handled = true;
                    ModtermCanvas.Invalidate();
                    return;
                }
            }

            // Ctrl+Alt is typically AltGr; let CharacterReceived emit the composed glyph.
            if (ctrl && alt)
                return;

            var terminal = _mtr.Terminal;
            string? vtSeq = VtUserInput.EncodeKey(
                e.Key,
                ctrl,
                alt,
                shift,
                capsLock,
                terminal.ApplicationCursor);

            if (!string.IsNullOrEmpty(vtSeq))
            {
                SendPtyInput(vtSeq);
                e.Handled = true;
                _keyDownSentToPty = true;
            }
        }

        private void RootGrid_CharacterReceived(UIElement sender, CharacterReceivedRoutedEventArgs e)
        {
            if (_keyDownSentToPty)
            {
                e.Handled = true;
                return;
            }

            char ch = e.Character;
            if (char.IsControl(ch) && ch != '\r' && ch != '\n' && ch != '\t')
                return;

            SendPtyInput(ch.ToString());
            e.Handled = true;
        }

        private void ModtermWindow_Activated(object sender, Microsoft.UI.Xaml.WindowActivatedEventArgs e)
        {
            if (_mtr.Terminal is null || !_mtr.Terminal.SendFocus)
                return;

            SendPtyInput(
                e.WindowActivationState == WindowActivationState.Deactivated
                    ? VtUserInput.FocusOut
                    : VtUserInput.FocusIn);
        }

        private void ModtermCanvas_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            _ptyConsumedRightClick = false;
            Point currentPoint = e.GetCurrentPoint(ModtermCanvas).Position;
            var props = e.GetCurrentPoint(ModtermCanvas).Properties;
            int button = ButtonFromUpdateKind(props.PointerUpdateKind, pressed: true);
            bool shift = IsKeyDown(VirtualKey.Shift);

            if (ShouldReportMouseToPty(shift) && button >= 0)
            {
                if (TryReportMouse(button, release: false, motion: false, currentPoint, clamp: false))
                {
                    _mouseReportButton = button;
                    _ptyConsumedRightClick = button == 2;
                    ModtermCanvas.CapturePointer(e.Pointer);
                    ClearSelectionVisual();
                    e.Handled = true;
                    return;
                }
            }

            if (button != 0)
                return;

            _mtr.IsSelecting = false;
            _mtr.SelectionRange = null;
            _mtr.SelectedText = "";

            if (!_mtr.IsInTextArea(currentPoint))
                return;

            _mtr.IsSelecting = true;
            _mtr.SelectionStart = currentPoint;
            _mtr.SelectionEnd = _mtr.SelectionStart;
            _mtr.SelectionTopRow = _mtr.TopRow - _mtr.ScrollOffset;
            _mtr.UpdateSelectedText();
            ModtermCanvas.Invalidate();
        }

        private void ModtermCanvas_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            Point currentPoint = e.GetCurrentPoint(ModtermCanvas).Position;
            var props = e.GetCurrentPoint(ModtermCanvas).Properties;
            bool shift = IsKeyDown(VirtualKey.Shift);
            var mode = _mtr.Terminal.MouseMode;

            if (ShouldReportMouseToPty(shift))
            {
                bool buttonDown = props.IsLeftButtonPressed || props.IsMiddleButtonPressed || props.IsRightButtonPressed;
                int button = _mouseReportButton >= 0
                    ? _mouseReportButton
                    : ButtonFromPressedState(props);

                if (buttonDown && mode.SendButtonTracking())
                {
                    TryReportMouse(button, release: false, motion: true, currentPoint, clamp: true);
                    e.Handled = true;
                    return;
                }

                if (!buttonDown && mode.SendMotionEvent())
                {
                    TryReportMouse(3, release: false, motion: true, currentPoint, clamp: false);
                    e.Handled = true;
                    return;
                }

                if (mode != MouseMode.Off && !shift)
                    return;
            }

            if (!_mtr.IsSelecting)
                return;

            _mtr.SelectionEnd = currentPoint;
            _mtr.UpdateSelectedText();
            ModtermCanvas.Invalidate();
        }

        private void ModtermCanvas_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            Point currentPoint = e.GetCurrentPoint(ModtermCanvas).Position;
            var props = e.GetCurrentPoint(ModtermCanvas).Properties;
            int button = ButtonFromUpdateKind(props.PointerUpdateKind, pressed: false);
            bool shift = IsKeyDown(VirtualKey.Shift);

            if (_mouseReportButton >= 0)
            {
                int reportButton = button >= 0 ? button : _mouseReportButton;
                if (ShouldSendMouseRelease())
                    TryReportMouse(reportButton, release: true, motion: false, currentPoint, clamp: true);

                EndMouseReport(e.Pointer);
                e.Handled = true;
                return;
            }

            if (!_mtr.IsSelecting)
                return;

            _mtr.SelectionEnd = currentPoint;
            _mtr.UpdateSelectedText();
            _mtr.IsSelecting = false;
            _mtr.CopySelectedTextToClipboard();
            ModtermCanvas.Invalidate();
        }

        private void ModtermCanvas_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            if (_mouseReportButton < 0)
                return;

            if (ShouldSendMouseRelease())
            {
                TryReportMouse(
                    _mouseReportButton,
                    release: true,
                    motion: false,
                    e.GetCurrentPoint(ModtermCanvas).Position,
                    clamp: true);
            }

            EndMouseReport(pointer: null);
        }

        private void ModtermCanvas_PointerCanceled(object sender, PointerRoutedEventArgs e)
        {
            ModtermCanvas_PointerCaptureLost(sender, e);
        }

        private void ModtermCanvas_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            if (_ptyConsumedRightClick && !IsKeyDown(VirtualKey.Shift))
                return;

            _flyout.ShowAt(ModtermCanvas, e.GetPosition(ModtermCanvas));
        }

        private void ModtermCanvas_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            int delta = e.GetCurrentPoint(ModtermCanvas).Properties.MouseWheelDelta;
            bool shift = IsKeyDown(VirtualKey.Shift);
            Point currentPoint = e.GetCurrentPoint(ModtermCanvas).Position;

            if (!shift && ShouldReportMouseToPty(shift: false) && _mtr.Terminal.MouseMode != MouseMode.X10)
            {
                int notches = Math.Max(1, Math.Abs(delta) / 120);
                int button = delta > 0 ? 4 : 5;
                for (int i = 0; i < notches; i++)
                    TryReportMouse(button, release: false, motion: false, currentPoint, clamp: true);

                e.Handled = true;
                return;
            }

            int scrollNotches = Math.Max(1, Math.Abs(delta) / 120);
            int rowsPerNotch = Math.Max(1, _mtr.Lines / 10);
            int rows = scrollNotches * rowsPerNotch * (delta > 0 ? 1 : -1);

            _mtr.ScrollBackBy(rows);
            e.Handled = true;
        }

        private void SendPtyInput(string text)
        {
            if (string.IsNullOrEmpty(text) || ConPtyTerminal is null)
                return;

            _mtr.ScrollOffset = 0;
            ConPtyTerminal.WriteInput(text);
            ModtermCanvas.Invalidate();
        }

        private void SendPtyMouse(string sequence)
        {
            if (string.IsNullOrEmpty(sequence) || ConPtyTerminal is null)
                return;

            // X10/UTF8 mouse encodings are raw 8-bit; SGR/URXVT are ASCII.
            var protocol = _mtr.Terminal.MouseProtocol;
            if (protocol is MouseProtocolEncoding.SGR or MouseProtocolEncoding.URXVT)
                ConPtyTerminal.WriteInput(sequence);
            else
                ConPtyTerminal.WriteInput(Encoding.Latin1.GetBytes(sequence));
        }

        private bool ShouldReportMouseToPty(bool shift)
        {
            // Shift+click is the xterm/Alacritty override for host selection
            // while an application has mouse tracking enabled.
            return !shift && _mtr.Terminal.MouseMode != MouseMode.Off;
        }

        private bool ShouldSendMouseRelease()
        {
            return _mtr.Terminal.MouseMode.SendButtonRelease()
                && _mtr.Terminal.MouseMode != MouseMode.X10;
        }

        private bool ShouldScrollWithPagingKeys()
        {
            var terminal = _mtr.Terminal;
            return !terminal.Buffers.IsAlternateBuffer
                && !terminal.ApplicationCursor
                && terminal.MouseMode == MouseMode.Off;
        }

        private bool TryReportMouse(int button, bool release, bool motion, Point point, bool clamp)
        {
            if (!_mtr.TryGetViewportCell(point, clamp, out int col, out int row))
                return false;

            if (motion && col == _lastReportedMouseCol && row == _lastReportedMouseRow)
                return true;

            bool alt = IsKeyDown(VirtualKey.Menu);
            bool ctrl = IsKeyDown(VirtualKey.Control);
            bool shift = IsKeyDown(VirtualKey.Shift);
            string seq = VtUserInput.EncodeMouse(
                _mtr.Terminal.MouseProtocol,
                button,
                release,
                motion,
                col,
                row,
                shift,
                alt,
                ctrl);

            _lastReportedMouseCol = col;
            _lastReportedMouseRow = row;
            SendPtyMouse(seq);
            return true;
        }

        private void EndMouseReport(Pointer? pointer)
        {
            _mouseReportButton = -1;
            _lastReportedMouseCol = -1;
            _lastReportedMouseRow = -1;
            if (pointer is not null)
                ModtermCanvas.ReleasePointerCapture(pointer);
        }

        private void ClearSelectionVisual()
        {
            if (!_mtr.IsSelecting && _mtr.SelectionRange is null && string.IsNullOrEmpty(_mtr.SelectedText))
                return;

            _mtr.IsSelecting = false;
            _mtr.SelectionRange = null;
            _mtr.SelectedText = "";
            ModtermCanvas.Invalidate();
        }

        private static int ButtonFromUpdateKind(PointerUpdateKind kind, bool pressed)
        {
            if (pressed)
            {
                return kind switch
                {
                    PointerUpdateKind.LeftButtonPressed => 0,
                    PointerUpdateKind.MiddleButtonPressed => 1,
                    PointerUpdateKind.RightButtonPressed => 2,
                    _ => -1
                };
            }

            return kind switch
            {
                PointerUpdateKind.LeftButtonReleased => 0,
                PointerUpdateKind.MiddleButtonReleased => 1,
                PointerUpdateKind.RightButtonReleased => 2,
                _ => -1
            };
        }

        private static int ButtonFromPressedState(PointerPointProperties props)
        {
            if (props.IsLeftButtonPressed)
                return 0;
            if (props.IsMiddleButtonPressed)
                return 1;
            if (props.IsRightButtonPressed)
                return 2;
            return 3;
        }

        private static bool IsKeyDown(VirtualKey key)
        {
            return InputKeyboardSource.GetKeyStateForCurrentThread(key)
                .HasFlag(CoreVirtualKeyStates.Down);
        }

        private static bool IsCapsLockOn()
        {
            var state = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.CapitalLock);
            return state.HasFlag(CoreVirtualKeyStates.Locked);
        }
    }
}
