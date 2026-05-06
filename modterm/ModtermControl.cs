using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Windows.Foundation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace modterm
{
    public abstract class ModtermControl
    {
        public bool IsEngaged { get; set; }
        public bool IsHovered { get; set; }
        public bool IsPressed { get; set; }
        public bool Interactive { get; set; }        
        public Rect Location { get; set; }
        public string TextContent { get; set; }
        public bool ContentSizing { get; set; }

        public EventHandler Clicked { get; set; }

        public List<ModtermControl> Children { get; set; } = new List<ModtermControl>();

        public abstract void Draw(CanvasControl sender, CanvasDrawingSession cds, ModtermDisplay mtd);

        public void HandleClick()
        {
            if (Interactive)
            {
                Clicked?.Invoke(this, EventArgs.Empty);
            }
        }
    }
}
