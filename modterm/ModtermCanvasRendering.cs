using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Modglass;
using System;
using Windows.UI;

namespace modterm
{
    public sealed partial class  MainWindow : Window
    {
        // Offset for banner color cycling effect
        private int _bannerColorOffset = 0;
        private void ModtermCanvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
        {
            ModglassDisplay.BeginEffectSequence(Effects.Glow);

            double cmdY = sender.ActualHeight - ModglassDisplay.CurrentFontSize - 20;
            ModglassDisplay.DrawAnsiText(sender, args.DrawingSession, 10f, (float)cmdY, "> " + _commandLine);

            double y = sender.ActualHeight - ModglassDisplay.CurrentFontSize * 2 - 25;
            int startIdx = Math.Max(0, _bufferLines.Count - 1 - _scrollOffset);
            
            for (int i = startIdx; i > 0; i--)
            {
                var line = _bufferLines[i];
                var color = ModglassDisplay.OutputColor;
                if (_commandLineHistory.Contains(line))
                {
                    //color = ModglassDisplay.InputColor;
                    line = "> " + line; // prefix command lines with >
                }
                ModglassDisplay.DrawAnsiText(sender, args.DrawingSession, 10f, (float)y, line);
                y -= ModglassDisplay.CurrentFontSize + 5;
                if (y < 0) break;
            }
            
            // Blinking cursor at correct position
            if (_cursorVisible)
            {
                // Caculate the text up to the cursor position
                string cmdLineWithPrompt = "> " + _commandLine;
                int cursorPosInText = 2 + _commandLineCursorPos; // 2 for '> '
                string textUpToCursor = cmdLineWithPrompt.Substring(0, Math.Min(cursorPosInText, cmdLineWithPrompt.Length));
                double cursorX = 10 + MeasureTextWidth(textUpToCursor, sender);
                ModglassDisplay.DrawAnsiText(sender, args.DrawingSession, (float)cursorX, (float)cmdY, "|");
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

            ModglassDisplay.EndEffectSequence();

            // draw all UI controls
            _upperRightControls?.DrawControls(sender, args.DrawingSession);
            _lowerRightControls?.DrawControls(sender, args.DrawingSession);
		}

        private double MeasureTextWidth(string text, CanvasControl canvas)
        {
            // adjust width since a trailing space isn't measured by CanvasTextLayout
            // - add a small fudge factor to account for this
            float trailingSpaceOffset = text.EndsWith(' ') ? ModglassDisplay.CurrentFontSize * 0.4f : 0f;

            using (var layout = new CanvasTextLayout(canvas, text,
                new CanvasTextFormat { FontFamily = ModglassDisplay.CurrentFont.Source, FontSize = (float)ModglassDisplay.CurrentFontSize }, 9999, 9999))
            {
                return layout.DrawBounds.Width + trailingSpaceOffset;
            }
        }
    }
}
