using System.Text;
using XtermSharp;

namespace modterm
{
    /// <summary>
    /// Bridges the XtermSharp engine to modterm's ConPTY backend. XtermSharp routes
    /// terminal responses (device attributes, cursor reports, mouse events, bracketed
    /// paste wrappers) and title/resize notifications through this delegate.
    /// </summary>
    internal sealed class ModtermTerminalDelegate : ITerminalDelegate
    {
        private readonly ModtermWindow _window;

        public ModtermTerminalDelegate(ModtermWindow window)
        {
            _window = window;
        }

        public void Send(byte[] data)
        {
            if (data is not { Length: > 0 })
                return;

            _window.ConPtyTerminal?.WriteInput(Encoding.UTF8.GetString(data));
        }

        public void SetTerminalTitle(Terminal source, string title)
        {
            // modterm renders its own title bar labels; nothing to do here yet.
        }

        public void SetTerminalIconTitle(Terminal source, string title)
        {
        }

        public void SizeChanged(Terminal source)
        {
            // Terminal size is driven by canvas measurement, not by escape sequences.
        }

        public void ShowCursor(Terminal source)
        {
        }

        public string? WindowCommand(Terminal source, WindowManipulationCommand command, params int[] args)
        {
            return null;
        }

        public bool IsProcessTrusted()
        {
            return true;
        }
    }
}
