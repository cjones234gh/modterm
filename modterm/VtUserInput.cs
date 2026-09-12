using System;
using Windows.System;
using modterm.Ghostty;

namespace modterm
{
    /// <summary>
    /// Maps WinUI keys to libghostty key identities and modifier flags.
    /// </summary>
    internal static class VtUserInput
    {
        public static bool IsModifierOnly(VirtualKey key)
        {
            return key is VirtualKey.Control or VirtualKey.LeftControl or VirtualKey.RightControl
                or VirtualKey.Shift or VirtualKey.LeftShift or VirtualKey.RightShift
                or VirtualKey.Menu or VirtualKey.LeftMenu or VirtualKey.RightMenu
                or VirtualKey.LeftWindows or VirtualKey.RightWindows
                or VirtualKey.CapitalLock or VirtualKey.NumberKeyLock or VirtualKey.Scroll;
        }

        public static bool IsAltKey(VirtualKey key)
        {
            return key is VirtualKey.Menu or VirtualKey.LeftMenu or VirtualKey.RightMenu;
        }

        public static GhosttyVtMods MapMods(bool shift, bool alt, bool ctrl, bool capsLock)
        {
            GhosttyVtMods mods = GhosttyVtMods.None;
            if (shift)
                mods |= GhosttyVtMods.Shift;
            if (alt)
                mods |= GhosttyVtMods.Alt;
            if (ctrl)
                mods |= GhosttyVtMods.Ctrl;
            if (capsLock)
                mods |= GhosttyVtMods.CapsLock;
            return mods;
        }

        public static bool TryMapKey(VirtualKey key, out GhosttyVtKey vtKey)
        {
            if (key >= VirtualKey.A && key <= VirtualKey.Z)
            {
                vtKey = GhosttyVtKey.A + (key - VirtualKey.A);
                return true;
            }

            if (key >= VirtualKey.Number0 && key <= VirtualKey.Number9)
            {
                vtKey = GhosttyVtKey.Digit0 + (key - VirtualKey.Number0);
                return true;
            }

            if (key >= VirtualKey.NumberPad0 && key <= VirtualKey.NumberPad9)
            {
                vtKey = GhosttyVtKey.Numpad0 + (key - VirtualKey.NumberPad0);
                return true;
            }

            if (key >= VirtualKey.F1 && key <= VirtualKey.F12)
            {
                vtKey = GhosttyVtKey.F1 + (key - VirtualKey.F1);
                return true;
            }

            vtKey = key switch
            {
                VirtualKey.Enter => GhosttyVtKey.Enter,
                VirtualKey.Tab => GhosttyVtKey.Tab,
                VirtualKey.Back => GhosttyVtKey.Backspace,
                VirtualKey.Escape => GhosttyVtKey.Escape,
                VirtualKey.Space => GhosttyVtKey.Space,
                VirtualKey.Left => GhosttyVtKey.ArrowLeft,
                VirtualKey.Right => GhosttyVtKey.ArrowRight,
                VirtualKey.Up => GhosttyVtKey.ArrowUp,
                VirtualKey.Down => GhosttyVtKey.ArrowDown,
                VirtualKey.Home => GhosttyVtKey.Home,
                VirtualKey.End => GhosttyVtKey.End,
                VirtualKey.Insert => GhosttyVtKey.Insert,
                VirtualKey.Delete => GhosttyVtKey.Delete,
                VirtualKey.PageUp => GhosttyVtKey.PageUp,
                VirtualKey.PageDown => GhosttyVtKey.PageDown,
                VirtualKey.Decimal => GhosttyVtKey.NumpadDecimal,
                VirtualKey.Add => GhosttyVtKey.NumpadAdd,
                VirtualKey.Subtract => GhosttyVtKey.NumpadSubtract,
                VirtualKey.Multiply => GhosttyVtKey.NumpadMultiply,
                VirtualKey.Divide => GhosttyVtKey.NumpadDivide,
                VirtualKey.Separator => GhosttyVtKey.NumpadSeparator,
                (VirtualKey)186 => GhosttyVtKey.Semicolon,
                (VirtualKey)187 => GhosttyVtKey.Equal,
                (VirtualKey)188 => GhosttyVtKey.Comma,
                (VirtualKey)189 => GhosttyVtKey.Minus,
                (VirtualKey)190 => GhosttyVtKey.Period,
                (VirtualKey)191 => GhosttyVtKey.Slash,
                (VirtualKey)192 => GhosttyVtKey.Backquote,
                (VirtualKey)219 => GhosttyVtKey.BracketLeft,
                (VirtualKey)220 => GhosttyVtKey.Backslash,
                (VirtualKey)221 => GhosttyVtKey.BracketRight,
                (VirtualKey)222 => GhosttyVtKey.Quote,
                _ => GhosttyVtKey.Unidentified,
            };
            return vtKey != GhosttyVtKey.Unidentified;
        }

        public static uint UnshiftedCodepoint(VirtualKey key)
        {
            if (key >= VirtualKey.A && key <= VirtualKey.Z)
                return (uint)('a' + (key - VirtualKey.A));
            if (key >= VirtualKey.Number0 && key <= VirtualKey.Number9)
                return (uint)('0' + (key - VirtualKey.Number0));
            return MapPrintable(key, shift: false, capsLock: false) is char ch ? ch : 0u;
        }

        public static char? MapPrintable(VirtualKey key, bool shift, bool capsLock)
        {
            if (key >= VirtualKey.A && key <= VirtualKey.Z)
            {
                char c = (char)('a' + (key - VirtualKey.A));
                bool upper = shift ^ capsLock;
                return upper ? char.ToUpperInvariant(c) : c;
            }

            if (key >= VirtualKey.Number0 && key <= VirtualKey.Number9)
            {
                char digit = (char)('0' + (key - VirtualKey.Number0));
                if (!shift)
                    return digit;

                return digit switch
                {
                    '1' => '!',
                    '2' => '@',
                    '3' => '#',
                    '4' => '$',
                    '5' => '%',
                    '6' => '^',
                    '7' => '&',
                    '8' => '*',
                    '9' => '(',
                    '0' => ')',
                    _ => digit
                };
            }

            if (key >= VirtualKey.NumberPad0 && key <= VirtualKey.NumberPad9)
                return (char)('0' + (key - VirtualKey.NumberPad0));

            return key switch
            {
                VirtualKey.Space => ' ',
                VirtualKey.Decimal => '.',
                VirtualKey.Add or VirtualKey.Separator => '+',
                VirtualKey.Subtract => '-',
                VirtualKey.Multiply => '*',
                VirtualKey.Divide => '/',
                (VirtualKey)188 => shift ? '<' : ',',
                (VirtualKey)190 => shift ? '>' : '.',
                (VirtualKey)189 => shift ? '_' : '-',
                (VirtualKey)187 => shift ? '+' : '=',
                (VirtualKey)191 => shift ? '?' : '/',
                (VirtualKey)186 => shift ? ':' : ';',
                (VirtualKey)222 => shift ? '"' : '\'',
                (VirtualKey)219 => shift ? '{' : '[',
                (VirtualKey)221 => shift ? '}' : ']',
                (VirtualKey)220 => shift ? '|' : '\\',
                (VirtualKey)192 => shift ? '~' : '`',
                _ => null
            };
        }
    }
}
