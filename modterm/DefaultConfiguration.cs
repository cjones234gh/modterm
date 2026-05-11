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
    public partial class ModtermDisplay
    {
        public UserAppConfiguration GetDefaultAppConfiguration()
        {
            string conargs = "--headless --width [W] --height [H] -- ";
            return new UserAppConfiguration()
            {
                WindowLocation = new Windows.Foundation.Rect(100, 100, 800, 600),
                TerminalFont = "Consolas",
                TerminalControlFont = "Lucida Console",
                TerminalFontSize = 12.0f,
                TerminalShell = new Shell 
                { 
                    Name = "powershell", 
                    Path = "conhost", 
                    Arguments = conargs + "C:\\Windows\\System32\\WindowsPowerShell\\v1.0\\powershell.exe" 
                },
                TerminalCursor = "bar",
                ThemeConfiguration = new ThemeConfiguration()
                {
                    Name = "Purpleser",
                    OutputColor = GetColorFromHexString("#50ff8c"),
                    OutputBlurColor = GetColorFromHexString("#00eeff"),
                    ControlColor = GetColorFromHexString("#ff4fc1"),
                    ControlBlurColor = GetColorFromHexString("#3333ff"),
                    BlurAmount = 7.0f,
                    WindowOpacityPct = 20,
                    WindowColor = GetColorFromHexString("#000066"),
                    ControlEngagedColor = Colors.Red,
                    ControlEngagedHoverColor = Colors.Red,
                    BackdropKind = BackdropKind.Blurred
                }
            };
        }
        private List<ThemeConfiguration> GetDefaultColorConfigurations()
        {

            return new List<ThemeConfiguration>()
            {
                new ThemeConfiguration()
                {
                    Name = "Purpleser",
                    OutputColor = GetColorFromHexString("#50ff8c"),
                    OutputBlurColor = GetColorFromHexString("#00eeff"),
                    ControlColor = GetColorFromHexString("#ff4fc1"),
                    ControlBlurColor = GetColorFromHexString("#3333ff"),
                    BlurAmount = 7.0f,
                    WindowOpacityPct = 20,
                    WindowColor = GetColorFromHexString("#000066"),
                    ControlEngagedColor = Colors.Red,
                    ControlEngagedHoverColor = Colors.Red
                },
                new ThemeConfiguration()
                {
                    Name = "Matrix",
                    OutputColor = Color.FromArgb(255, 100, 255, 0), // <- nice green
                    OutputBlurColor = Color.FromArgb(255, 100, 255, 0),
                    ControlColor = Color.FromArgb(255, 100, 255, 0),
                    ControlBlurColor = Color.FromArgb(255, 100, 255, 0),
                    BlurAmount = 7.0f,
                    WindowOpacityPct = 80,
                    WindowColor = Colors.Black,
                    ControlEngagedColor = Colors.Red,
                    ControlEngagedHoverColor = Colors.Red
                },
                new ThemeConfiguration
                {
                    Name = "CyberEggplant",
                    OutputColor = GetColorFromHexString("#50ff8c"),
                    OutputBlurColor = GetColorFromHexString("#00eeff"),
                    ControlColor = Colors.Cyan,
                    ControlBlurColor = Colors.Magenta,
                    BlurAmount = 15.0f,
                    WindowOpacityPct = 60,
                    WindowColor = GetColorFromHexString("#280654"),
                    ControlEngagedColor = Colors.Red,
                    ControlEngagedHoverColor = Colors.DarkRed,
                    BackdropKind = BackdropKind.Mica
                },
                new ThemeConfiguration()
                {
                    Name = "Blurromancer",
                    OutputColor = GetColorFromHexString("#50ff8c"),
                    OutputBlurColor = GetColorFromHexString("#00eeff"),
                    ControlColor = Colors.Cyan,
                    ControlBlurColor = Colors.Blue,
                    BlurAmount = 5.0f,
                    WindowOpacityPct = 30,
                    WindowColor = Colors.DarkBlue,
                    ControlEngagedColor = Colors.Orange,
                    ControlEngagedHoverColor = Colors.Orange,
                    BackdropKind = BackdropKind.Acrylic
                },
                new ThemeConfiguration
                {
                    Name = "Cyberpunk",
                    OutputColor = GetColorFromHexString("#50ff8c"),
                    OutputBlurColor = GetColorFromHexString("#00eeff"),
                    ControlColor = Colors.Cyan,
                    ControlBlurColor = Colors.Magenta,
                    BlurAmount = 3.0f,
                    WindowOpacityPct = 20,
                    WindowColor = Color.FromArgb(255, 153, 0, 255),
                    ControlEngagedColor = Colors.Red,
                    ControlEngagedHoverColor = Colors.DarkRed
                },
                new ThemeConfiguration
                {
                    Name = "Aqua",
                    OutputColor = GetColorFromHexString("#50ff8c"),
                    OutputBlurColor = GetColorFromHexString("#00eeff"),
                    ControlColor = Colors.Cyan,
                    ControlBlurColor = Colors.Magenta,
                    BlurAmount = 8.0f,
                    WindowOpacityPct = 10,
                    WindowColor = Colors.Aqua,
                    ControlEngagedColor = Colors.Red,
                    ControlEngagedHoverColor = Colors.Red
                },
                new ThemeConfiguration
                {
                    Name = "Cut Paper",
                    OutputColor = Colors.DimGray,
                    OutputBlurColor = Colors.DarkGray,
                    ControlColor = Colors.DarkGray,
                    ControlBlurColor = Colors.AliceBlue,
                    BlurAmount = 2.0f,
                    WindowOpacityPct = 100,
                    WindowColor = Colors.LightGoldenrodYellow,
                    ControlEngagedColor = Colors.Navy,
                    ControlEngagedHoverColor = Colors.Navy
                }
            };
        }
    }
}
