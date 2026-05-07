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
                TerminalCursor = "bar"
            };
        }
        private List<ColorConfiguration> GetDefaultColorConfigurations()
        {

            return new List<ColorConfiguration>()
            {
                new ColorConfiguration()
                {
                    Name = "Clear",
                    OutputColor = GetColorFromHexString("#50ff8c"),
                    OutputBlurColor = GetColorFromHexString("#00eeff"),
                    ControlColor = GetColorFromHexString("#d77b27"),
                    ControlBlurColor = GetColorFromHexString("#c14fff"),
                    BlurAmount = 5.0f,
                    WindowOpacityPct = 0,
                    WindowColor = GetColorFromHexString("#000000"),
                    ControlEngagedColor = Colors.Red,
                    ControlEngagedHoverColor = Colors.Red
                },
                new ColorConfiguration()
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
                new ColorConfiguration
                {
                    Name = "BluePunk",
                    OutputColor = GetColorFromHexString("#50ff8c"),
                    OutputBlurColor = GetColorFromHexString("#00eeff"),
                    ControlColor = Colors.Cyan,
                    ControlBlurColor = Colors.Magenta,
                    BlurAmount = 10.0f,
                    WindowOpacityPct = 8,
                    WindowColor = Colors.DarkSlateBlue,
                    ControlEngagedColor = Colors.Red,
                    ControlEngagedHoverColor = Colors.DarkRed
                },
                new ColorConfiguration()
                {
                    Name = "Neuromancer",
                    OutputColor = GetColorFromHexString("#50ff8c"), 
                    OutputBlurColor = GetColorFromHexString("#00eeff"), 
                    ControlColor = GetColorFromHexString("#6e8ffa"), 
                    ControlBlurColor = GetColorFromHexString("#c14fff"),
                    BlurAmount = 5.0f,
                    WindowOpacityPct = 30,
                    WindowColor = Colors.DarkTurquoise,
                    ControlEngagedColor = Colors.Red,
                    ControlEngagedHoverColor = Colors.Red
                },
                new ColorConfiguration
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
                new ColorConfiguration
                {
                    Name = "Mad Scientist",
                    OutputColor = GetColorFromHexString("#50ff8c"),
                    OutputBlurColor = GetColorFromHexString("#00eeff"),
                    ControlColor = GetColorFromHexString("#d77b27"),
                    ControlBlurColor = GetColorFromHexString("#c14fff"),
                    BlurAmount = 5.0f,
                    WindowOpacityPct = 20,
                    WindowColor = GetColorFromHexString("#b0ff19"),
                    ControlEngagedColor = Colors.Red,
                    ControlEngagedHoverColor = Colors.Red


                },
                new ColorConfiguration
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
                new ColorConfiguration
                {
                    Name = "Cut Paper",
                    OutputColor = Colors.DimGray,
                    OutputBlurColor = Colors.DarkGray,
                    ControlColor = Colors.DarkGray,
                    ControlBlurColor = Colors.LightGray,
                    BlurAmount = 4.0f,
                    WindowOpacityPct = 100,
                    WindowColor = Colors.LightGoldenrodYellow,
                    ControlEngagedColor = Colors.Red,
                    ControlEngagedHoverColor = Colors.Red
                }
            };
        }
    }
}
