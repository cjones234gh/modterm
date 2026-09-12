namespace modterm
{
    /// <summary>
    /// A zero-based cell coordinate in buffer space (column, absolute row).
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
    /// A stream-based (not rectangular) selection span.
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
}
