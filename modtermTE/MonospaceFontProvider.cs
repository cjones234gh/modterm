using Microsoft.Graphics.Canvas.Text;
using System;
using System.Collections.Generic;
using System.Linq;

namespace modtermTE
{
    internal static class MonospaceFontProvider
    {
        public static IReadOnlyList<string> GetFontFamilyNames()
        {
            var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var bundledFont in modterm.BundledFonts.BundledFontFamilyNames)
            {
                names.Add(bundledFont);
            }

            using var fontSet = CanvasFontSet.GetSystemFontSet();
            foreach (var face in fontSet.Fonts)
            {
                if (!face.IsMonospaced)
                {
                    continue;
                }

                foreach (var familyName in face.FamilyNames.Values)
                {
                    if (!string.IsNullOrWhiteSpace(familyName))
                    {
                        names.Add(familyName);
                    }
                }
            }

            return names.ToList();
        }
    }
}
