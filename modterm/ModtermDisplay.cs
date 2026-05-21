using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Windows.Foundation;
using Windows.UI;
using WinRT.modtermVtableClasses;

namespace modterm
{
    public enum Effects
    {
        None,
        Glow,
    }

    public partial class ModtermDisplay
    {
        // font family names for terminal and control text (Win2D CanvasTextFormat)
        public string CurrentFont { get; set; } = string.Empty;
        public string CurrentControlFont { get; set; } = string.Empty;
        // glow colors for terminal and control text
        public Color OutputGlowColor { get; set; }
        // normal colors for terminal and control text
        public Color OutputColor { get; set; }
        // control colors (normal, engaged, hovered, etc.)
        public Color ControlColor { get; set; }
        public Color ControlGlowColor { get; set; }
        public Color ControlEngagedColor { get; set; }
        public Color ControlEngagedHoverColor { get; set; }
        // control rendering details (rounded corners, line width, etc.)
        public float CornerRadius { get; set; }
        public float LineWidth { get; set; }
        public byte BlurFillTransparency { get; set; }
        public byte SharpBorderTransparency { get; set; }
        public byte SharpFillTransparency { get; set; }
        // blur amount for text and control boxes
        public float BlurAmount { get; set; }
        // theme
        public string CurrentConfigurationName { get; set; } = string.Empty;
        // system backdrop info
        public string SystemBackdropInfo { get; private set; } = string.Empty;
        // space between content and control borders
        public float ControlPadding { get; set; }
        // space between controls and canvas edges/other controls
        public float ControlMargin { get; set; }
        public float ControlMarginRight { get; set; }
        // control scale based on font size to maintain consistent proportions
        public float CurrentFontSizeControlScale { get; set; }

        private float _currentFontSize;
        private float _currentControlFontSize;
        private float _controlFontScale = 0.8f;
        private float _currentBgColorPadding; 
        private int _transparencyPct;
        private byte _alpha;
        private Color _tintColor;
        private SolidColorBrush _backgroundBrush = new SolidColorBrush(Colors.Red);
        private List<ThemeConfiguration> _namedColorConfigurations = new List<ThemeConfiguration>();
        private int _themeConfigIndex = 0;
        private bool _effectSequenceStarted = false;
        private List<DrawTextCall> _effectSequence = new List<DrawTextCall>();
        private CanvasControl _sender = null!;
        private CanvasDrawingSession _drawSession = null!;
        private Effects _effect = Effects.None;

        public float CurrentFontSize
        {
            get
            {
                return _currentFontSize;
            }
            set
            {
                _currentFontSize = value;
                _currentControlFontSize = CurrentFontSize * _controlFontScale;
                ControlPadding = _currentControlFontSize / 1.5f;
                ControlMargin = 5;// + (_currentFontSize / 2);
                ControlMarginRight = 10;
                _currentBgColorPadding = _currentFontSize / 1.25f; // adjust background rectangle padding based on font size for better fit
            }
        }

        public int OpacityPct
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

        public Color TintColor
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

        public void Initialize()
        {
            // set default values
            CurrentFontSize = 12f;
            CurrentFont = "Consolas";
            CurrentControlFont = "Lucida Console";
            _namedColorConfigurations = GetDefaultColorConfigurations();
            _themeConfigIndex = 0;

            // internal control defaults
            CornerRadius = 2f;
            BlurFillTransparency = 150;
            SharpBorderTransparency = 150;
            SharpFillTransparency = 40;
            LineWidth = 0.5f;
        }

        public void ApplySystemBackdrop(BackdropKind kind, Window wInstance)
        {
            SystemBackdrop backdrop = kind switch
            {
                BackdropKind.Blurred => new BlurredBackdrop(),
                BackdropKind.Mica => new MicaBackdrop(),
                BackdropKind.Acrylic => new DesktopAcrylicBackdrop(),
                _ => new BlurredBackdrop()
            };
            wInstance.SystemBackdrop = backdrop;
            SystemBackdropInfo = $"{kind}";
        }

        public List<ThemeConfiguration> GetAllColorConfigurations()
        {
            return _namedColorConfigurations;
        }

