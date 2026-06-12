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
    public class DisplayLabel
    {
        public DisplayLabel(string textContent, bool contentSizing = true, Rect location = new Rect())
        {
            TextContent = textContent;
            ContentSizing = contentSizing;
            Location = location;
        }
        public Rect Location { get; set; }
        public string TextContent { get; set; } = string.Empty;
        public bool ContentSizing { get; set; }
        public void Draw(CanvasControl sender, CanvasDrawingSession cds, ModtermRender mtd)
        {
            mtd.DrawModtermLabel(sender, cds, this);
        }
    }
}
