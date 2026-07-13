using System;

namespace modterm
{
    internal static class TerminalCursorStyles
    {
        public const string Solid = "Solid";
        public const string Underline = "Underline";

        public static string Normalize(string? value)
        {
            if (string.Equals(value, Underline, StringComparison.OrdinalIgnoreCase))
            {
                return Underline;
            }

            return Solid;
        }

        public static bool IsUnderline(string? value) =>
            string.Equals(Normalize(value), Underline, StringComparison.OrdinalIgnoreCase);
    }
}
