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
        private float _measuredCharWidth;

        private int _leftTextPadding = 5;

        // Offset for banner color cycling effect
        private int _bannerColorOffset = 0;
        private void ModtermCanvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
        {
            // Do not spawn the conhost until we can measure the canvas during drawing and determine how many rows/columns we can fit
            if (!_terminal.Started)
            {
                int measuredRows = (int)(sender.ActualHeight / (_mtd.CurrentFontSize + 2));
                float measuredCharWidth = MeasureCharWidth(sender, args);
                int measuredCols = (int)(sender.ActualWidth / measuredCharWidth);
                _lines = measuredRows;
                _columns = measuredCols;
                _measuredCharWidth = measuredCharWidth;
                _vtController.VisibleRows = _lines;
                _vtController.VisibleColumns = _columns;
                StartConPTY();
            }

            _mtd.BeginEffectSequence(sender, args.DrawingSession, Effects.Glow);

            // use toprow to calculate visible area
            int topRow = _vtController.ViewPort.TopRow;
            var pageSpans = _vtController.ViewPort.GetPageSpans(topRow, _lines, _columns);
            double y = 0;
            foreach (var row in pageSpans)
            {
                float x = _leftTextPadding;
                int col = 0; // Reset column at the start of each row
                foreach (var span in row.Spans)
                {
                    //Debug.WriteLine($"span.BackgroundColor: {span.BackgroundColor}, span.ForgroundColor: {span.ForgroundColor}");
                    Color fg = _mtd.OutputColor;
                    if (!span.ForegroundIsDefault)
                    {
                        try { fg = _mtd.GetColorFromHexString(span.ForgroundColor); } catch { }
                    }

                    Color bg = Colors.Black;
                    if (!span.BackgroundIsDefault)
                    {
                        try { bg = _mtd.GetColorFromHexString(span.BackgroundColor); } catch { }
                    }

                    if (span.Hidden)
                    {
                        fg = bg; // Set foreground to background color to hide text
                    }

                    // Draw the text span at the correct column position
                    x = _leftTextPadding + (col * _measuredCharWidth);
                    _mtd.DrawText(span.Text, x, (float)y, (float)(span.Text.Length * _measuredCharWidth), fg, bg, _mtd.GetTextFormat(), span.ForegroundIsDefault, span.BackgroundIsDefault);
                    
                    // Advance col by the number of characters in the span
                    col += span.Text.Length;
                }
                y += _mtd.CurrentFontSize + 2;
            }

            // Debug: Draw grid lines for columns and rows
            //DrawDebugGrid(sender, args);

            // Draw blinking cursor if visible
            if (_cursorVisible)
            {
                var cursor = _vtController.ViewPort.CursorPosition;
                float cursorX = _leftTextPadding + (float)(cursor.Column * _measuredCharWidth);
                float cursorY = (float)(cursor.Row * (_mtd.CurrentFontSize + 2));
                args.DrawingSession.DrawText("|", cursorX, cursorY, _mtd.OutputColor, _mtd.GetTextFormat());
            }

            _mtd.EndEffectSequence();

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

        // Measure the width of a typical monospace character for accurate column calculation
        private float MeasureCharWidth(CanvasControl sender, CanvasDrawEventArgs args)
        {
            // Use 'W' as a typical wide character for monospace fonts
            using (var layout = new CanvasTextLayout(args.DrawingSession, "W", _mtd.GetTextFormat(), 9999, 9999))
            {
                return (float)layout.DrawBounds.Width * 1.1f; // add a small buffer to prevent clipping
            }
        }

        // Debug method to draw grid lines for columns and rows
        private void DrawDebugGrid(CanvasControl sender, CanvasDrawEventArgs args)
        {
            // Draw debug grid lines for columns
            for (int c = 0; c < _columns; c++)
            {
                float x = _leftTextPadding + (c * _measuredCharWidth);
                args.DrawingSession.DrawLine(x, 0, x, (float)sender.ActualHeight, Colors.Red, 0.5f);
                if (x > sender.ActualWidth)
                { Debug.WriteLine("Column " + c + " x=" + x + " exceeds canvas width " + sender.ActualWidth); break; }
            }
            // Draw debug grid lines for rows
            for (int r = 0; r < _lines; r++)
            {
                float yLine = r * (_mtd.CurrentFontSize + 2);
                args.DrawingSession.DrawLine(0, yLine, (float)sender.ActualWidth, yLine, Colors.Blue, 0.5f);
                if (yLine > sender.ActualHeight)
                { Debug.WriteLine("Row " + r + " y=" + yLine + " exceeds canvas height " + sender.ActualHeight); break; }
            }
        }



    }
}
