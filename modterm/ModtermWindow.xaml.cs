using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Windows.System;
using Windows.UI;
using WinUIEx;


namespace modterm
{
    public sealed partial class ModtermWindow : Window
    {
        public ModtermWindow()
        {
            this.InitializeComponent();
            this.InitializeApplication();

            // ConPTY is started from the first ModtermCanvas_Draw once layout size is real (see ModtermCanvasDrawing).
            this.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                () =>
                {
                    ModtermCanvas.Invalidate();
                    ControlCanvas.Invalidate();
                });
        }
    }
}