using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Foundation;

namespace modterm
{
    public class DisplayLabelGroup
    {
        private float _widthPadding;
        private float _heightPadding;
        private float _padding;
        private float _topControlPadding;

        public enum LabelDock
        {
            Left,
            Top,
            Right,
            Bottom
        }
        public LabelDock Dock { get; set; }
        public List<DisplayLabel> Labels { get; set; } = new List<DisplayLabel>();

        public DisplayLabelGroup(LabelDock dock, float mtdPadding)
        {
            Dock = dock;
            _padding = mtdPadding;
            _widthPadding = 2 * _padding;
            _heightPadding = 1.6f * _padding;
            _topControlPadding = 45; // Adjust this value as needed
        }

        public void DrawLabels(CanvasControl sender, CanvasDrawingSession cds, ModtermDisplay mtd)
        {
            ArrangeLabels(sender, cds, mtd);
            foreach (var label in Labels)
            {
                label.Draw(sender, cds, mtd);
            }
            
        }

        private void ArrangeLabels(CanvasControl sender, CanvasDrawingSession cds, ModtermDisplay mtd)
        {
            float canvasWidth = (float)sender.ActualWidth;
            float canvasHeight = (float)sender.ActualHeight;

            float y = 0;

            switch (Dock)
            {
                case LabelDock.Left:
                    throw new NotImplementedException();
                case LabelDock.Top:
                    float totalWidth = 0;
                    foreach (var control in Labels)
                    { 
                        totalWidth += control.ContentSizing
                            ? (float)(new CanvasTextLayout(sender, control.TextContent, mtd.CurrentControlTextFormat, 9999, 9999).DrawBounds.Width + _widthPadding)
                            : (float)control.Location.Width + _padding;
                    }
                    float startX = (canvasWidth - totalWidth) / 2;
                    float yy = mtd.ControlMargin;
                    foreach (var control in Labels)
                    {
                        float width = control.ContentSizing
                            ? (float)(new CanvasTextLayout(sender, control.TextContent, mtd.CurrentControlTextFormat, 9999, 9999).DrawBounds.Width + _widthPadding)
                            : (float)control.Location.Width;
                        float height = control.ContentSizing
                            ? (float)(new CanvasTextLayout(sender, control.TextContent, mtd.CurrentControlTextFormat, 9999, 9999).DrawBounds.Height + _heightPadding)
                            : (float)control.Location.Height;
                        control.Location = new Windows.Foundation.Rect(startX, yy, width, height);
                        startX += width + _padding;
                    }
                    break;
                case LabelDock.Right:
                    throw new NotImplementedException();
                case LabelDock.Bottom:
                    throw new NotImplementedException();
            }
        }
    }
}
