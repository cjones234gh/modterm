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
    public sealed partial class ModtermWindow : Window
    {
        public void ControlCanvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)

        {             // draw all UI controls
            _titleBarControls?.DrawControls(sender, args.DrawingSession, _mtd);
        }
    }
}
