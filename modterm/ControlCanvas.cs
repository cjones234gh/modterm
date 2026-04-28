using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using System.Diagnostics;
using System;
using Windows.UI;
using Microsoft.UI;
using Windows.Foundation;

namespace modterm
{
    public sealed partial class ModtermWindow : Window
    {
        public void ControlCanvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
        {             // draw all UI controls
            _titleBarControls?.DrawControls(sender, args.DrawingSession, _mtd);
        }

        public void ControlCanvas_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            Point currentPoint = e.GetCurrentPoint(ControlCanvas).Position;
            foreach (var control in _titleBarControls.Controls)
            {
                if (control.Location.Contains(currentPoint) && control.Interactive)
                {
                    control.IsPressed = true;
                    control.IsEngaged = true;
                }
                else
                {
                    control.IsPressed = false;
                    control.IsEngaged = false;
                }
            }
            ControlCanvas.Invalidate();
        }

        public void ControlCanvas_PointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            Point currentPoint = e.GetCurrentPoint(ControlCanvas).Position;
            foreach (var control in _titleBarControls.Controls)
            {
                if (control.IsEngaged)
                {
                    control.IsPressed = false;
                    control.IsEngaged = false;
                    // Check if the pointer is still over the control on release to trigger click action
                    if (control.Location.Contains(currentPoint))
                    {
                        control.HandleClick();
                    }
                }
            }
            ControlCanvas.Invalidate();
        }

        public void ControlCanvas_PointerMoved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            Point currentPoint = e.GetCurrentPoint(ControlCanvas).Position;

            foreach (var control in _titleBarControls.Controls)
            {
                control.IsHovered = control.Location.Contains(currentPoint);
            }

            ControlCanvas.Invalidate();
        }
    }
}
