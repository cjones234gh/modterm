using System;
using System.Text;
using Windows.System;
using XtermSharp;

namespace modterm
{
    /// <summary>
    /// Encodes keyboard and mouse input as xterm VT sequences.
    /// Matches the sequences modern TUI apps (and terminals like Alacritty) expect.
    /// </summary>
    internal static class VtUserInput
    {
        public const string FocusIn = "\x1b[I";
        public const string FocusOut = "\x1b[O";

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

        /// <summary>
        /// xterm modifier parameter: 1 + shift + 2*alt + 4*ctrl.
        /// </summary>
        public static int ModifierParam(bool shift, bool alt, bool ctrl)
        {
            return 1 + (shift ? 1 : 0) + (alt ? 2 : 0) + (ctrl ? 4 : 0);
        }

        public static string WrapPaste(string text, bool bracketed)
        {
            if (!bracketed || string.IsNullOrEmpty(text))
                return text;

            return "\x1b[200~" + text + "\x1b[201~";
        }

        public static string? EncodeKey(
            VirtualKey key,
            bool ctrl,
            bool alt,
            bool shift,
            bool capsLock,
            bool applicationCursor)
        {
            int mods = ModifierParam(shift, alt, ctrl);
            bool modified = mods != 1;

            switch (key)
            {
                case VirtualKey.Enter:
                    return PrefixAlt(alt, ctrl ? "\n" : "\r");
                case VirtualKey.Tab:
                    if (shift && !ctrl && !alt)
                        return "\x1b[Z";
                    return modified ? CsiTilde(9, mods) : PrefixAlt(alt, "\t");
                case VirtualKey.Back:
                    if (ctrl && !alt)
                        return "\x08";
                    return PrefixAlt(alt, "\x7f");
                case VirtualKey.Escape:
                    return "\x1b";
                case VirtualKey.Left:
                    return CursorKey('D', applicationCursor, mods);
                case VirtualKey.Right:
                    return CursorKey('C', applicationCursor, mods);
                case VirtualKey.Up:
                    return CursorKey('A', applicationCursor, mods);
                case VirtualKey.Down:
                    return CursorKey('B', applicationCursor, mods);
                case VirtualKey.Home:
                    return CursorKey('H', applicationCursor, mods);
                case VirtualKey.End:
                    return CursorKey('F', applicationCursor, mods);
                case VirtualKey.Insert:
                    return CsiTilde(2, mods);
                case VirtualKey.Delete:
                    return CsiTilde(3, mods);
                case VirtualKey.PageUp:
                    return CsiTilde(5, mods);
                case VirtualKey.PageDown:
                    return CsiTilde(6, mods);
                case VirtualKey.F1:
                    return FunctionKey('P', mods);
                case VirtualKey.F2:
                    return FunctionKey('Q', mods);
                case VirtualKey.F3:
                    return FunctionKey('R', mods);
                case VirtualKey.F4:
                    return FunctionKey('S', mods);
                case VirtualKey.F5:
                    return CsiTilde(15, mods);
                case VirtualKey.F6:
                    return CsiTilde(17, mods);
                case VirtualKey.F7:
                    return CsiTilde(18, mods);
                case VirtualKey.F8:
                    return CsiTilde(19, mods);
                case VirtualKey.F9:
                    return CsiTilde(20, mods);
                case VirtualKey.F10:
                    return CsiTilde(21, mods);
                case VirtualKey.F11:
                    return CsiTilde(23, mods);
                case VirtualKey.F12:
                    return CsiTilde(24, mods);
            }

            if (ctrl && !alt)
            {
                string? ctrlSeq = EncodeControlKey(key, shift);
                if (ctrlSeq is not null)
                    return ctrlSeq;
            }

            char? printable = MapPrintable(key, shift, capsLock);
            if (printable is null)
                return null;

            string text = printable.Value.ToString();
            if (alt && !ctrl)
                return "\x1b" + text;

            return text;
        }

