using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace modterm
{
    public class ControlGroup
    {
        private float _widthPadding;
        private float _heightPadding;
        private float _padding;
        public enum ControlDock
        {
            Left,
            Top,
            Right,
            Bottom
        }
        public ControlDock Dock { get; set; }

        public List<ModtermControl> Controls { get; set; } = new List<ModtermControl>();

        public ControlGroup(ControlDock dock, float mtdPadding)
        {
            Dock = dock;
            _padding = mtdPadding;
            _widthPadding = 2 * _padding;
            _heightPadding = 1.6f * _padding;
        }

        public void DrawControls(CanvasControl sender, CanvasDrawingSession cds, ModtermDisplay mtd)
        {
            ArrangeControls(sender, cds, mtd);
            foreach (var control in Controls)
            {
                control.Draw(sender, cds, mtd);
            }
        }

        private void ArrangeControls(CanvasControl sender, CanvasDrawingSession cds, ModtermDisplay mtd)
        {
            float canvasWidth = (float)sender.ActualWidth;
            float canvasHeight = (float)sender.ActualHeight;

            float x = 0, y = 0;

            switch (Dock)
            {
                case ControlDock.Left:
                    throw new NotImplementedException();
                case ControlDock.Top:
                    float totalWidth = 0;
                    foreach (var control in Controls)
                    { 
                        totalWidth += control.ContentSizing
                            ? (float)(new CanvasTextLayout(sender, control.TextContent, mtd.GetControlTextFormat(), 9999, 9999).DrawBounds.Width + _widthPadding)
                            : (float)control.Location.Width + _padding;
                    }
                    float startX = (canvasWidth - totalWidth) / 2;
                    float yy = mtd.ControlMargin;
                    foreach (var control in Controls)
                    {
                        float width = control.ContentSizing
                            ? (float)(new CanvasTextLayout(sender, control.TextContent, mtd.GetControlTextFormat(), 9999, 9999).DrawBounds.Width + _widthPadding)
                            : (float)control.Location.Width;
                        float height = control.ContentSizing
                            ? (float)(new CanvasTextLayout(sender, control.TextContent, mtd.GetControlTextFormat(), 9999, 9999).DrawBounds.Height + _heightPadding)
                            : (float)control.Location.Height;
                        control.Location = new Windows.Foundation.Rect(startX, yy, width, height);
                        startX += width + _padding;
                    }
                    break;
                case ControlDock.Right:
                    y = mtd.ControlMargin + 45;
                    for (int i = 0; i < Controls.Count; i++)
                    {
                        var control = Controls[i];
                        float width, height;
                        if (control.ContentSizing)
                        {
                            var textFormat = mtd.GetControlTextFormat();
                            var textLayout = new CanvasTextLayout(sender, control.TextContent, textFormat, 9999, 9999);
                            width = (float)textLayout.DrawBounds.Width + _widthPadding;
                            height = (float)textLayout.DrawBounds.Height + _heightPadding;
                        }
                        else
                        {
                            width = (float)control.Location.Width;
                            height = (float)control.Location.Height;
                        }
                        y += height;
                        float xRight = canvasWidth - mtd.ControlMargin - width;
                        control.Location = new Windows.Foundation.Rect(xRight, y, width, height);
                        y += _padding;
                    }
                    break;
                case ControlDock.Bottom:
                    throw new NotImplementedException();
            }
        }
    }
}
