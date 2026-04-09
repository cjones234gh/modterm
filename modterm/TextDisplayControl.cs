using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Composition;
using Windows.Foundation;

namespace modterm
{
    public class TextDisplayControl : IModtermControl
    {
        public bool IsEngaged { get; set; }
        public bool IsHovered { get; set; }
        public bool IsPressed { get; set; }
        public Rect Location { get; set; }
        public string TextContent { get; set; }
        public bool ContentSizing { get; set; } = true; // control size is based on TextContent

        public TextDisplayControl(Rect location, string textContent)
        {
            Location = location;
            TextContent = textContent;
        }

        public void Draw(CanvasControl sender, CanvasDrawingSession cds, ModtermDisplay modtermDisplay)
        {
            modtermDisplay.DrawTextDisplayControl(sender, cds, this);
        }
    }
}
