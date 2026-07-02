using System.Collections.Generic;
using Windows.Foundation;

namespace modterm
{
    public partial class ModtermWindow
    {
        private const string DefaultThemeName = "Bluefang";

        public UserAppConfiguration GetDefaultAppConfiguration()
        {
            return new UserAppConfiguration()
            {
                LastWindowLocation = new Point(100, 100),
                WindowSize = new Size(800, 600),
                TerminalFont = "BlexMono Nerd Font Mono",
                LabelFont = "BlexMono Nerd Font Mono",
                TerminalFontSize = 12.0f,
                TerminalShell = new Shell
                {
                    Name = "powershell",
                    Path = "C:\\Windows\\System32\\WindowsPowerShell\\v1.0\\powershell.exe",
                    Arguments = ""
                },
                TerminalCursor = "bar",
                ThemeConfiguration = LoadThemeConfiguration(DefaultThemeName),
                ShellConfigurations = new List<Shell>()
                {
                    new Shell { Name = "cmd", Path = "C:\\Windows\\System32\\cmd.exe", Arguments = "" },
                    new Shell { Name = "powershell", Path = "C:\\Windows\\System32\\WindowsPowerShell\\v1.0\\powershell.exe", Arguments = "" },
                    new Shell { Name = "bash", Path = "C:\\Program Files\\Git\\bin\\bash.exe", Arguments = "" },
                    new Shell { Name = "wsl", Path = "wsl.exe", Arguments = "" },
                }
            };
        }
    }
}
