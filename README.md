# modterm

This is a GPU accelerated terminal emulator for Windows 11, running powershell, wsl, git-bash, cmd, etc.. It features VT emulation, full color support, and a configurable glass-like UI built on WinUI and Win2D. It uses XtermSharp for VT emulation and Windows ConPTY for pty support.

# Releases

Coming Soon

# To Build from Source

Clone this repo, and then also clone https://github.com/cjones234gh/XtermSharp next to it. Modterm has a project reference to XtermSharp.

Build modterm in Visual Studio with F5, or Cursor AI with F5.

# Known Issues

BlexMono is currently the default font, but not embedded yet. (You can choose a new font by launching the config editor, right-click for context, select Launch...Editor)

Running TUI apps often requires running 'reset' in git-bash and WSL environments for correct rendering. Occasional TUI app mis-renderings occur with border characters, and sometimes with scrambled line ordering as is the case with 'gitui'.

Configuration and Theme editor works, but to save an existing theme you must "Save as New" and then use its current name to overwrite it.

Copy/Paste works, but mouse clicks aren't translated in any way to to the pty.

No support for tiling or anything yet, but that will come.





