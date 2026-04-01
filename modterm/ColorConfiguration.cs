using Windows.UI;

namespace modterm
{
    public class ColorConfiguration
    {
        public required string Name { get; set; }
        public Color InputColor { get; set; }
        public Color OutputColor { get; set; }
        public Color InputGlowColor { get; set; }
        public Color OutputGlowColor { get; set; }
        public Color ControlColor { get; set; }
        public Color ControlGlowColor { get; set; }
        public float BlurAmount { get; set; }
        public int TransparencyPct { get; set; }
        public Color TintColor { get; set; }
        public Color ControlEngagedColor { get; set; }
        public Color ControlEngagedHoverColor { get; set; }
    }
}
