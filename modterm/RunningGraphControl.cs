using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.UI;

namespace modterm
{
    public class RunningGraphControl : IModtermControl
    {
        public bool IsEngaged { get; set; }
        public bool IsHovered { get; set; }
        public bool IsPressed { get; set; }
        public Rect Location { get; set; }
        public string TextContent { get; set; }
        public bool ContentSizing { get; set; } = false; // graph has fixed size, not based on text content
        public int IntervalMs { get; set; } // how often to the data is updated
        public float DataPointMaxValue { get; set; } // max value for scaling the graph
        public float DataPointMinValue { get; set; } // min value for scaling the graph
        public List<float> DataPoints { get; set; } = new List<float>(); // current value to display

        public RunningGraphControl(Rect location, int intervalMs, float dataPointMinValue, float dataPointMaxValue)
        {
            Location = location;
            IntervalMs = intervalMs;
            DataPointMinValue = dataPointMinValue;
            DataPointMaxValue = dataPointMaxValue;
        }

        public void Draw(CanvasControl sender, CanvasDrawingSession cds,
            ModtermDisplay modtermDisplay)
        {
            Color controlColor = modtermDisplay.GetControlColor(this);
            Color controlBlurColor = modtermDisplay.GetControlGlowColor(this);

            // Graph parameters
            float width = (float)Location.Width;
            float height = (float)Location.Height;
            float left = (float)Location.X;
            float top = (float)Location.Y;
            float right = left + width;
            float bottom = top + height;
            int numPoints = (int)(width / 3f);
            if (DataPoints == null || DataPoints.Count < 2)
                return;
            int startIdx = Math.Max(0, DataPoints.Count - numPoints);
            int count = DataPoints.Count - startIdx;

            // Precompute points
            var points = new System.Numerics.Vector2[count];
            for (int i = 0; i < count; i++)
            {
                float x = right - i * 3f;
                float val = DataPoints[DataPoints.Count - 1 - i];
                float norm = (val - DataPointMinValue) / (DataPointMaxValue - DataPointMinValue);
                float y = top + (1 - norm) * height;
                points[i] = new System.Numerics.Vector2(x, y);
            }

            // Blur layer
            using (var commandList = new CanvasCommandList(sender))
            {
                using (var clds = commandList.CreateDrawingSession())
                {
                    //clds.DrawRoundedRectangle(Location, ModtermDisplay.CornerRadius, ModtermDisplay.CornerRadius, controlBlurColor, ModtermDisplay.LineWidth);
                    //if (!this.IsHovered)
                    //    clds.FillRoundedRectangle(Location, ModtermDisplay.CornerRadius, ModtermDisplay.CornerRadius,
                    //        Color.FromArgb(ModtermDisplay.BlurFillTransparency, controlBlurColor.R, controlBlurColor.G, controlBlurColor.B));
                    // Draw line graph (blur layer)
                    for (int i = 1; i < points.Length; i++)
                    {
                        // draw brighter line for blur layer to enhance the glow effect when blurred
                        clds.DrawLine(points[i - 1], points[i],
                            Color.FromArgb(255 /*ModtermDisplay.BlurFillTransparency*/, controlBlurColor.R, controlBlurColor.G, controlBlurColor.B),
                            modtermDisplay.LineWidth * 2);
                    }

                }
                var blurEffect = new GaussianBlurEffect { Source = commandList, BlurAmount = modtermDisplay.BlurAmount };
                cds.DrawImage(blurEffect);
            }

            // Sharp layer
            //cds.DrawRoundedRectangle(Location, modtermDisplay.CornerRadius, modtermDisplay.CornerRadius,
            //    Color.FromArgb(modtermDisplay.SharpBorderTransparency, controlColor.R, controlColor.G, controlColor.B), modtermDisplay.LineWidth);
            cds.FillRoundedRectangle(Location, modtermDisplay.CornerRadius, modtermDisplay.CornerRadius,
                Color.FromArgb(modtermDisplay.SharpFillTransparency, controlColor.R, controlColor.G, controlColor.B));
            // Draw line graph (sharp layer)
            for (int i = 1; i < points.Length; i++)
            {
                cds.DrawLine(points[i - 1], points[i], controlColor, modtermDisplay.LineWidth + 1);
            }
        }
    }
}
