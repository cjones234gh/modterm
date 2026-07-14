# modterm

This is a GPU accelerated terminal emulator for Windows 11, running powershell, wsl, git-bash, cmd, etc.. It features VT emulation, full color support, and a configurable glass-like UI built on WinUI and Win2D. It uses XtermSharp for VT emulation and Windows ConPTY for pty support.

# Releases

Coming Soon

# To Build from Source

Clone this repo, and then also clone https://github.com/cjones234gh/XtermSharp next to it. Modterm has a project reference to XtermSharp.

Build modterm in Visual Studio with F5, or Cursor AI with F5.

# Known Issues

* Running TUI apps someimtes requires running `reset` before launch in git-bash and WSL environments for correct rendering.

* Copy/Paste works, but mouse clicks aren't translated in any way to to the pty.






