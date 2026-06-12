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
using System.Numerics;
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
        // glow colors for terminal and control text
        public Color OutputGlowColor { get; set; }
        // normal colors for terminal and control text
        public Color OutputColor { get; set; }
        // control colors (normal, engaged, hovered, etc.)
        public Color LabelColor { get; set; }
        public Color LabelGlowColor { get; set; }
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

        private string _currentFont = "BlexMono Nerd Font Mono";
        private string _currentControlFont = "BlexMono Nerd Font Mono";
        private float _currentFontSize = 12f;
        private float _controlFontScale = 1f;
        private float _currentControlFontSize = 12f;
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

        public CanvasTextFormat CurrentTextFormat { get; set; } = null!;
        public CanvasTextFormat CurrentControlTextFormat { get; set; } = null!;

        public string CurrentFont 
        { 
            get 
            {
                return _currentFont;
            }
            set 
            {
                _currentFont = value; 
                CurrentTextFormat.FontFamily = _currentFont; 
            }
        }

        public string CurrentControlFont 
        { 
            get { return _currentControlFont; }
            set { _currentControlFont = value; CurrentControlTextFormat.FontFamily = _currentControlFont; }
        }
        
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
                CurrentTextFormat.FontSize = _currentFontSize;
                CurrentControlTextFormat.FontSize = _currentControlFontSize;
                ControlPadding = _currentControlFontSize / 1.75f;
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
            CurrentTextFormat = new CanvasTextFormat { FontFamily = _currentFont,
                FontSize = _currentFontSize, WordWrapping = CanvasWordWrapping.NoWrap };
            CurrentControlTextFormat = new CanvasTextFormat { FontFamily = _currentControlFont,
                FontSize = _currentControlFontSize, WordWrapping = CanvasWordWrapping.NoWrap };
            _namedColorConfigurations = GetDefaultThemeConfigurations();
            _themeConfigIndex = 0;

            // internal control defaults
            CornerRadius = 0.5f;
            BlurFillTransparency = 25;
            SharpBorderTransparency = 200;
            SharpFillTransparency = 10;
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

        public List<ThemeConfiguration> GetAllThemeConfigurations()
        {
            return _namedColorConfigurations;
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
            LabelColor = config.LabelColor;
            LabelGlowColor = config.LabelBlurColor;
            BlurAmount = config.BlurAmount;
            OpacityPct = config.WindowOpacityPct;
            TintColor = config.WindowColor;
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

        public void DrawText(string text, float x, float y, float width, Color color, Color bgColor, CanvasTextFormat textFormat, bool foregroundIsDefault = false, bool backgroundIsDefault = false, bool fitToCell = false, float cellHeight = 0f)
        {
            _effectSequence.Add(new DrawTextCall(text, x, y, width, color, bgColor, textFormat, foregroundIsDefault, backgroundIsDefault, fitToCell, cellHeight));
        }

        public void DrawModtermLabel(CanvasControl sender, CanvasDrawingSession cds, ModtermLabel label)
        {
            Color labelColor = LabelColor;
            Color labelBlurColor = LabelGlowColor;

            // blur layer - draw text content
            using (var commandList = new CanvasCommandList(sender))
            {
                using (var clds = commandList.CreateDrawingSession())
                {
                    clds.DrawText(label.TextContent, (float)label.Location.X + ControlPadding,
                        (float)label.Location.Y + ControlPadding / 4, labelBlurColor, CurrentControlTextFormat);
                }

                var blurEffect = new GaussianBlurEffect { Source = commandList, BlurAmount = BlurAmount };
                cds.DrawImage(blurEffect);
            }

            
            // sharp layer - draw text content
            cds.DrawText(label.TextContent, (float)label.Location.X + ControlPadding,
                (float)label.Location.Y + ControlPadding / 4, labelColor, CurrentControlTextFormat);

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
            }

            // Blurred glow layer
            using (var commandList = new CanvasCommandList(_sender))
            {
                using (var clds = commandList.CreateDrawingSession())
                {
                    foreach (DrawTextCall call in _effectSequence)
                    {
                        Color glyphColor = (call.ForegroundIsDefault || call.Color == OutputColor)
                            ? OutputGlowColor
                            : call.Color;

                        if (call.FitToCell)
                        {
                            DrawGlyphFitted(clds, call, glyphColor);
                        }
                        else
                        {
                            clds.DrawText(call.Text, call.X, call.Y, glyphColor, call.TextFormat);
                        }
                    }
                }
                var blurEffect = new GaussianBlurEffect { Source = commandList, BlurAmount = BlurAmount };
                _drawSession.DrawImage(blurEffect);
            }

            // Sharp layer
            foreach (DrawTextCall call in _effectSequence)
            {
                if (call.FitToCell)
                {
                    DrawGlyphFitted(_drawSession, call, call.Color);
                }
                else
                {
                    _drawSession.DrawText(call.Text.Replace(' ', '\u00A0'), call.X, call.Y, call.Color, call.TextFormat);
                }
            }
        }

        // Cache of the rendered ink bounds of a full braille cell (U+28FF) per text format.
        // The reference is measured from the actually-drawn glyph, so it reflects the font
        // that really renders braille - whether that is the primary font or a DirectWrite
        // fallback (e.g. Consolas, which has no braille glyphs of its own).
        private readonly Dictionary<CanvasTextFormat, Rect> _brailleCellInkBounds = new Dictionary<CanvasTextFormat, Rect>();

        // The full braille cell glyph (all 8 dots set). Its ink bounds define the design
        // cell that every other braille glyph's dots are positioned within.
        private const string FullBrailleCell = "\u28FF";

        private Rect GetBrailleCellInkBounds(ICanvasResourceCreator resourceCreator, CanvasTextFormat format)
        {
            if (_brailleCellInkBounds.TryGetValue(format, out Rect cached))
                return cached;

            using var refLayout = new CanvasTextLayout(resourceCreator, FullBrailleCell, format, 0f, 0f)
            {
                WordWrapping = CanvasWordWrapping.NoWrap
            };

            // DrawBounds is the ink rectangle of the glyph as actually rasterized, including
            // any fallback substitution - unlike LayoutBounds, which uses the primary font's
            // (e.g. Consolas) line metrics and underestimates a taller fallback glyph.
            Rect bounds = refLayout.DrawBounds;

            // Avoid unbounded growth across font/size changes (only ~2 live formats normally).
            if (_brailleCellInkBounds.Count > 16)
                _brailleCellInkBounds.Clear();

            _brailleCellInkBounds[format] = bounds;
            return bounds;
        }

        // Draws a single braille glyph scaled to fill exactly one grid cell. The full-cell
        // ink reference (measured from the real, possibly fallback, font) is mapped onto the
        // terminal cell; because every braille glyph shares the same pen origin and design
        // cell, each glyph's dots land correctly and stay confined to their row instead of
        // overflowing vertically into neighbouring rows.
        private void DrawGlyphFitted(CanvasDrawingSession ds, DrawTextCall call, Color color)
        {
            Rect reference = GetBrailleCellInkBounds(ds, call.TextFormat);
            if (reference.Width <= 0 || reference.Height <= 0)
            {
                ds.DrawText(call.Text, call.X, call.Y, color, call.TextFormat);
                return;
            }

            using var layout = new CanvasTextLayout(ds, call.Text, call.TextFormat, 0f, 0f)
            {
                WordWrapping = CanvasWordWrapping.NoWrap
            };

            float scaleX = call.Width / (float)reference.Width;
            float scaleY = call.CellHeight / (float)reference.Height;

            Matrix3x2 prior = ds.Transform;
            ds.Transform =
                Matrix3x2.CreateTranslation((float)-reference.Left, (float)-reference.Top) *
                Matrix3x2.CreateScale(scaleX, scaleY) *
                Matrix3x2.CreateTranslation(call.X, call.Y);

            ds.DrawTextLayout(layout, 0f, 0f, color);
            ds.Transform = prior;
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

