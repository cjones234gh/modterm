using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;


using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Windows.Devices.Midi;
using Windows.Foundation;
using Windows.UI;

namespace modterm
{
    public enum Effects
    {
        None,
        Glow,
    }

    public static partial class ModtermDisplay
    {
        public static FontFamily CurrentFont = new FontFamily("SimSun-ExtB");
        public static FontFamily CurrentControlFont = new FontFamily("Consolas");
        public static Color InputColor;
        public static Color InputGlowColor;
        public static Color OutputGlowColor;
        public static Color OutputColor;
        public static Color ControlColor;
        public static Color ControlGlowColor;
        public static Color ControlEngagedColor;
        public static Color ControlEngagedHoverColor;

        public static float CornerRadius;
        public static float LineWidth;
        public static byte BlurFillTransparency;
        public static byte SharpBorderTransparency;
        public static byte SharpFillTransparency;

        public static float BlurAmount;

        // theme
        public static string CurrentConfigurationName;

        // space between content and control borders
        public static float ControlPadding;

        // space between controls and canvas edges/other controls
        public static float ControlMargin;

        // control scale based on font size to maintain consistent proportions
        public static float CurrentFontSizeControlScale;

        private static float _currentFontSize;
        private static float _currentControlFontSize;
        private static int _transparencyPct;
        private static byte _alpha;
        private static Color _tintColor;
        private static SolidColorBrush _backgroundBrush = new SolidColorBrush(Colors.Red);
        private static List<ColorConfiguration> _namedColorConfigurations = new List<ColorConfiguration>();
        private static int _namedColorConfigIndex = 0;

        private static bool _effectSequenceStarted = false;
        private static List<DrawTextCall> _effectSequence = new List<DrawTextCall>();
        private static CanvasControl _sender;
        private static CanvasDrawingSession _drawSession;
        private static Effects _effect = Effects.None;

        public static float CurrentFontSize
        {
            get
            {
                return _currentFontSize;
            }
            set
            {
                _currentFontSize = value;
            }
        }

        public static float CurrentControlFontSize
        {
            get
            {
                return _currentControlFontSize;
            }
            set
            {
                _currentControlFontSize = value;
                ControlPadding = _currentControlFontSize/1.5f;
                ControlMargin = 5;// + (_currentFontSize / 2);
            }
        }

        public static int TransparencyPct
        {
            get
            {
                return _transparencyPct;
            }
            set
            {
                _transparencyPct = value;
                _alpha = (byte)(_transparencyPct * 2.55);
                _backgroundBrush.Color = GetBackgroundArgb();
            }
        }

        public static Color TintColor
        {
            get
            {
                return _tintColor;
            }
            set
            {
                _tintColor = value;
                _backgroundBrush.Color = GetBackgroundArgb();
            }
        }

        public static void Initialize()
        {
            // set default values
            CurrentFontSize = 12f;
            CurrentControlFontSize = 11f;


            _namedColorConfigurations = ModtermDisplay.CreateColorConfigurations();
            _namedColorConfigIndex = 0;

            // internal control defaults
            CornerRadius = 2f;
            BlurFillTransparency = 50;
            SharpBorderTransparency = 150;
            SharpFillTransparency = 20;
            LineWidth = 0.5f;

            SetColorConfiguration("Default");

        }

        public static List<string> GetConfigurationNames()
        {
            List<string> names = new List<string>();
            foreach (var config in _namedColorConfigurations)
            {
                names.Add(config.Name);
            }
            return names;
        }

        public static Color GetControlColor(IModtermControl control)
        {
            if (control.IsPressed)
                return Color.FromArgb(180, ControlColor.R, ControlColor.G, ControlColor.B);
            else if (control.IsEngaged)
                return control.IsHovered ? ControlEngagedHoverColor : ControlEngagedColor;
            else if (control.IsHovered)
                return ControlColor;
            else
                return ControlColor;
        }

        public static Color GetControlGlowColor(IModtermControl control)
        {
            if (control.IsPressed)
                // return output glow
                return ControlGlowColor;
            else if (control.IsEngaged)
                return control.IsHovered ? ControlEngagedHoverColor : ControlEngagedColor;
            else if (control.IsHovered)
                // return output glow at hover
                return ControlGlowColor;
            else
                // return output glow when not engaged or hovered
                return ControlGlowColor;
        }

        private static Color GetBackgroundArgb()
        {
            return TintColor == Colors.Transparent
                ? Colors.Transparent
                : Color.FromArgb(_alpha, TintColor.R, TintColor.G, TintColor.B);
        }

        public static void SetColorConfiguration(ColorConfiguration config)
        {
            OutputColor = config.OutputColor;
            OutputGlowColor = config.OutputGlowColor;
            ControlColor = config.ControlColor;
            ControlGlowColor = config.ControlGlowColor;
            BlurAmount = config.BlurAmount;
            TransparencyPct = config.TransparencyPct;
            TintColor = config.TintColor;
            ControlEngagedColor = config.ControlEngagedColor;
            ControlEngagedHoverColor = config.ControlEngagedHoverColor;
            _backgroundBrush.Color = GetBackgroundArgb();
        }

