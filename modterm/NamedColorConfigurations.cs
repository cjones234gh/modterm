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
        private List<ColorConfiguration> CreateColorConfigurations()
        {

            return new List<ColorConfiguration>()
        {
            new ColorConfiguration()
            {
                Name = "Default",
                OutputColor = GetColorFromHexString("#50ff8c"),
                OutputGlowColor = GetColorFromHexString("#00eeff"),
                ControlColor = GetColorFromHexString("#d77b27"),
                ControlGlowColor = GetColorFromHexString("#c14fff"),
                BlurAmount = 5.0f,
                TransparencyPct = 10,
                TintColor = GetColorFromHexString("#37096d"),
                ControlEngagedColor = Colors.Red,
                ControlEngagedHoverColor = Colors.Red
            },
            new ColorConfiguration()
            {
                Name = "Matrix",
                OutputColor = Color.FromArgb(255, 100, 255, 0), // <- nice green
                OutputGlowColor = Color.FromArgb(255, 100, 255, 0),
                ControlColor = Color.FromArgb(255, 100, 255, 0),
                ControlGlowColor = Color.FromArgb(255, 100, 255, 0),
                BlurAmount = 7.0f,
                TransparencyPct = 80,
                TintColor = Colors.Black,
                ControlEngagedColor = Colors.Red,
                ControlEngagedHoverColor = Colors.Red
            },
            new ColorConfiguration()
            {
                Name = "Neuromancer",
                OutputColor = GetColorFromHexString("#50ff8c"), 
                OutputGlowColor = GetColorFromHexString("#00eeff"), 
                ControlColor = GetColorFromHexString("#6e8ffa"), 
                ControlGlowColor = GetColorFromHexString("#c14fff"),
                BlurAmount = 5.0f,
                TransparencyPct = 30,
                TintColor = Colors.DarkTurquoise,
                ControlEngagedColor = Colors.Red,
                ControlEngagedHoverColor = Colors.Red
            },
            new ColorConfiguration
            {
                Name = "Cyberpunk",
                OutputColor = GetColorFromHexString("#50ff8c"),
                OutputGlowColor = GetColorFromHexString("#00eeff"),
                ControlColor = Colors.Cyan,
                ControlGlowColor = Colors.Magenta,
                BlurAmount = 3.0f,
                TransparencyPct = 20,
                TintColor = Color.FromArgb(255, 153, 0, 255),
                ControlEngagedColor = Colors.Red,
                ControlEngagedHoverColor = Colors.DarkRed
            },
            new ColorConfiguration
            {
                Name = "Mad Scientist",
                OutputColor = GetColorFromHexString("#50ff8c"),
                OutputGlowColor = GetColorFromHexString("#00eeff"),
                ControlColor = GetColorFromHexString("#d77b27"),
                ControlGlowColor = GetColorFromHexString("#c14fff"),
                BlurAmount = 5.0f,
                TransparencyPct = 20,
                TintColor = GetColorFromHexString("#b0ff19"),
                ControlEngagedColor = Colors.Red,
                ControlEngagedHoverColor = Colors.Red


            },
            new ColorConfiguration
            {
                Name = "Aqua",
                OutputColor = GetColorFromHexString("#50ff8c"),
                OutputGlowColor = GetColorFromHexString("#00eeff"),
                ControlColor = Colors.Cyan,
                ControlGlowColor = Colors.Magenta,
                BlurAmount = 8.0f,
                TransparencyPct = 10,
                TintColor = Colors.Aqua,
                ControlEngagedColor = Colors.Red,
                ControlEngagedHoverColor = Colors.Red
            },
            new ColorConfiguration
            {
                Name = "Cut Paper",
                OutputColor = Colors.DimGray,
                OutputGlowColor = Colors.DarkGray,
                ControlColor = Colors.DarkGray,
                ControlGlowColor = Colors.LightGray,
                BlurAmount = 4.0f,
                TransparencyPct = 100,
                TintColor = Colors.LightGoldenrodYellow,
                ControlEngagedColor = Colors.Red,
                ControlEngagedHoverColor = Colors.Red
            }
        };
        }
    }
}