        public List<string> GetConfigurationNames()
        {
            List<string> names = new List<string>();
            foreach (var config in _namedColorConfigurations)
            {
                names.Add(config.Name);
            }
            return names;
        }

        public Color GetControlColor(ModtermControl control)
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

        public Color GetControlGlowColor(ModtermControl control)
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

        private Color GetBackgroundArgb()
        {
            return TintColor == Colors.Transparent
                ? Colors.Transparent
                : Color.FromArgb(_alpha, TintColor.R, TintColor.G, TintColor.B);
        }

        public void SetColorConfiguration(ThemeConfiguration config, Window wInstance)
        {
            OutputColor = config.OutputColor;
            OutputGlowColor = config.OutputBlurColor;
            ControlColor = config.ControlColor;
            ControlGlowColor = config.ControlBlurColor;
            BlurAmount = config.BlurAmount;
            OpacityPct = config.WindowOpacityPct;
            TintColor = config.WindowColor;
            ControlEngagedColor = config.ControlEngagedColor;
            ControlEngagedHoverColor = config.ControlEngagedHoverColor;
            ApplySystemBackdrop(config.BackdropKind, wInstance);
            _backgroundBrush.Color = GetBackgroundArgb();
        }

        public SolidColorBrush GetBackgroundBrush()
        {
            return _backgroundBrush;
        }

        public void BeginEffectSequence(CanvasControl sender, CanvasDrawingSession ds, Effects effect)
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

        public void EndEffectSequence()
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

        public void SetColorConfiguration(string configurationName, Window wInstance)
        {
            var config = _namedColorConfigurations.Find(c => c.Name == configurationName);
            if (config != null)
            {
                CurrentConfigurationName = config.Name;
                SetColorConfiguration(config, wInstance);
            }
        }

        public void DrawText(string text, float x, float y, float width, Color color, Color bgColor, CanvasTextFormat textFormat, bool foregroundIsDefault = false, bool backgroundIsDefault = false)
        {
            _effectSequence.Add(new DrawTextCall(text, x, y, width, color, bgColor, textFormat, foregroundIsDefault, backgroundIsDefault));
        }

        public void DrawControlBox(CanvasControl sender, CanvasDrawingSession cds, Rect location)
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
                        location, CornerRadius, CornerRadius, controlBlurColor, LineWidth);

