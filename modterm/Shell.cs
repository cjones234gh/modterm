using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace modterm
{
    public enum ConPtyLaunchMode
    {
        // Current reliable mode in modterm: explicit std handles + inherited pipe handles.
        CompatiblePipes = 0,
        // Debug mode: ConPTY attribute only (no explicit std handles, no generic handle inheritance).
        PseudoConsoleOnly = 1
    }

    public class Shell
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string Arguments { get; set; } = string.Empty;
        public ConPtyLaunchMode LaunchMode { get; set; } = ConPtyLaunchMode.CompatiblePipes;
    }
}
