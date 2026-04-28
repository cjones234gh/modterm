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
        public enum ControlDock
        {
            Left,
            Top,
            Right,
            Bottom
        }
        public ControlDock Dock { get; set; }

        public List<ModtermControl> Controls { get; set; } = new List<ModtermControl>();

        public ControlGroup(ControlDock dock)
        {
            Dock = dock;
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
            float padding = mtd.ControlPadding;
            float canvasWidth = (float)sender.ActualWidth;
            float canvasHeight = (float)sender.ActualHeight;

            float x = 0, y = 0;

            switch (Dock)
            {
                case ControlDock.Left:
                    throw new NotImplementedException();
                case ControlDock.Top:
                    // this is a bit more complex, we need to measure all controls first to get the total width, then we can center them as a group.
                    float totalWidth = 0;
                    foreach (var control in Controls)
                    { 
                        totalWidth += control.ContentSizing
                            ? (float)(new CanvasTextLayout(sender, control.TextContent, mtd.GetControlTextFormat(), 9999, 9999).DrawBounds.Width + padding * 2)
                            : (float)control.Location.Width + padding;
                    }
                    float startX = (canvasWidth - totalWidth) / 2;
                    float yy = mtd.ControlMargin;
                    foreach (var control in Controls)
                    {
                        float width = control.ContentSizing
                            ? (float)(new CanvasTextLayout(sender, control.TextContent, mtd.GetControlTextFormat(), 9999, 9999).DrawBounds.Width + padding * 2)
                            : (float)control.Location.Width;
                        float height = control.ContentSizing
                            ? (float)(new CanvasTextLayout(sender, control.TextContent, mtd.GetControlTextFormat(), 9999, 9999).DrawBounds.Height + padding * 1.4f)
                            : (float)control.Location.Height;
                        control.Location = new Windows.Foundation.Rect(startX, yy, width, height);
                        startX += width + padding;
                    }
                    break;
                case ControlDock.Right:
                    y = canvasHeight - mtd.ControlMargin;
                    for (int i = 0; i < Controls.Count; i++)
                    {
                        var control = Controls[i];
                        float width, height;
                        if (control.ContentSizing)
                        {
                            var textFormat = mtd.GetControlTextFormat();
                            var textLayout = new CanvasTextLayout(sender, control.TextContent, textFormat, 9999, 9999);
                            width = (float)textLayout.DrawBounds.Width + 2 * padding;
                            height = (float)textLayout.DrawBounds.Height + 2 * padding;
                        }
                        else
                        {
                            width = (float)control.Location.Width;
                            height = (float)control.Location.Height;
                        }
                        y -= height;
                        float xRight = canvasWidth - mtd.ControlMargin - width;
                        control.Location = new Windows.Foundation.Rect(xRight, y, width, height);
                        y -= padding;
                    }
                    break;
                case ControlDock.Bottom:
                    throw new NotImplementedException();
            }
        }
    }
}
