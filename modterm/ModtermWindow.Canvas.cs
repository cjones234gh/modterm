using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using System;
using System.Text;
using Windows.Foundation;
using Windows.System;
using Windows.UI.Core;
using modterm.Ghostty;

namespace modterm
{
    public sealed partial class ModtermWindow : Window
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

            if (!VtUserInput.TryMapKey(e.Key, out GhosttyVtKey vtKey))
                return;

            GhosttyVtMods mods = VtUserInput.MapMods(shift, alt, ctrl, capsLock);
            string? text = (!ctrl && !alt)
                ? VtUserInput.MapPrintable(e.Key, shift, capsLock)?.ToString()
                : null;
            GhosttyVtKeyAction action = e.KeyStatus.WasKeyDown
                ? GhosttyVtKeyAction.Repeat
                : GhosttyVtKeyAction.Press;

            byte[] encoded = _mtr.Terminal.EncodeKey(
                vtKey,
                action,
                mods,
                text,
                VtUserInput.UnshiftedCodepoint(e.Key));

            if (encoded.Length == 0)
                return;

            SendPtyBytes(encoded);
            _ptyHeldKeys.Add(e.Key);
            e.Handled = true;
            _keyDownSentToPty = true;
        }

        private void ModtermCanvas_KeyUp(object sender, KeyRoutedEventArgs e)
        {
            if (!_ptyHeldKeys.Remove(e.Key))
                return;

            if (!VtUserInput.TryMapKey(e.Key, out GhosttyVtKey vtKey))
                return;

            GhosttyVtMods mods = VtUserInput.MapMods(
                IsKeyDown(VirtualKey.Shift),
                IsKeyDown(VirtualKey.Menu),
                IsKeyDown(VirtualKey.Control),
                IsCapsLockOn());

            byte[] encoded = _mtr.Terminal.EncodeKey(
                vtKey,
                GhosttyVtKeyAction.Release,
                mods,
                text: null,
                VtUserInput.UnshiftedCodepoint(e.Key));

            if (encoded.Length == 0)
                return;

            SendPtyBytes(encoded);
            e.Handled = true;
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

            GhosttyVtMods mods = VtUserInput.MapMods(
                IsKeyDown(VirtualKey.Shift),
                IsKeyDown(VirtualKey.Menu),
                IsKeyDown(VirtualKey.Control),
                IsCapsLockOn());

            string text = ch.ToString();
            byte[] encoded = _mtr.Terminal.EncodeKey(
                GhosttyVtKey.Unidentified,
                GhosttyVtKeyAction.Press,
                mods,
                text,
                ch);

            if (encoded.Length == 0)
                encoded = Encoding.UTF8.GetBytes(text);

            SendPtyBytes(encoded);
            e.Handled = true;
        }

        private void ModtermWindow_Activated(object sender, Microsoft.UI.Xaml.WindowActivatedEventArgs e)
        {
            if (_mtr.Terminal is null || !_mtr.Terminal.FocusReporting)
                return;

            bool gained = e.WindowActivationState != WindowActivationState.Deactivated;
            SendPtyBytes(_mtr.Terminal.EncodeFocus(gained));
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
                if (TryReportMouse(
                    GhosttyMouseAction.Press,
                    MouseButtonFromReport(button),
                    anyPressed: true,
                    currentPoint,
                    clamp: false))
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

            _mtr.ClearHostSelection();

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
            var terminal = _mtr.Terminal;

            if (ShouldReportMouseToPty(shift))
            {
                bool buttonDown = props.IsLeftButtonPressed || props.IsMiddleButtonPressed || props.IsRightButtonPressed;
                GhosttyMouseButtonId? button = _mouseReportButton >= 0
                    ? MouseButtonFromReport(_mouseReportButton)
                    : MouseButtonFromPressedState(props);

                if (buttonDown && terminal.MouseButtonTracking)
                {
                    TryReportMouse(GhosttyMouseAction.Motion, button, anyPressed: true, currentPoint, clamp: true);
                    e.Handled = true;
                    return;
                }

                if (!buttonDown && terminal.MouseAnyTracking)
                {
                    TryReportMouse(GhosttyMouseAction.Motion, null, anyPressed: false, currentPoint, clamp: false);
                    e.Handled = true;
                    return;
                }

                if (terminal.MouseTracking && !shift)
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

            if (_mouseReportButton >= 0)
            {
                int reportButton = button >= 0 ? button : _mouseReportButton;
                if (ShouldSendMouseRelease())
                {
                    TryReportMouse(
                        GhosttyMouseAction.Release,
                        MouseButtonFromReport(reportButton),
                        anyPressed: false,
                        currentPoint,
                        clamp: true);
                }

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
                    GhosttyMouseAction.Release,
                    MouseButtonFromReport(_mouseReportButton),
                    anyPressed: false,
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

            if (!shift && ShouldReportMouseToPty(shift: false) && !_mtr.Terminal.MouseX10)
            {
                int notches = Math.Max(1, Math.Abs(delta) / 120);
                GhosttyMouseButtonId wheel = delta > 0 ? GhosttyMouseButtonId.Four : GhosttyMouseButtonId.Five;
                for (int i = 0; i < notches; i++)
                    TryReportMouse(GhosttyMouseAction.Press, wheel, anyPressed: false, currentPoint, clamp: true);

                e.Handled = true;
                return;
            }

            int scrollNotches = Math.Max(1, Math.Abs(delta) / 120);
            int rowsPerNotch = Math.Max(1, _mtr.Lines / 10);
            int rows = scrollNotches * rowsPerNotch * (delta > 0 ? 1 : -1);

            _mtr.ScrollBackBy(rows);
            e.Handled = true;
        }

        private void SendPtyBytes(byte[] data)
        {
            if (data is not { Length: > 0 } || ConPtyTerminal is null)
                return;

            _mtr.FollowLiveOutput();
            ConPtyTerminal.WriteInput(data);
            ModtermCanvas.Invalidate();
        }

        private bool ShouldReportMouseToPty(bool shift)
        {
            // Shift+click is the xterm/Alacritty override for host selection
            // while an application has mouse tracking enabled.
            return !shift && _mtr.Terminal.MouseTracking;
        }

        private bool ShouldSendMouseRelease()
        {
            return _mtr.Terminal.MouseTracking && !_mtr.Terminal.MouseX10;
        }

        private bool ShouldScrollWithPagingKeys()
        {
            var terminal = _mtr.Terminal;
            return !terminal.AlternateScreen
                && !terminal.ApplicationCursor
                && !terminal.MouseTracking;
        }

        private bool TryReportMouse(
            GhosttyMouseAction action,
            GhosttyMouseButtonId? button,
            bool anyPressed,
            Point point,
            bool clamp)
        {
            if (!_mtr.TryGetViewportCell(point, clamp, out int col, out int row))
                return false;

            if (action == GhosttyMouseAction.Motion && col == _lastReportedMouseCol && row == _lastReportedMouseRow)
                return true;

            GhosttyVtMods mods = VtUserInput.MapMods(
                IsKeyDown(VirtualKey.Shift),
                IsKeyDown(VirtualKey.Menu),
                IsKeyDown(VirtualKey.Control),
                capsLock: false);

            uint screenWidth = (uint)Math.Max(1, Math.Round(ModtermCanvas.ActualWidth));
            uint screenHeight = (uint)Math.Max(1, Math.Round(ModtermCanvas.ActualHeight));
            byte[] encoded = _mtr.Terminal.EncodeMouse(
                action,
                button,
                (float)point.X,
                (float)point.Y,
                mods,
                anyPressed,
                screenWidth,
                screenHeight,
                _mtr.CellWidthPixels,
                _mtr.CellHeightPixels,
                _mtr.PaddingLeft,
                _mtr.PaddingTop);

            _lastReportedMouseCol = col;
            _lastReportedMouseRow = row;
            SendPtyBytes(encoded);
            return encoded.Length > 0 || action == GhosttyMouseAction.Motion;
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

            _mtr.ClearHostSelection();
            ModtermCanvas.Invalidate();
        }

        private static GhosttyMouseButtonId? MouseButtonFromReport(int button)
        {
            return button switch
            {
                0 => GhosttyMouseButtonId.Left,
                1 => GhosttyMouseButtonId.Middle,
                2 => GhosttyMouseButtonId.Right,
                4 => GhosttyMouseButtonId.Four,
                5 => GhosttyMouseButtonId.Five,
                _ => null
            };
        }

        private static GhosttyMouseButtonId? MouseButtonFromPressedState(PointerPointProperties props)
        {
            if (props.IsLeftButtonPressed)
                return GhosttyMouseButtonId.Left;
            if (props.IsMiddleButtonPressed)
                return GhosttyMouseButtonId.Middle;
            if (props.IsRightButtonPressed)
                return GhosttyMouseButtonId.Right;
            return null;
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
