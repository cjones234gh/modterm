# modterm

This is a GPU accelerated terminal emulator for Windows 11, running powershell, wsl, git-bash, cmd, etc.. It features VT emulation, full color support, and a configurable glass-like UI built on WinUI and Win2D. It uses XtermSharp for VT emulation and Windows ConPTY for pty support. It has an easy to use configuration and theming GUI launched from the context menu.

# Releases

[0.8.2-alpha](https://github.com/cjones234gh/modterm/releases/tag/v0.8.2-alpha) - Link to a binary installer, if you don't want to build from source.

# To Build from Source

Clone this repo, and then also clone https://github.com/cjones234gh/XtermSharp next to it. Modterm has a project reference to XtermSharp.

Build modterm in Visual Studio with F5, or Cursor AI with F5.

# Known Issues

* Running different TUI apps often requires running `reset` before launch in git-bash, WSL, and Powershell environments for correct rendering, for now.

* Color is currently limited to a 256 color palette, RGB values are translated to the nearest palette entry.