        public static SolidColorBrush GetBackgroundBrush()
        {
            return _backgroundBrush;
        }

        public static void BeginEffectSequence(CanvasControl sender, CanvasDrawingSession ds, Effects effect)
        {
            _sender = sender;
            _drawSession = ds;
            _effect = effect;
            if (_effectSequenceStarted)
            {
                throw new InvalidOperationException("Effect sequence already started.");
            }
            else
            {
                _effectSequenceStarted = true;
                _effectSequence.Clear();
            }
        }

        public static void EndEffectSequence()
        {
            if (_effectSequenceStarted)
            {
                DrawEffectSequence();
                _effectSequenceStarted = false;
                _effectSequence.Clear();
            }
            else
            {
                throw new InvalidOperationException("Effect sequence was not started.");
            }
        }

        public static void SetColorConfiguration(string configurationName)
        {
            var config = _namedColorConfigurations.Find(c => c.Name == configurationName);
            CurrentConfigurationName = config.Name;
            if (config != null)
            {
                SetColorConfiguration(config);
            }
        }

        public static void DrawText(string text, float x, float y, Color color, CanvasTextFormat textFormat)
        {
            _effectSequence.Add(new DrawTextCall(text, x, y, color, textFormat));
        }

        public static void DrawControlBox(CanvasControl sender, CanvasDrawingSession cds, Rect location)
        {
            var controlColor = OutputColor;
            var controlBlurColor = OutputGlowColor;
            // blur layer
            using (var commandList = new CanvasCommandList(sender))
            {
                using (var clds = commandList.CreateDrawingSession())
                {
                    // Draw a bordered rectangle
                    clds.DrawRoundedRectangle(
                        location, ModtermDisplay.CornerRadius, ModtermDisplay.CornerRadius, controlBlurColor, ModtermDisplay.LineWidth);

                    // Draw background rectangle only in hover state
                    // (see below, do we want to bother with states for a box?)
                }

                var blurEffect = new GaussianBlurEffect { Source = commandList, BlurAmount = ModtermDisplay.BlurAmount };
                cds.DrawImage(blurEffect);
            }

            // Draw a bordered rectangle
            cds.DrawRoundedRectangle(location, ModtermDisplay.CornerRadius, ModtermDisplay.CornerRadius,
                Color.FromArgb(ModtermDisplay.SharpBorderTransparency, controlColor.R, controlColor.G, controlColor.B), ModtermDisplay.LineWidth);

            // Draw background rectangle
            cds.FillRoundedRectangle(location, ModtermDisplay.CornerRadius, ModtermDisplay.CornerRadius,
                Color.FromArgb(ModtermDisplay.SharpFillTransparency, controlColor.R, controlColor.G, controlColor.B));
        }

        public static void DrawTextDisplayControl(CanvasControl sender, CanvasDrawingSession cds, TextDisplayControl control)
        {
            Color controlColor = GetControlColor(control);
            Color controlBlurColor = GetControlGlowColor(control);

            CanvasTextFormat textFormat = ModtermDisplay.GetControlTextFormat();

            //
            //
            // blur layer
            using (var commandList = new CanvasCommandList(sender))
            {
                using (var clds = commandList.CreateDrawingSession())
                {
                    // Draw a bordered rectangle
                    //clds.DrawRoundedRectangle(
                    //    control.Location, ModtermDisplay.CornerRadius, ModtermDisplay.CornerRadius, controlBlurColor, ModtermDisplay.LineWidth);

                    //// Draw background rectangle except when in hover state
                    //if (!control.IsHovered)
                    //    clds.FillRoundedRectangle(control.Location, ModtermDisplay.CornerRadius, ModtermDisplay.CornerRadius,
                    //        Color.FromArgb(ModtermDisplay.BlurFillTransparency, controlBlurColor.R, controlBlurColor.G, controlBlurColor.B));

                    // Draw TextContent
                    clds.DrawText(control.TextContent, (float)control.Location.X + ModtermDisplay.ControlPadding,
                        (float)control.Location.Y + ModtermDisplay.ControlPadding / 2, controlBlurColor, textFormat);
                }

                var blurEffect = new GaussianBlurEffect { Source = commandList, BlurAmount = ModtermDisplay.BlurAmount };
                cds.DrawImage(blurEffect);
            }

            //
            //
            // sharp layer

            // Draw a bordered rectangle
            //cds.DrawRoundedRectangle(control.Location, ModtermDisplay.CornerRadius, ModtermDisplay.CornerRadius,
            //    Color.FromArgb(ModtermDisplay.SharpBorderTransparency, controlColor.R, controlColor.G, controlColor.B), ModtermDisplay.LineWidth);

            // Draw background rectangle
            cds.FillRoundedRectangle(control.Location, ModtermDisplay.CornerRadius, ModtermDisplay.CornerRadius,
                Color.FromArgb(ModtermDisplay.SharpFillTransparency, controlColor.R, controlColor.G, controlColor.B));

            // Draw TextContent
            cds.DrawText(control.TextContent, (float)control.Location.X + ModtermDisplay.ControlPadding,
                (float)control.Location.Y + ModtermDisplay.ControlPadding / 2, controlColor, textFormat);
        }

