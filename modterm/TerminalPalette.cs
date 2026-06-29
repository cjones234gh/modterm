using System;
using System.Collections.Generic;
using Windows.UI;

namespace modterm
{
    /// <summary>
    /// Maps the sixteen standard ANSI palette slots (indices 0-15) to optional theme overrides.
    /// Keys in theme JSON are case-insensitive; bright slots also accept snake_case aliases
    /// (e.g. "bright_green").
    /// </summary>
    internal static class TerminalPalette
    {
        public static readonly string[] StandardNames =
        {
            "Black",
            "Red",
            "Green",
            "Yellow",
            "Blue",
            "Magenta",
            "Cyan",
            "White",
            "BrightBlack",
            "BrightRed",
            "BrightGreen",
            "BrightYellow",
            "BrightBlue",
            "BrightMagenta",
            "BrightCyan",
            "BrightWhite"
        };

        private static readonly Dictionary<string, int> NameToIndex = BuildNameToIndex();

        public static bool TryGetIndex(string name, out int index)
            => NameToIndex.TryGetValue(name, out index);

        public static bool TryGetColor(Dictionary<string, Color>? palette, int index, out Color color)
        {
            color = default;
            if (palette is null || index < 0 || index >= StandardNames.Length)
            {
                return false;
            }

            if (palette.TryGetValue(StandardNames[index], out color))
            {
                return true;
            }

            foreach (var entry in palette)
            {
                if (TryGetIndex(entry.Key, out int entryIndex) && entryIndex == index)
                {
                    color = entry.Value;
                    return true;
                }
            }

            return false;
        }

        private static Dictionary<string, int> BuildNameToIndex()
        {
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < StandardNames.Length; i++)
            {
                string name = StandardNames[i];
                map[name] = i;

                if (name.StartsWith("Bright", StringComparison.Ordinal))
                {
                    string baseName = name.Substring("Bright".Length);
                    map[$"bright_{baseName}"] = i;
                    map[$"bright-{baseName}"] = i;
                }
            }

            return map;
        }
    }
}
