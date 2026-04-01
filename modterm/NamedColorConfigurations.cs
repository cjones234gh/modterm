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
    public static partial class ModtermDisplay
    {
        private static List<ColorConfiguration> CreateColorConfigurations()
        {

            return new List<ColorConfiguration>()
        {
            new ColorConfiguration()
            {
                Name = "Default",
                InputColor = Color.FromArgb(255, 0, 238, 255),
                InputGlowColor = Color.FromArgb(255, 0, 238, 255),
                OutputColor = Color.FromArgb(255, 80, 255, 140),
                OutputGlowColor = Color.FromArgb(255, 80, 255, 140),
                ControlColor = Color.FromArgb(255, 80, 255, 140),
                ControlGlowColor = Color.FromArgb(255, 80, 255, 140),
                BlurAmount = 5.0f,
                TransparencyPct = 10,
                TintColor = Color.FromArgb(255, 153, 0, 255),
                ControlEngagedColor = Colors.Red,
                ControlEngagedHoverColor = Colors.Red
            },
            new ColorConfiguration()
            {
                Name = "Matrix",
                InputColor = Colors.LimeGreen,
                InputGlowColor = Color.FromArgb(255, 100, 255, 0),
                OutputColor = Color.FromArgb(255, 100, 255, 0), // <- nice green
                OutputGlowColor = Color.FromArgb(255, 100, 255, 0),
                ControlColor = Color.FromArgb(255, 100, 255, 0),
                ControlGlowColor = Color.FromArgb(255, 100, 255, 0),
                BlurAmount = 7.0f,
                TransparencyPct = 40,
                TintColor = Colors.Black,
                ControlEngagedColor = Colors.Red,
                ControlEngagedHoverColor = Colors.Red
            },
            new ColorConfiguration()
            {
                Name = "Neuromancer",
                InputColor = Colors.HotPink,
                InputGlowColor = Colors.HotPink,
                OutputColor = Colors.MediumVioletRed,
                OutputGlowColor = Colors.Cyan,
                ControlColor = Colors.Cyan,
                ControlGlowColor= Colors.Blue,
                BlurAmount = 5.0f,
                TransparencyPct = 30,
                TintColor = Colors.DarkSlateGray,
                ControlEngagedColor = Colors.Red,
                ControlEngagedHoverColor = Colors.Red
            },
            new ColorConfiguration()
            {
                Name = "Glowmancer",
                InputColor = Colors.Blue,
                InputGlowColor = Colors.Yellow,
                OutputColor = Colors.Cyan,
                OutputGlowColor = Colors.Magenta,
                ControlColor = Colors.Cyan,
                ControlGlowColor = Colors.BlueViolet,
                BlurAmount = 7.0f,
                TransparencyPct = 15,
                TintColor = Color.FromArgb(255, 51, 255, 238),
                ControlEngagedColor = Colors.OrangeRed,
                ControlEngagedHoverColor = Colors.OrangeRed
            },
            new ColorConfiguration
            {
                Name = "Cyberpunk",
                InputColor = Color.FromArgb(255, 0, 238, 255),
                InputGlowColor = Color.FromArgb(255, 80, 255, 140),
                OutputColor = Color.FromArgb(255, 80, 255, 140),
                OutputGlowColor = Color.FromArgb(255, 0, 238, 255),
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
                Name = "Slate Blue, Dark",
                InputColor = Color.FromArgb(255, 0, 238, 255),
                InputGlowColor = Color.FromArgb(255, 80, 255, 140),
                OutputColor = Colors.Cyan,
                OutputGlowColor = Colors.Magenta,
                ControlColor = Color.FromArgb(255, 80, 255, 140),
                ControlGlowColor = Color.FromArgb(255, 0, 238, 255),
                BlurAmount = 5.0f,
                TransparencyPct = 30,
                TintColor = Colors.DarkSlateBlue,
                ControlEngagedColor = Colors.HotPink,
                ControlEngagedHoverColor = Colors.HotPink
            },
            new ColorConfiguration
            {
                Name = "Retro Smoked",
                InputColor = Colors.White,
                InputGlowColor = Colors.YellowGreen,
                OutputColor = Colors.LimeGreen,
                OutputGlowColor = Colors.Purple,
                ControlColor = Colors.White,
                ControlGlowColor= Colors.Blue,
                BlurAmount = 3f,
                TransparencyPct = 40,
                TintColor = Colors.Black,
                ControlEngagedColor = Colors.PaleVioletRed,
                ControlEngagedHoverColor = Colors.PaleVioletRed
            },
            new ColorConfiguration
            {
                Name = "Darth Vader",
                InputColor = Colors.LightGray,
                InputGlowColor = Colors.MediumVioletRed,
                OutputColor = Colors.Red,
                OutputGlowColor = Colors.Green,
                ControlColor = Colors.White,
                ControlGlowColor = Colors.Aquamarine,
                BlurAmount = 10f,
                TransparencyPct = 50,
                TintColor = Colors.Black,
                ControlEngagedColor = Colors.Yellow,
                ControlEngagedHoverColor = Colors.Yellow


            },
            new ColorConfiguration
            {
                Name = "Aqua",
                InputColor = Colors.Cyan,
                OutputColor = Colors.LightBlue,
                InputGlowColor = Colors.Cyan,
                OutputGlowColor = Colors.Magenta,
                ControlColor = Colors.Cyan,
                ControlGlowColor = Colors.Magenta,
                BlurAmount = 2.0f,
                TransparencyPct = 15,
                TintColor = Colors.Aqua,
                ControlEngagedColor = Colors.Red,
                ControlEngagedHoverColor = Colors.Red
            },
            new ColorConfiguration
            {
                Name = "Dark Mode",
                InputColor = Colors.GreenYellow,
                InputGlowColor = Colors.DeepSkyBlue,
                OutputColor = Colors.LimeGreen,
                OutputGlowColor = Colors.DeepSkyBlue,
                ControlColor = Colors.DeepSkyBlue,
                ControlGlowColor= Colors.LightGreen,
                BlurAmount = 10.0f,
                TransparencyPct = 50,
                TintColor = Colors.Black,
                ControlEngagedColor = Colors.Red,
                ControlEngagedHoverColor = Colors.Red
            },
            new ColorConfiguration
            {
                Name = "Cut Paper",
                InputColor = Colors.LightGray,
                InputGlowColor = Colors.DarkGray,
                OutputColor = Colors.DimGray,
                OutputGlowColor = Colors.DarkGray,
                ControlColor = Colors.DarkGray,
                ControlGlowColor = Colors.LightGray,
                BlurAmount = 4.0f,
                TransparencyPct = 100,
                TintColor = Colors.LightGoldenrodYellow,
                ControlEngagedColor = Colors.Red,
                ControlEngagedHoverColor = Colors.Red
            },
            new ColorConfiguration
            {
                Name = "Pro 1",
                InputColor = Color.FromArgb(255, 0, 102, 204), // 'dim azure' blue
                InputGlowColor = Colors.DarkGray,
                OutputColor = Colors.DimGray,
                OutputGlowColor = Colors.DarkGray,
                ControlColor = Colors.LimeGreen,
                ControlGlowColor = Colors.YellowGreen,
                BlurAmount = 4.0f,
                TransparencyPct = 100,
                TintColor = Colors.Black,
                ControlEngagedColor = Colors.Red,
                ControlEngagedHoverColor = Colors.Red
            },
            new ColorConfiguration
            {
                Name = "Pro 2",
                InputColor = Colors.Cyan,
                InputGlowColor = Colors.DarkGray,
                OutputColor = Colors.DarkOrange,
                OutputGlowColor = Colors.DarkOrange,
                ControlColor= Colors.DarkOrange,
                ControlGlowColor= Colors.DarkOrange,
                BlurAmount = 6.0f,
                TransparencyPct = 100,
                TintColor = Colors.Black,
                ControlEngagedColor = Colors.Red,
                ControlEngagedHoverColor = Colors.Red
            },
            new ColorConfiguration
            {
                Name = "Plain",
                InputColor = Colors.LightGreen,
                InputGlowColor = Colors.Cyan,
                OutputColor = Colors.LightGray,
                OutputGlowColor = Colors.White,
                ControlColor= Colors.Yellow,
                ControlGlowColor= Colors.Orange,
                BlurAmount = 0.0f,
                TransparencyPct = 100,
                TintColor = Colors.Black,
                ControlEngagedColor = Colors.Red,
                ControlEngagedHoverColor = Colors.Red
            },
            new ColorConfiguration
            {
                Name = "Nebula 1",
                InputColor = Colors.BlueViolet,
                InputGlowColor = Colors.Cyan,
                OutputColor = Colors.LightGray,
                OutputGlowColor = Colors.DarkGray,
                ControlColor= Colors.Purple,
                ControlGlowColor= Colors.Orange,
                BlurAmount = 5.0f,
                TransparencyPct = 100,
                TintColor = Colors.Red,
                ControlEngagedColor = Colors.Red,
                ControlEngagedHoverColor = Colors.Red
            }
        };
        }
    }
}
