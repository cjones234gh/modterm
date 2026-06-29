using Microsoft.UI.Windowing;
using System;
using Windows.Graphics;

namespace modterm
{
    internal static class WindowPlacement
    {
        private const int MinimumVisibleExtent = 32;

        public static bool IsRectVisibleOnAnyDisplay(RectInt32 rect)
        {
            // DisplayArea.FindAll() cannot be enumerated with foreach due to a WinRT bug.
            var displays = DisplayArea.FindAll();
            for (int i = 0; i < displays.Count; i++)
            {
                var visible = Intersect(rect, displays[i].WorkArea);
                if (visible.Width >= MinimumVisibleExtent && visible.Height >= MinimumVisibleExtent)
                {
                    return true;
                }
            }

            return false;
        }

        public static RectInt32 EnsureVisible(RectInt32 rect)
        {
            try
            {
                if (IsRectVisibleOnAnyDisplay(rect))
                {
                    return rect;
                }
            }
            catch (Exception)
            {
                // If display enumeration fails, fall through and place on the primary display.
            }

            try
            {
                var primary = DisplayArea.Primary;
                if (primary is null)
                {
                    return rect;
                }

                var workArea = primary.WorkArea;
                int x = workArea.X + Math.Max(0, (workArea.Width - rect.Width) / 2);
                int y = workArea.Y + Math.Max(0, (workArea.Height - rect.Height) / 2);

                return new RectInt32
                {
                    X = x,
                    Y = y,
                    Width = rect.Width,
                    Height = rect.Height
                };
            }
            catch (Exception)
            {
                return rect;
            }
        }

        private static RectInt32 Intersect(RectInt32 a, RectInt32 b)
        {
            int left = Math.Max(a.X, b.X);
            int top = Math.Max(a.Y, b.Y);
            int right = Math.Min(a.X + a.Width, b.X + b.Width);
            int bottom = Math.Min(a.Y + a.Height, b.Y + b.Height);

            return new RectInt32
            {
                X = left,
                Y = top,
                Width = Math.Max(0, right - left),
                Height = Math.Max(0, bottom - top)
            };
        }
    }
}
