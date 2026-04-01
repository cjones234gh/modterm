using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;

using System;
using Windows.UI;

namespace modterm
{
    public sealed partial class  ModtermWindow : Window
    {
        private int _lines;
        private int _columns;

        private int _leftTextPadding = 10;

        // Offset for banner color cycling effect
        private int _bannerColorOffset = 0;
        private void ModtermCanvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
        {

            ModtermDisplay.BeginEffectSequence(sender, args.DrawingSession, Effects.Glow);

            // Calculate rows/columns based on measured character width and font size
            int rows = (int)(sender.ActualHeight / (ModtermDisplay.CurrentFontSize + 2));
            float measuredCharWidth = MeasureCharWidth(sender, args);
            int cols = (int)(sender.ActualWidth / measuredCharWidth);
            _vtController.VisibleRows = rows;
            _vtController.VisibleColumns = cols;
            _lines = rows;
            _columns = cols;

            // use toprow to calculate visible area
            int topRow = _vtController.ViewPort.TopRow;
            var pageSpans = _vtController.ViewPort.GetPageSpans(topRow, rows, cols);
            double y = 0;
            foreach (var row in pageSpans)
            {
                float x = _leftTextPadding;
                foreach (var span in row.Spans)
                {
                    // Convert VT color string to Windows.UI.Color
                    Color fg = ModtermDisplay.OutputColor;
                    try { fg = ColorFromWeb(span.ForgroundColor); } catch { } 
                    // Draw the text span
                    if (!string.IsNullOrEmpty(span.Text))
                    {
                        ModtermDisplay.DrawText(span.Text, x, (float)y, fg, ModtermDisplay.GetTextFormat());
                    }
                    // Advance X by measured width
                    using (var layout = new CanvasTextLayout(args.DrawingSession, span.Text ?? "", ModtermDisplay.GetTextFormat(), 9999, 9999))
                    {
                        x += (float)layout.DrawBounds.Width;
                    }
                }
                y += ModtermDisplay.CurrentFontSize + 2;
            }

            // Draw blinking cursor if visible
            if (_cursorVisible)
            {
                var cursor = _vtController.ViewPort.CursorPosition;
                float cursorX = _leftTextPadding + (float)(cursor.Column * measuredCharWidth);
                float cursorY = (float)(cursor.Row * (ModtermDisplay.CurrentFontSize + 2));
                args.DrawingSession.DrawText("|", cursorX, cursorY, ModtermDisplay.OutputColor, ModtermDisplay.GetTextFormat());
            }

            ModtermDisplay.EndEffectSequence();

            // Helper: Convert #RRGGBB or #AARRGGBB to Color
            Color ColorFromWeb(string web)
            {
                if (string.IsNullOrEmpty(web)) return ModtermDisplay.OutputColor;
                if (web.StartsWith("#"))
                {
                    if (web.Length == 7)
                        return Color.FromArgb(255,
                            Convert.ToByte(web.Substring(1, 2), 16),
                            Convert.ToByte(web.Substring(3, 2), 16),
                            Convert.ToByte(web.Substring(5, 2), 16));
                    if (web.Length == 9)
                        return Color.FromArgb(
                            Convert.ToByte(web.Substring(1, 2), 16),
                            Convert.ToByte(web.Substring(3, 2), 16),
                            Convert.ToByte(web.Substring(5, 2), 16),
                            Convert.ToByte(web.Substring(7, 2), 16));
                }
                return ModtermDisplay.OutputColor;
            }

            // Visual selection highlight (simple semi-transparent overlay)
            if (!string.IsNullOrEmpty(_selectedText) && _isSelecting)
            {
                double x = Math.Min(_selectionStart.X, _selectionEnd.X);
                double yy = Math.Min(_selectionStart.Y, _selectionEnd.Y);
                double width = Math.Abs(_selectionEnd.X - _selectionStart.X);
                double height = Math.Abs(_selectionEnd.Y - _selectionStart.Y);
                if (width > 0 && height > 0)
                {
                    args.DrawingSession.FillRectangle(
                        new Windows.Foundation.Rect(x, yy, width, height),
                        Color.FromArgb(80, 0, 120, 255));
                }
            }

            

            // draw all UI controls
            //_titleBarControls?.DrawControls(sender, args.DrawingSession);
            _lowerRightControls?.DrawControls(sender, args.DrawingSession);
		}

        public void ControlCanvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)

        {             // draw all UI controls
            _titleBarControls?.DrawControls(sender, args.DrawingSession);
        }

        // Measure the width of a typical monospace character for accurate column calculation
        private float MeasureCharWidth(CanvasControl sender, CanvasDrawEventArgs args)
        {
            // Use 'W' as a typical wide character for monospace fonts
            using (var layout = new CanvasTextLayout(args.DrawingSession, "W", ModtermDisplay.GetTextFormat(), 9999, 9999))
            {
                return (float)layout.DrawBounds.Width;
            }
        }

        
    }
}
