using Windows.Foundation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace modterm
{
    public class UserAppConfiguration
    {
        public Rect WindowLocation { get; set; }
        public string TerminalFont { get; set; } = string.Empty;
        public string TerminalControlFont { get; set; } = string.Empty;
        public float TerminalFontSize { get; set; }
        public Shell TerminalShell { get; set; } = null!;
        public string TerminalCursor { get; set; } = string.Empty;

        public string ColorConfiguration { get; set; } = string.Empty;
    }
}
