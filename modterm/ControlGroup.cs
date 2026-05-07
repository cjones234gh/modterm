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
    public class ControlGroup
    {
        private float _widthPadding;
        private float _heightPadding;
        private float _padding;
        private float _topControlPadding;

        public enum ControlDock
        {
            Left,
            Top,
            Right,
            Bottom
        }
        public ControlDock Dock { get; set; }

        public List<ModtermControl> Controls { get; set; } = new List<ModtermControl>();

        /// <summary>
        /// Cached text measurements for expandable (child flyout) controls; invalidated on resize via <see cref="InvalidateExpandableChildMeasureCache"/>.
        /// </summary>
        private Size _expandableMeasureCanvasSize = new Size(1, 1);
        private readonly Dictionary<ModtermControl, (float w, float h)[]> _expandableChildSizes = new Dictionary<ModtermControl, (float w, float h)[]>();

        public void InvalidateExpandableChildMeasureCache()
        {
            _expandableChildSizes.Clear();
            _expandableMeasureCanvasSize = new Size(1, 1);
        }

        public ControlGroup(ControlDock dock, float mtdPadding)
        {
            Dock = dock;
            _padding = mtdPadding;
            _widthPadding = 2 * _padding;
            _heightPadding = 1.6f * _padding;
            _topControlPadding = 45; // Adjust this value as needed
        }

        public void DrawControls(CanvasControl sender, CanvasDrawingSession cds, ModtermDisplay mtd)
        {
            ArrangeControls(sender, cds, mtd);
            // First pass: normal controls and collapsed flyout parents.
            // Second pass: expanded flyout parents (with children) so child strip draws above earlier right-dock controls.
            foreach (var control in Controls)
            {
                if (HasExpandableFlyout(control) && control.IsEngaged)
                    continue;
                control.Draw(sender, cds, mtd);
            }
            foreach (var control in Controls)
            {
                if (HasExpandableFlyout(control) && control.IsEngaged)
                    control.Draw(sender, cds, mtd);
            }
        }

        private static bool HasExpandableFlyout(ModtermControl control)
            => control.Children is { Count: > 0 };

        private void ArrangeControls(CanvasControl sender, CanvasDrawingSession cds, ModtermDisplay mtd)
        {
            float canvasWidth = (float)sender.ActualWidth;
            float canvasHeight = (float)sender.ActualHeight;

            float y = 0;

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
                    y = mtd.ControlMargin + _topControlPadding;
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
                        float xRight = canvasWidth - mtd.ControlMarginRight - width;
                        control.Location = new Windows.Foundation.Rect(xRight, y, width, height);
                        y += _padding;

                        if (HasExpandableFlyout(control) && control.IsEngaged)
                            LayoutExpandableChildrenLeftOfParent(sender, mtd, control);
                    }
                    break;
                case ControlDock.Bottom:
                    throw new NotImplementedException();
            }
        }

        /// <summary>
        /// Places <paramref name="parent"/>'s children in a horizontal row to the left of the parent, spaced by <see cref="ModtermDisplay.ControlMarginRight"/>.
        /// Text extents are cached per canvas size until <see cref="InvalidateExpandableChildMeasureCache"/> runs (e.g. on window resize).
        /// </summary>
        private void LayoutExpandableChildrenLeftOfParent(CanvasControl sender, ModtermDisplay mtd, ModtermControl parent)
        {
            if (parent.Children is not { Count: > 0 })
                return;

            float cw = (float)sender.ActualWidth;
            float ch = (float)sender.ActualHeight;

            if (_expandableMeasureCanvasSize.Width != cw || _expandableMeasureCanvasSize.Height != ch)
            {
                _expandableChildSizes.Clear();
                _expandableMeasureCanvasSize = new Size(cw, ch);
            }

            if (!_expandableChildSizes.TryGetValue(parent, out var wh) || wh.Length != parent.Children.Count)
            {
                wh = new (float w, float h)[parent.Children.Count];
                var textFormat = mtd.GetControlTextFormat();
                for (int i = 0; i < parent.Children.Count; i++)
                {
                    var child = parent.Children[i];
                    using (var layout = new CanvasTextLayout(sender, child.TextContent, textFormat, 9999, 9999))
                    {
                        wh[i] = ((float)layout.DrawBounds.Width + _widthPadding,
                            (float)layout.DrawBounds.Height + _heightPadding);
                    }
                }
                _expandableChildSizes[parent] = wh;
            }

            float gap = mtd.ControlMarginRight;
            float parentLeft = (float)parent.Location.Left;
            float parentTop = (float)parent.Location.Top;
            float parentHeight = (float)parent.Location.Height;

            float x = parentLeft - gap;
            for (int i = parent.Children.Count - 1; i >= 0; i--)
            {
                var child = parent.Children[i];
                var (w, h) = wh[i];
                x -= w;
                float childY = parentTop + (parentHeight - h) / 2f;
                child.Location = new Rect(x, childY, w, h);
                x -= gap;
            }

            float minChildLeft = float.MaxValue;
            foreach (var rowChild in parent.Children)
                minChildLeft = (float)Math.Min(minChildLeft, rowChild.Location.Left);

            float minX = mtd.ControlMargin;
            if (minChildLeft < minX)
            {
                float delta = minX - minChildLeft;
                foreach (var shiftChild in parent.Children)
                {
                    var r = shiftChild.Location;
                    shiftChild.Location = new Rect(r.X + delta, r.Y, r.Width, r.Height);
                }
            }
        }
    }
}