        public static string EncodeMouse(
            MouseProtocolEncoding protocol,
            int button,
            bool release,
            bool motion,
            int x,
            int y,
            bool shift,
            bool alt,
            bool ctrl)
        {
            int cb = EncodeButton(button);
            if (shift)
                cb |= 4;
            if (alt)
                cb |= 8;
            if (ctrl)
                cb |= 16;
            if (motion)
                cb |= 32;

            int px = x + 1;
            int py = y + 1;

            switch (protocol)
            {
                case MouseProtocolEncoding.SGR:
                    int sgr = cb;
                    if (release && !motion)
                    {
                        sgr = EncodeButton(button);
                        if (shift)
                            sgr |= 4;
                        if (alt)
                            sgr |= 8;
                        if (ctrl)
                            sgr |= 16;
                    }

                    char final = (release && !motion) ? 'm' : 'M';
                    return $"\x1b[<{sgr};{px};{py}{final}";

                case MouseProtocolEncoding.URXVT:
                    int urxvt = (release && !motion) ? (3 | (cb & ~3)) : cb;
                    return $"\x1b[{urxvt + 32};{px};{py}M";

                case MouseProtocolEncoding.UTF8:
                    int utfCb = (release && !motion) ? 3 : cb;
                    var utf8 = new StringBuilder(8);
                    utf8.Append("\x1b[M");
                    AppendMouseUtf8(utf8, utfCb + 32);
                    AppendMouseUtf8(utf8, px + 32);
                    AppendMouseUtf8(utf8, py + 32);
                    return utf8.ToString();

                default:
                    int x10 = (release && !motion) ? 3 : cb;
                    int cx = Math.Min(255, 32 + px);
                    int cy = Math.Min(255, 32 + py);
                    return $"\x1b[M{(char)(x10 + 32)}{(char)cx}{(char)cy}";
            }
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

        private static string? EncodeControlKey(VirtualKey key, bool shift)
        {
            if (key >= VirtualKey.A && key <= VirtualKey.Z)
                return ((char)(key - VirtualKey.A + 1)).ToString();

            if (key == VirtualKey.Space)
                return "\0";

            if (key >= VirtualKey.Number0 && key <= VirtualKey.Number9)
            {
                return (key, shift) switch
                {
                    (VirtualKey.Number2, _) => "\0",
                    (VirtualKey.Number3, _) => "\x1b",
                    (VirtualKey.Number4, _) => "\x1c",
                    (VirtualKey.Number5, _) => "\x1d",
                    (VirtualKey.Number6, _) => "\x1e",
                    (VirtualKey.Number7, _) => "\x1f",
                    (VirtualKey.Number8, _) => "\x7f",
                    _ => null
                };
            }

            return key switch
            {
                (VirtualKey)219 => "\x1b", // Ctrl+[
                (VirtualKey)220 => "\x1c", // Ctrl+\
                (VirtualKey)221 => "\x1d", // Ctrl+]
                (VirtualKey)192 => "\0",   // Ctrl+@
                (VirtualKey)189 => "\x1f", // Ctrl+- / Ctrl+_
                (VirtualKey)191 => "\x1f", // Ctrl+/
                (VirtualKey)190 => shift ? "\x1e" : null, // Ctrl+^
                _ => null
            };
        }

        private static string PrefixAlt(bool alt, string sequence)
        {
            return alt ? "\x1b" + sequence : sequence;
        }

        private static string CursorKey(char letter, bool applicationCursor, int mods)
        {
            if (mods != 1)
                return $"\x1b[1;{mods}{letter}";

            return applicationCursor ? $"\x1bO{letter}" : $"\x1b[{letter}";
        }

        private static string FunctionKey(char ss3Letter, int mods)
        {
            if (mods != 1)
                return $"\x1b[1;{mods}{ss3Letter}";

            return $"\x1bO{ss3Letter}";
        }

        private static string CsiTilde(int number, int mods)
        {
            return mods == 1 ? $"\x1b[{number}~" : $"\x1b[{number};{mods}~";
        }

        private static int EncodeButton(int button)
        {
            return button switch
            {
                0 => 0,
                1 => 1,
                2 => 2,
                3 => 3,
                4 => 64,
                5 => 65,
                _ => 0
            };
        }

        private static void AppendMouseUtf8(StringBuilder data, int ch)
        {
            if (ch == 2047)
            {
                data.Append('\0');
                return;
            }

            if (ch < 127)
            {
                data.Append((char)ch);
                return;
            }

            if (ch > 2047)
                ch = 2047;

            data.Append((char)(0xC0 | (ch >> 6)));
            data.Append((char)(0x80 | (ch & 0x3F)));
        }
    }
}
