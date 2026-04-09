using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using System.Diagnostics;
using System;
using Windows.UI;
using Microsoft.UI;

namespace modterm
{
    public sealed partial class  ModtermWindow : Window
    {
        private int _lines = 0;
        private int _columns = 0;

        private int _leftTextPadding = 5;

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

            if (_resizeNeeded)
            {
                Debug.WriteLine($"Resize Needed. Resizing _vtCon and _terminal with {_lines} lines and {_columns} columns calc'd from drawing and measuring.");
                _vtController.ResizeView(_columns, _lines);
                _terminal?.Resize((short)_columns, (short)_lines);
                _appearanceInfoControl.TextContent = ModtermDisplay.GetAppearanceInfo(_lines, _columns);
                ControlCanvas.Invalidate();
                _resizeNeeded = false;
            }

            // use toprow to calculate visible area
            int topRow = _vtController.ViewPort.TopRow;
            var pageSpans = _vtController.ViewPort.GetPageSpans(topRow, rows, cols);
            double y = 0;
            foreach (var row in pageSpans)
            {
                float x = _leftTextPadding;
                int col = 0; // Reset column at the start of each row
                foreach (var span in row.Spans)
                {
                    // Convert VT color string to Windows.UI.Color
                    Color fg = ModtermDisplay.OutputColor;
                    try { fg = ColorFromWeb(span.ForgroundColor); } catch { }
                    
                    string textToDraw = span.Text ?? "";

                    // replace tabs with spaces (assuming tab stops every 4 columns)
                    //textToDraw = span.Text?.Replace("\t", "    ") ?? "";

                    // replace spaces with non-breaking spaces to prevent collapsing
                    //textToDraw = textToDraw.Replace(" ", "\u00A0");

                    // Draw the text span at the correct column position
                    x = _leftTextPadding + (col * measuredCharWidth);
                    ModtermDisplay.DrawText(textToDraw, x, (float)y, fg, ModtermDisplay.GetTextFormat());
                    
                    // Advance col by the number of characters in the span
                    col += textToDraw?.Length ?? 0;
                }
                y += ModtermDisplay.CurrentFontSize + 2;
            }


            // Draw debug grid lines for columns
            //for (int c = 0; c < cols; c++)
            //{
            //    float x = _leftTextPadding + (c * measuredCharWidth);
            //    args.DrawingSession.DrawLine(x, 0, x, (float)sender.ActualHeight, Colors.Red, 0.5f);
            //    if (x > sender.ActualWidth)
            //    {   Debug.WriteLine("Column " + c + " x=" + x + " exceeds canvas width " + sender.ActualWidth); break; }
            //}
            //// Draw debug grid lines for rows
            //for (int r = 0; r < rows; r++)
            //{
            //    float yLine = r * (ModtermDisplay.CurrentFontSize + 2);
            //    args.DrawingSession.DrawLine(0, yLine, (float)sender.ActualWidth, yLine, Colors.Blue, 0.5f);
            //    if (yLine > sender.ActualHeight)
            //    {   Debug.WriteLine("Row " + r + " y=" + yLine + " exceeds canvas height " + sender.ActualHeight); break; }
            //}

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
                return (float)layout.DrawBounds.Width * 1.1f; // add a small buffer to prevent clipping
            }
        }

        
    }
}
