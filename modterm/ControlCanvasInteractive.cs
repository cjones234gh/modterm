using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.System;

namespace modterm
{
    public sealed partial class ModtermWindow : Window
    {
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
