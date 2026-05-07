using Windows.UI;

namespace modterm
{
    public class ColorConfiguration
    {
        public required string Name { get; set; }
        public Color OutputColor { get; set; }
        public Color OutputBlurColor { get; set; }
        public Color ControlColor { get; set; }
        public Color ControlBlurColor { get; set; }
        public float BlurAmount { get; set; }
        public int WindowOpacityPct { get; set; }
        public Color WindowColor { get; set; }
        public BackdropKind BackdropKind { get; set; }
        public Color ControlEngagedColor { get; set; }
        public Color ControlEngagedHoverColor { get; set; }
    }
}
