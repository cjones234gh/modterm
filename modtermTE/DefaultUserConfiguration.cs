using System;
using System.Collections.Generic;
using Microsoft.UI;
using Windows.Foundation;
using Windows.UI;

namespace modtermTE
{
    internal static class DefaultUserConfiguration
    {
        public static UserAppConfiguration Create()
        {
            return new UserAppConfiguration
            {
                LastWindowLocation = new Point(100, 100),
                WindowSize = new Size(800, 600),
                TerminalFont = "BlexMono Nerd Font Mono",
                LabelFont = "BlexMono Nerd Font Mono",
                TerminalFontSize = 12.0f,
                TerminalShell = new Shell
                {
                    Name = "powershell",
                    Path = "conhost",
                    Arguments = " --headless --width [W] --height [H] -- C:\\Windows\\System32\\WindowsPowerShell\\v1.0\\powershell.exe"
                },
                TerminalCursor = "bar",
                ThemeConfiguration = new ThemeConfiguration
                {
                    Name = "Purpleser",
                    OutputColor = ColorFromHex("#50ff8c"),
                    OutputBlurColor = ColorFromHex("#00eeff"),
                    LabelColor = ColorFromHex("#ff4fc1"),
                    LabelBlurColor = ColorFromHex("#3333ff"),
                    BlurAmount = 12.0f,
                    WindowOpacityPct = 20,
                    WindowColor = ColorFromHex("#000066"),
                    BackdropKind = BackdropKind.Blurred,
                    Palette = null // Let CreatePaletteMappingControl() initialize with GetDefaultAnsiColor() for consistency
                },
                ShellConfigurations =
                [
                    new Shell { Name = "cmd", Path = "conhost", Arguments = " --headless --width [W] --height [H] -- C:\\Windows\\System32\\cmd.exe" },
                    new Shell { Name = "powershell", Path = "conhost", Arguments = " --headless --width [W] --height [H] -- C:\\Windows\\System32\\WindowsPowerShell\\v1.0\\powershell.exe" },
                    new Shell { Name = "bash", Path = "conhost", Arguments = " --headless --width [W] --height [H] -- C:\\Program Files\\Git\\bin\\bash.exe" },
                    new Shell { Name = "wsl", Path = "conhost", Arguments = " --headless --width [W] --height [H] -- wsl.exe" },
                ]
            };
        }

        private static Color ColorFromHex(string hex)
        {
            hex = hex.TrimStart('#');
            byte a = 255;
            byte r = Convert.ToByte(hex.Substring(0, 2), 16);
            byte g = Convert.ToByte(hex.Substring(2, 2), 16);
            byte b = Convert.ToByte(hex.Substring(4, 2), 16);
            return Color.FromArgb(a, r, g, b);
        }
    }
}
