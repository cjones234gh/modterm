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
    public interface IModtermControl
    {
        public bool IsEngaged { get; set; }
        public bool IsHovered { get; set; }
        public bool IsPressed { get; set; }
        public Rect Location { get; set; }
        public string TextContent { get; set; }
        public bool ContentSizing { get; set; }

        public void Draw(CanvasControl sender, CanvasDrawingSession cds);
    }
}
