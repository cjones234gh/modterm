using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace modterm
{
    internal static class BundledFonts
    {
        public const string BlexMonoNerdFontFamilyName = "BlexMono Nerd Font Mono";

        private const string BlexMonoRegularFileName = "BlexMonoNerdFontMono-Regular.ttf";
        private const string BlexMonoBoldFileName = "BlexMonoNerdFontMono-Bold.ttf";

        private const uint FrPrivate = 0x10;

        public static IReadOnlyList<string> BundledFontFamilyNames { get; } =
            new[] { BlexMonoNerdFontFamilyName };

        public static void RegisterBundledFonts()
        {
            RegisterFontFile(BlexMonoRegularFileName);
            RegisterFontFile(BlexMonoBoldFileName);
        }

        public static bool IsBundledFont(string fontFamilyName) =>
            string.Equals(fontFamilyName, BlexMonoNerdFontFamilyName, StringComparison.OrdinalIgnoreCase);

        public static string ResolveFontFamily(string fontFamilyName, bool bold = false)
        {
            if (!IsBundledFont(fontFamilyName))
            {
                return fontFamilyName;
            }

            return GetBlexMonoFontFamilyUri(bold);
        }

        /// <summary>
        /// The bundled bold face is a separate TTF. Requesting FontWeight.Bold against
        /// the regular file makes DirectWrite synthesize a wider outline.
        /// </summary>
        public static bool BundledBoldUsesDedicatedFace(string fontFamilyName) =>
            IsBundledFont(fontFamilyName);

        private static string GetBlexMonoFontFamilyUri(bool bold)
        {
            string fileName = bold ? BlexMonoBoldFileName : BlexMonoRegularFileName;
            string fontPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts", fileName);
            if (File.Exists(fontPath))
            {
                return $"{new Uri(fontPath).AbsoluteUri}#{BlexMonoNerdFontFamilyName}";
            }

            return $"ms-appx:///Assets/Fonts/{fileName}#{BlexMonoNerdFontFamilyName}";
        }

        private static void RegisterFontFile(string fileName)
        {
            string fontPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts", fileName);
            if (File.Exists(fontPath))
            {
                AddFontResourceEx(fontPath, FrPrivate, IntPtr.Zero);
            }
        }

        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
        private static extern int AddFontResourceEx(string lpszFilename, uint fl, IntPtr pdv);
    }
}
