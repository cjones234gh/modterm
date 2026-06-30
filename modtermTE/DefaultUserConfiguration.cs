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
                    Palette = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["Black"] = ColorFromHex("#0C0C0C"),
                        ["Red"] = ColorFromHex("#C50F15"),
                        ["Green"] = ColorFromHex("#13A10E"),
                        ["Yellow"] = ColorFromHex("#C19C00"),
                        ["Blue"] = ColorFromHex("#0037DA"),
                        ["Magenta"] = ColorFromHex("#881798"),
                        ["Cyan"] = ColorFromHex("#3A96DD"),
                        ["White"] = ColorFromHex("#CCCCCC"),
                        ["BrightBlack"] = ColorFromHex("#767676"),
                        ["BrightRed"] = ColorFromHex("#E74856"),
                        ["BrightGreen"] = ColorFromHex("#16C60C"),
                        ["BrightYellow"] = ColorFromHex("#F9F1A5"),
                        ["BrightBlue"] = ColorFromHex("#3B78FF"),
                        ["BrightMagenta"] = ColorFromHex("#B4009E"),
                        ["BrightCyan"] = ColorFromHex("#61D6D6"),
                        ["BrightWhite"] = ColorFromHex("#F2F2F2")
                    }
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
