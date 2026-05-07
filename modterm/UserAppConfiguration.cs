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
        public string TerminalFont { get; set; }
        public string TerminalControlFont { get; set; }
        public float TerminalFontSize { get; set; }
        public Shell TerminalShell { get; set; }
        public string TerminalCursor { get; set; }
    }
}
