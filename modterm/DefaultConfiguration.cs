using Microsoft.UI;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using Windows.UI;


namespace modterm
{
    public partial class ModtermWindow
    {
        public UserAppConfiguration GetDefaultAppConfiguration()
        {
            return new UserAppConfiguration()
            {
                LastWindowLocation = new Windows.Foundation.Point(100, 100),
                WindowSize = new Windows.Foundation.Size(800, 600),
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
                ThemeConfiguration = new ThemeConfiguration()
                {
                    Name = "Purpleser",
                    OutputColor = ModtermRender.GetColorFromHexString("#50ff8c"),
                    OutputBlurColor = ModtermRender.GetColorFromHexString("#00eeff"),
                    LabelColor = ModtermRender.GetColorFromHexString("#ff4fc1"),
                    LabelBlurColor = ModtermRender.GetColorFromHexString("#3333ff"),
                    BlurAmount = 12.0f,
                    WindowOpacityPct = 20,
                    WindowColor = ModtermRender.GetColorFromHexString("#000066"),
                    BackdropKind = BackdropKind.Blurred
                },
                ShellConfigurations = new List<Shell>()
                {
                    new Shell { Name = "cmd", Path = "C:\\Windows\\System32\\cmd.exe", Arguments = "" },
                    new Shell { Name = "powershell", Path = "C:\\Windows\\System32\\WindowsPowerShell\\v1.0\\powershell.exe", Arguments = "" },
                    new Shell { Name = "bash", Path = "C:\\Program Files\\Git\\bin\\bash.exe", Arguments = "" },
                    new Shell { Name = "wsl", Path = "wsl.exe", Arguments = "" },
                }
            };
        }
        private List<ThemeConfiguration> GetDefaultThemeConfigurations()
        {

            return new List<ThemeConfiguration>()
            {
                new ThemeConfiguration()
                {
                    Name = "Purpleser",
                    OutputColor = ModtermRender.GetColorFromHexString("#50ff8c"),
                    OutputBlurColor = ModtermRender.GetColorFromHexString("#00eeff"),
                    LabelColor = ModtermRender.GetColorFromHexString("#ff4fc1"),
                    LabelBlurColor = ModtermRender.GetColorFromHexString("#3333ff"),
                    BlurAmount = 7.0f,
                    WindowOpacityPct = 20,
                    WindowColor = ModtermRender.GetColorFromHexString("#000066"),
                    BackdropKind = BackdropKind.Blurred
                },
                new ThemeConfiguration()
                {
                    Name = "Matrix",
                    OutputColor = Color.FromArgb(255, 100, 255, 0), // <- nice green
                    OutputBlurColor = Color.FromArgb(255, 100, 255, 0),
                    LabelColor = Color.FromArgb(255, 100, 255, 0),
                    LabelBlurColor = Color.FromArgb(255, 100, 255, 0),
                    BlurAmount = 7.0f,
                    WindowOpacityPct = 80,
                    WindowColor = Colors.Black,
                    BackdropKind = BackdropKind.Mica
                },
                new ThemeConfiguration()
                {
                    Name = "BluePrism",
                    OutputColor = Color.FromArgb(255, 0, 0, 255),
                    OutputBlurColor = Colors.Cyan,
                    LabelColor = Color.FromArgb(255, 0, 0, 255),
                    LabelBlurColor = Colors.Cyan,
                    BlurAmount = 12.0f,
                    WindowOpacityPct = 20,
                    WindowColor = ModtermRender.GetColorFromHexString("#000066"),
                    BackdropKind = BackdropKind.Blurred
                },
                new ThemeConfiguration()
                {
                    Name = "BlueMatrice",
                    OutputColor = Color.FromArgb(255, 90, 255, 90),
                    OutputBlurColor = Color.FromArgb(255, 90, 45, 240),
                    LabelColor = Color.FromArgb(255, 100, 255, 0),
                    LabelBlurColor = Color.FromArgb(255, 90, 45, 240),
                    BlurAmount = 12.0f,
                    WindowOpacityPct = 40,
                    WindowColor = ModtermRender.GetColorFromHexString("#000000"),
                    BackdropKind = BackdropKind.Blurred
                },
                new ThemeConfiguration
                {
                    Name = "CyberEggplant",
                    OutputColor = ModtermRender.GetColorFromHexString("#50ff8c"),
                    OutputBlurColor = ModtermRender.GetColorFromHexString("#00eeff"),
                    LabelColor = Colors.Cyan,
                    LabelBlurColor = Colors.Magenta,
                    BlurAmount = 15.0f,
                    WindowOpacityPct = 60,
                    WindowColor = ModtermRender.GetColorFromHexString("#280654"),
                    BackdropKind = BackdropKind.Mica
                },
                new ThemeConfiguration()
                {
                    Name = "Blurromancer",
                    OutputColor = ModtermRender.GetColorFromHexString("#50ff8c"),
                    OutputBlurColor = ModtermRender.GetColorFromHexString("#00eeff"),
                    LabelColor = Colors.Cyan,
                    LabelBlurColor = Colors.Blue,
                    BlurAmount = 5.0f,
                    WindowOpacityPct = 30,
                    WindowColor = Colors.DarkBlue,
                    BackdropKind = BackdropKind.Acrylic
                },
                new ThemeConfiguration
                {
                    Name = "Cyberpunk",
                    OutputColor = ModtermRender.GetColorFromHexString("#50ff8c"),
                    OutputBlurColor = ModtermRender.GetColorFromHexString("#00eeff"),
                    LabelColor = Colors.Cyan,
                    LabelBlurColor = Colors.Magenta,
                    BlurAmount = 3.0f,
                    WindowOpacityPct = 20,
                    WindowColor = Color.FromArgb(255, 153, 0, 255),
                    BackdropKind = BackdropKind.Acrylic
                },
                new ThemeConfiguration
                {
                    Name = "Aqua",
                    OutputColor = ModtermRender.GetColorFromHexString("#50ff8c"),
                    OutputBlurColor = ModtermRender.GetColorFromHexString("#00eeff"),
                    LabelColor = Colors.Cyan,
                    LabelBlurColor = Colors.Magenta,
                    BlurAmount = 8.0f,
                    WindowOpacityPct = 10,
                    WindowColor = Colors.Aqua,
                    BackdropKind = BackdropKind.Blurred
                },
                new ThemeConfiguration
                {
                    Name = "Cut Paper",
                    OutputColor = Colors.DimGray,
                    OutputBlurColor = Colors.DarkGray,
                    LabelColor = Colors.DarkGray,
                    LabelBlurColor = Colors.AliceBlue,
                    BlurAmount = 2.0f,
                    WindowOpacityPct = 100,
                    WindowColor = Colors.LightGoldenrodYellow,
                    BackdropKind = BackdropKind.Blurred
                }
            };
        }
    }
}