                    // Draw background rectangle only in hover state
                    // (see below, do we want to bother with states for a box?)
                }

                var blurEffect = new GaussianBlurEffect { Source = commandList, BlurAmount = BlurAmount };
                cds.DrawImage(blurEffect);
            }

            // Draw a bordered rectangle
            cds.DrawRoundedRectangle(location, CornerRadius, CornerRadius,
                Color.FromArgb(SharpBorderTransparency, controlColor.R, controlColor.G, controlColor.B), LineWidth);

            // Draw background rectangle
            cds.FillRoundedRectangle(location, CornerRadius, CornerRadius,
                Color.FromArgb(SharpFillTransparency, controlColor.R, controlColor.G, controlColor.B));
        }

        public void DrawTextDisplayControl(CanvasControl sender, CanvasDrawingSession cds, TextDisplayControl control)
        {
            Color controlColor = GetControlColor(control);
            Color controlBlurColor = GetControlGlowColor(control);

            CanvasTextFormat textFormat = GetControlTextFormat();

            //
            //
            // blur layer
            using (var commandList = new CanvasCommandList(sender))
            {
                using (var clds = commandList.CreateDrawingSession())
                {
                    //if (control.Interactive)
                    //{
                        // Draw a bordered rectangle
                        //clds.DrawRoundedRectangle(
                        //    control.Location, CornerRadius, CornerRadius, controlBlurColor, LineWidth);

                        // Draw background rectangle except when in hover state
                        if (!control.IsHovered || !control.Interactive)
                            clds.FillRoundedRectangle(control.Location, CornerRadius, CornerRadius,
                                Color.FromArgb(BlurFillTransparency, controlBlurColor.R, controlBlurColor.G, controlBlurColor.B));
                    //}
                    
                    // Draw TextContent
                    clds.DrawText(control.TextContent, (float)control.Location.X + ControlPadding,
                        (float)control.Location.Y + ControlPadding / 2, controlBlurColor, textFormat);
                }

                var blurEffect = new GaussianBlurEffect { Source = commandList, BlurAmount = BlurAmount };
                cds.DrawImage(blurEffect);
            }

            //
            //
            // sharp layer
            if (control.Interactive)
            {
                // Draw a bordered rectangle
                //cds.DrawRoundedRectangle(control.Location, CornerRadius, CornerRadius,
                //    Color.FromArgb(SharpBorderTransparency, controlColor.R, controlColor.G, controlColor.B), LineWidth);
            }

            // Draw background rectangle
            cds.FillRoundedRectangle(control.Location, CornerRadius, CornerRadius,
                Color.FromArgb(SharpFillTransparency, controlColor.R, controlColor.G, controlColor.B));

            // Draw TextContent
            cds.DrawText(control.TextContent, (float)control.Location.X + ControlPadding,
                (float)control.Location.Y + ControlPadding / 2, controlColor, textFormat);

            if (control.IsEngaged && control.Children is { Count: > 0 })
            {
                foreach (var child in control.Children)
                    child.Draw(sender, cds, this);
            }
        }

        public List<Color> GetColorWheelProgression(double StepDegrees, double Saturation, int NumColors)
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

        public Color HslToColor(double h, double s, double l)
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

        public double HueToRgb(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1.0 / 6.0) return p + (q - p) * 6 * t;
            if (t < 1.0 / 2.0) return q;
            if (t < 2.0 / 3.0) return p + (q - p) * (2.0 / 3.0 - t) * 6;
            return p;
        }

        private void DrawEffectSequence()
        {
            // Blurred glow layer
            using (var commandList = new CanvasCommandList(_sender))
            {
                using (var clds = commandList.CreateDrawingSession())
                {
                    foreach (DrawTextCall call in _effectSequence)
                    {
                        // draw a background rectangle only for explicit cell backgrounds (not SGR default / host fill)
                        if (!call.BackgroundIsDefault)
                        {
                            clds.FillRectangle(call.X, call.Y, call.Width, call.Height, call.BackgroundColor);
                        }
                        if (call.ForegroundIsDefault || call.Color == OutputColor)
                        {
                            clds.DrawText(call.Text, call.X, call.Y, OutputGlowColor, call.TextFormat);
                        }
                        else
                        {
                            clds.DrawText(call.Text, call.X, call.Y, call.Color, call.TextFormat);
                        }
                    }
                }
                var blurEffect = new GaussianBlurEffect { Source = commandList, BlurAmount = BlurAmount };
                _drawSession.DrawImage(blurEffect);
            }

            // Sharp layer
            foreach (DrawTextCall call in _effectSequence)
            {
                if (!call.BackgroundIsDefault)
                {
                    _drawSession.FillRectangle(call.X, call.Y, call.Width, call.Height, call.BackgroundColor);
                }
                _drawSession.DrawText(call.Text.Replace(' ', '\u00A0'), call.X, call.Y, call.Color, call.TextFormat);
            }
        }

        public void CycleColorConfiguration(Window wInstance)
        {
            _themeConfigIndex = (_themeConfigIndex + 1) % _namedColorConfigurations.Count;
            SetColorConfiguration(_namedColorConfigurations[_themeConfigIndex], wInstance);
        }

        public CanvasTextFormat GetControlTextFormat()
        {
            return new CanvasTextFormat
            {
                FontFamily = CurrentControlFont,
                FontSize = _currentControlFontSize
            };
        }

        public CanvasTextFormat GetTextFormat()
        {
            return new CanvasTextFormat
            {
                FontFamily = string.IsNullOrWhiteSpace(CurrentFont) ? "Cascadia Mono" : CurrentFont,
                FontSize = CurrentFontSize,
                WordWrapping = CanvasWordWrapping.NoWrap
            };
        }

        public string GetHexStringFromColor(Color color)
        {
            return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        }

        public Color GetColorFromHexString(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return OutputColor;
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
    }
}