        public static List<Color> GetColorWheelProgression(double StepDegrees, double Saturation, int NumColors)
        {
            List<Color> colors = new List<Color>();
            var rand = new Random();
            // Pick a random starting hue (0-360)
            double startHue = rand.NextDouble() * 360.0;
            double lightness = 0.55; // 55% lightness for good visibility

            for (int i = 0; i < NumColors; i++)
            {
                double hue = (startHue + i * StepDegrees) % 360.0;
                colors.Add(HslToColor(hue, Saturation, lightness));
            }
            return colors;
        }

        public static Color HslToColor(double h, double s, double l)
        {
            h = h / 360.0;
            double r = l, g = l, b = l;
            if (s != 0)
            {
                double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
                double p = 2 * l - q;
                r = HueToRgb(p, q, h + 1.0 / 3.0);
                g = HueToRgb(p, q, h);
                b = HueToRgb(p, q, h - 1.0 / 3.0);
            }
            return Color.FromArgb(255, (byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
        }

        public static double HueToRgb(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1.0 / 6.0) return p + (q - p) * 6 * t;
            if (t < 1.0 / 2.0) return q;
            if (t < 2.0 / 3.0) return p + (q - p) * (2.0 / 3.0 - t) * 6;
            return p;
        }

        private static void DrawEffectSequence()
        {
            // Blurred glow layer
            using (var commandList = new CanvasCommandList(_sender))
            {
                using (var clds = commandList.CreateDrawingSession())
                {
                    foreach (DrawTextCall call in _effectSequence)
                    {
                        if (call.Color == OutputColor)
                        {
                            clds.DrawText(call.Text, call.X, call.Y, OutputGlowColor, call.TextFormat);
                        }
                        else
                        {
                            clds.DrawText(call.Text, call.X, call.Y, call.Color, call.TextFormat);
                        }
                    }
                }
                var blurEffect = new GaussianBlurEffect { Source = commandList, BlurAmount = ModtermDisplay.BlurAmount };
                _drawSession.DrawImage(blurEffect);
            }

            // Sharp layer
            foreach (DrawTextCall call in _effectSequence)
            {
                _drawSession.DrawText(call.Text, call.X, call.Y, call.Color, call.TextFormat);
            }
        }

        public static void CycleColorConfiguration()
        {
            _namedColorConfigIndex = (_namedColorConfigIndex + 1) % _namedColorConfigurations.Count;
            SetColorConfiguration(_namedColorConfigurations[_namedColorConfigIndex]);
        }

        public static CanvasTextFormat GetControlTextFormat()
        {
            return new CanvasTextFormat
            {
                FontFamily = ModtermDisplay.CurrentControlFont.Source,
                FontSize = ModtermDisplay.CurrentControlFontSize
            };
        }

        public static CanvasTextFormat GetTextFormat()
        {
            return new CanvasTextFormat
            {
                FontFamily = ModtermDisplay.CurrentFont.Source,
                FontSize = ModtermDisplay.CurrentFontSize
            };
        }

        public static string GetHexStringFromColor(Color color)
        {
            return $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
        }

        public static Color GetColorFromHexString(string hex)
        {
            if (hex.StartsWith("#")) hex = hex.Substring(1);
            byte a = 255, r = 0, g = 0, b = 0;
            if (hex.Length == 8)
            {
                a = Convert.ToByte(hex.Substring(0, 2), 16);
                r = Convert.ToByte(hex.Substring(2, 2), 16);
                g = Convert.ToByte(hex.Substring(4, 2), 16);
                b = Convert.ToByte(hex.Substring(6, 2), 16);
            }
            else if (hex.Length == 6)
            {
                r = Convert.ToByte(hex.Substring(0, 2), 16);
                g = Convert.ToByte(hex.Substring(2, 2), 16);
                b = Convert.ToByte(hex.Substring(4, 2), 16);
            }
            return Color.FromArgb(a, r, g, b);
        }

        public static string GetAppearanceInfo(int lines, int columns)
        {
            string info =
                $"\"{ModtermDisplay.CurrentConfigurationName}\" Tint: {ModtermDisplay.GetHexStringFromColor(ModtermDisplay.GetBackgroundBrush().Color)} " +
                $" Lines: {lines} Cols: {columns}";
            return info.Replace(" ", "\u00A0"); // replace spaces with non-breaking spaces to prevent collapsing in the UI
        }
    }
}

