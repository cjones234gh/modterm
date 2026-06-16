using XtermSharp;

namespace modterm
{
    /// <summary>
    /// A zero-based cell coordinate in buffer space (column, absolute row). Replaces
    /// VtNetCore's TextPosition, preserving its stream (reading-order) comparison semantics.
    /// </summary>
    public struct TextPosition
    {
        public int Column;
        public int Row;

        public static bool operator >(TextPosition l, TextPosition r)
            => l.Row > r.Row || (l.Row == r.Row && l.Column > r.Column);

        public static bool operator <(TextPosition l, TextPosition r)
            => r.Row > l.Row || (r.Row == l.Row && r.Column > l.Column);

        public static bool operator >=(TextPosition l, TextPosition r)
            => l.Row > r.Row || (l.Row == r.Row && l.Column >= r.Column);

        public static bool operator <=(TextPosition l, TextPosition r)
            => r.Row > l.Row || (r.Row == l.Row && r.Column >= l.Column);

        public bool Within(TextPosition start, TextPosition end)
        {
            if (start > end)
                return this >= end && this <= start;

            return this >= start && this <= end;
        }
    }

    /// <summary>
    /// A stream-based (not rectangular) selection span. Replaces VtNetCore's TextRange.
    /// </summary>
    public class TextRange
    {
        public TextPosition Start { get; set; }
        public TextPosition End { get; set; }

        public bool Contains(int column, int row)
        {
            return new TextPosition { Column = column, Row = row }.Within(Start, End);
        }
    }

    /// <summary>
    /// Decodes XtermSharp's packed cell attribute: ((int)flags &lt;&lt; 18) | (fg &lt;&lt; 9) | bg.
    /// fg/bg are 9-bit palette indices (0-255), with 256 = default and 257 = inverted-default.
    /// </summary>
    internal static class XtermAttr
    {
        public static void Decode(int attribute, out int fg, out int bg, out FLAGS flags)
        {
            bg = attribute & 0x1ff;
            fg = (attribute >> 9) & 0x1ff;
            flags = (FLAGS)(attribute >> 18);
        }

        public static bool IsDefault(int index)
            => index == Renderer.DefaultColor || index == Renderer.InvertedDefaultColor;
    }
}
