using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Composition;
using Windows.Foundation;

namespace modterm
{
    public class TextDisplayControl : ModtermControl
    {
        public TextDisplayControl(string textContent, bool interactive, bool contentSizing = true, Rect location = new Rect())
        {
            Location = location;
            TextContent = textContent;
            Interactive = interactive;
            ContentSizing = contentSizing;
        }

        public override void Draw(CanvasControl sender, CanvasDrawingSession cds, ModtermDisplay modtermDisplay)
        {
            modtermDisplay.DrawTextDisplayControl(sender, cds, this);
        }

        
    }
}
