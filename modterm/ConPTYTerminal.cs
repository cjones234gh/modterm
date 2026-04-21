using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.Diagnostics;

using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Windows.UI;

namespace modterm
{
    public partial class ConPTYTerminal : IDisposable
    {
        /// <summary>Minimum grid size for CreatePseudoConsole / ResizePseudoConsole.</summary>
        public const int MinTerminalRows = 6;
        public const int MinTerminalColumns = 11;

        /// <summary>Defer <see cref="Start"/> until the host canvas has at least this layout size (DIP).</summary>
        public const double DeferredPtyMinCanvasWidth = 64;
        public const double DeferredPtyMinCanvasHeight = 64;

        /// <summary>
        /// ConPTY must not be created at 1×1 or other pre-layout sizes: a low-priority startup callback can run
        /// before the canvas has a real layout size, which bakes bad LINES/COLUMNS and breaks WSL/readline prompts.
        /// </summary>
        public static void ClampTerminalDimensions(ref int lines, ref int columns)
        {
            if (lines <= 0 || columns <= 0)
            {
                lines = 24;
                columns = 80;
                return;
            }

            lines = Math.Max(lines, MinTerminalRows);
            columns = Math.Max(columns, MinTerminalColumns);
        }

        public string ShellPath { get; private set; } = string.Empty;
        public bool Started { get; set; } = false;

        private IntPtr          _hPC;
        private IntPtr          _hPCPtr = IntPtr.Zero;
        private SafeFileHandle  _inputWrite;
        private SafeFileHandle  _inputRead;
        private SafeFileHandle  _outputWrite;
        private SafeFileHandle  _outputRead;
        private Process?        _process;
        private Task?           _readTask;
        private bool            _disposed;
        private IntPtr          _attrListPtr = IntPtr.Zero;
        private List<Color>     _bannerColors;
        private IntPtr          _conhostWindow = IntPtr.Zero;
        private int             _conhostProcessId = -1;
        private int             _conhostBaseWindowWidth = 0;
        private int             _conhostBaseWindowHeight = 0;
        private short           _conhostBaseCols = 0;
        private short           _conhostBaseRows = 0;
        private bool            _useConhostWindowResize = false;
        private int             _conhostWindowProcessId = -1;

        public event EventHandler<string>? OutputReceived;

        public int GetProcessId()
        {
            return _process?.Id ?? -1;
        }
        public ConPTYTerminal()
        {
            _inputWrite = new SafeFileHandle();
            _inputRead = new SafeFileHandle();
            _outputWrite = new SafeFileHandle();
            _outputRead = new SafeFileHandle();
            //_bannerColors = ModtermDisplay.GetColorWheelProgression(10f, 100, 7);
        }

        public void Start(Shell targetShell, int lines, int columns)
        {
            int rows = lines;
            int cols = columns;
            ClampTerminalDimensions(ref rows, ref cols);
            bool launchViaConhost = IsConhostShell(targetShell);

            // security attributes for the pipes: must allow handle inheritance for ConPTY to use them
            var sa = new SECURITY_ATTRIBUTES();
            sa.nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES>();
            sa.bInheritHandle = true;
            sa.lpSecurityDescriptor = IntPtr.Zero;
            IntPtr saPtr = Marshal.AllocHGlobal(Marshal.SizeOf<SECURITY_ATTRIBUTES>());
            Marshal.StructureToPtr(sa, saPtr, false);

            // Pipe for child's STDOUT/STDERR (we read from this)
            CreatePipe(out _outputRead, out _outputWrite, saPtr, 0);

            // Prevent read handle from being inherited
            if (!SetHandleInformation(_outputRead, HANDLE_FLAG_INHERIT, 0))
                throw new Exception("Stdout SetHandleInformation");

            // Pipe for child's STDIN (we write to this)
            CreatePipe(out _inputRead, out _inputWrite, saPtr, 0);

            // Prevent write handle from being inherited
            if (!SetHandleInformation(_inputWrite, HANDLE_FLAG_INHERIT, 0))
                throw new Exception("Stdin SetHandleInformation");

            // Free the security attributes struct
            Marshal.FreeHGlobal(saPtr);

            // Create pseudo console at clamped size (caller may pass pre-layout 1×1 from canvas math).
            var coord = new COORD { X = (short)cols, Y = (short)rows };
            if (CreatePseudoConsole(coord, _inputRead, _outputWrite, 0, out _hPC) != 0)
                throw new Exception("CreatePseudoConsole failed");

            // prepare process data with attribute list that includes the HPCON handle,
            // so child process is attached to it
            IntPtr attrListSize = IntPtr.Zero;
            InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref attrListSize);
            _attrListPtr = Marshal.AllocHGlobal(attrListSize);

            // lpValue must be a *pointer* to the HPCON (kernel reads the handle from that address),
            // not the handle value itself.
            _hPCPtr = Marshal.AllocHGlobal(IntPtr.Size);
            Marshal.WriteIntPtr(_hPCPtr, _hPC);

            // zero out the PI
            var pi = default(PROCESS_INFORMATION);

            // zero out and then set STARTUPINFOEX fields;
            var startupInfo = default(STARTUPINFOEX);
            startupInfo.StartupInfo.cb = Marshal.SizeOf<STARTUPINFOEX>();
            startupInfo.lpAttributeList = _attrListPtr;

            // std handle wiring with inherited pipe handles.
            startupInfo.StartupInfo.dwFlags = STARTF_USESTDHANDLES | STARTF_USESHOWWINDOW;
            startupInfo.StartupInfo.wShowWindow = (short)(launchViaConhost ? SW_SHOWNORMAL : SW_HIDE);
            startupInfo.StartupInfo.hStdInput = _inputRead.DangerousGetHandle();
            startupInfo.StartupInfo.hStdOutput = _outputWrite.DangerousGetHandle();
            startupInfo.StartupInfo.hStdError = _outputWrite.DangerousGetHandle();

            if (!InitializeProcThreadAttributeList(startupInfo.lpAttributeList, 1, 0, ref attrListSize))
                throw new Exception("InitializeProcThreadAttributeList failed");

            if (!UpdateProcThreadAttribute(startupInfo.lpAttributeList, 0, (IntPtr)PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE,
                _hPCPtr, (IntPtr)IntPtr.Size, IntPtr.Zero, IntPtr.Zero))
                throw new Exception("UpdateProcThreadAttribute failed");

            // Build full command line: application path + arguments
            string commandLine = string.IsNullOrEmpty(targetShell.Arguments)
                ? $"\"{targetShell.Path}\""
                : $"\"{targetShell.Path}\" {targetShell.Arguments}";

            ShellPath = commandLine;
            // swap the width and height placeholders with _lines and _columns in the commandLine
            commandLine = commandLine.Replace("[W]", cols.ToString()).Replace("[H]", rows.ToString());

            Debug.WriteLine($"Starting process with command line: {commandLine}");

            // Build environment block with TERM, LINES, and COLUMNS.
            // Keep key handling case-insensitive like Windows and avoid duplicate keys.
            var envVars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
            {
                string key = (string)entry.Key;
                string value = (string?)entry.Value ?? string.Empty;
                if (!envVars.ContainsKey(key))
                {
                    envVars[key] = value;
                }
            }

            envVars["TERM"] = "xterm-256color";
            envVars["COLORTERM"] = "truecolor";
            envVars["FORCE_COLOR"] = "true";
            envVars["TERM_PROGRAM"] = "modterm";

            // wsl.exe → Linux: only vars listed in WSLENV are forwarded; merge so TERM_PROGRAM / dimensions reach bash.
            if (string.Equals(targetShell.Name, "wsl", StringComparison.OrdinalIgnoreCase)
                || targetShell.Path.EndsWith("wsl.exe", StringComparison.OrdinalIgnoreCase))
                MergeWslInteropEnv(envVars);

            string envBlockStr = string.Join("\0", envVars.Select(kv => $"{kv.Key}={kv.Value}")) + "\0\0";
            IntPtr envBlockPtr = Marshal.StringToHGlobalUni(envBlockStr);
            IntPtr commandLinePtr = Marshal.StringToHGlobalUni(commandLine);

            uint creationFlags = EXTENDED_STARTUPINFO_PRESENT | CREATE_UNICODE_ENVIRONMENT;
            IntPtr envPtrForCreateProcess = envBlockPtr;
            IntPtr currentDirectory = IntPtr.Zero;
            bool inheritHandles = true;

            // P/Invoke requires a null in this call, not a null string, so we have to disable nullable warnings for this section
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
            // Create the process with the extended startup info.
            try
            {
                if (!CreateProcessW(IntPtr.Zero, commandLinePtr, IntPtr.Zero, IntPtr.Zero, inheritHandles,
                        creationFlags, envPtrForCreateProcess, currentDirectory, ref startupInfo, out pi))
                {
                    int error = Marshal.GetLastWin32Error();
                    throw new Exception($"CreateProcess failed with error code: {error}");
                }
            }
            finally
            {
                Marshal.FreeHGlobal(envBlockPtr);
                Marshal.FreeHGlobal(commandLinePtr);
            }

#pragma warning restore CS8625 // Cannot convert null literal to non-nullable reference type.

            // get the process by PID so we can monitor/end it later;
            _process = Process.GetProcessById(pi.dwProcessId);
            Started = true;
            Debug.WriteLine($"Started process with PID: {pi.dwProcessId}");
            ConfigureConhostWindowResizeIfNeeded(pi.dwProcessId, (short)cols, (short)rows, launchViaConhost);

            // Start async reader
            _readTask = Task.Run(ReadOutputLoop);


            //// show the banner (true ASCII art with 24-bit ANSI encoded color, because why not)
            //OutputReceived?.Invoke(this, "\x1B[38;2;" + GetAnsiRGB(_bannerColors[0]) + "                                                                                    ");
            //OutputReceived?.Invoke(this, "\x1B[38;2;" + GetAnsiRGB(_bannerColors[0]) + "                                  .o8      .                                        ");
            //OutputReceived?.Invoke(this, "\x1B[38;2;" + GetAnsiRGB(_bannerColors[1]) + "                                 \"888    .o8                                        ");
            //OutputReceived?.Invoke(this, "\x1B[38;2;" + GetAnsiRGB(_bannerColors[2]) + "ooo. .oo.  .oo.    .ooooo.   .oooo888  .o888oo  .ooooo.  oooo d8b ooo. .oo.  .oo.   ");
            //OutputReceived?.Invoke(this, "\x1B[38;2;" + GetAnsiRGB(_bannerColors[3]) + "`888P\"Y88bP\"Y88b  d88' `88b d88' `888    888   d88' `88b `888\"\"8P `888P\"Y88bP\"Y88b  ");
            //OutputReceived?.Invoke(this, "\x1B[38;2;" + GetAnsiRGB(_bannerColors[4]) + " 888   888   888  888   888 888   888    888   888ooo888  888      888   888   888  ");
            //OutputReceived?.Invoke(this, "\x1B[38;2;" + GetAnsiRGB(_bannerColors[5]) + " 888   888   888  888   888 888   888    888 . 888    .o  888      888   888   888  ");
            //OutputReceived?.Invoke(this, "\x1B[38;2;" + GetAnsiRGB(_bannerColors[6]) + "o888o o888o o888o `Y8bod8P' `Y8bod88P\"   \"888\" `Y8bod8P' d888b    o888o o888o o888o \x1B[0m");
            //OutputReceived?.Invoke(this, "\x1B[0m\n\x1B[0m\n\x1B[0m\n");
        }

        /// <summary>
        /// Append <c>WSLENV</c> entries so the Linux side of WSL sees the same terminal-related vars as Windows.
        /// </summary>
        private static void MergeWslInteropEnv(Dictionary<string, string> envVars)
        {
            // /u = available in WSL (Unix-style). Colon-delimited list.
            // https://learn.microsoft.com/en-us/windows/wsl/interop#share-environment-variables-between-windows-and-wsl-with-wslenv
            const string block = "TERM/u:COLORTERM/u:TERM_PROGRAM/u:FORCE_COLOR/u:LINES/u:COLUMNS/u";
            if (envVars.TryGetValue("WSLENV", out var existing) && !string.IsNullOrWhiteSpace(existing))
            {
                string trimmed = existing.TrimEnd(':', ';');
                envVars["WSLENV"] = string.IsNullOrEmpty(trimmed) ? block : $"{trimmed}:{block}";
            }
            else
            {
                envVars["WSLENV"] = block;
            }
        }

        private string GetAnsiRGB(Color c)
        {
            //  \x1B[38;2;+GetAnsiRGB(<color>)
            //  \x1B[0m
            return c.R.ToString() + ";" + c.G.ToString() + ";" + c.B.ToString() + "m";
        }
                
        public void WriteInput(string text)
        {
            if (_inputWrite != null && !_inputWrite.IsClosed)
            {
                var bytes = Encoding.UTF8.GetBytes(text);
                WriteFile(_inputWrite, bytes, (uint)bytes.Length, out uint sent, IntPtr.Zero);
            }
        }

        public void Resize(short cols, short rows)
        {
            int r = rows;
            int c = cols;
            ClampTerminalDimensions(ref r, ref c);
            if (TryResizeConhostWindow((short)c, (short)r))
            {
                return;
            }

            Debug.WriteLine($"Resizing ConPTY to {c} cols x {r} rows (requested {cols}×{rows})");
            if (_hPC == IntPtr.Zero)
                return;
            int hr = ResizePseudoConsole(_hPC, new COORD { X = (short)c, Y = (short)r });
            if (hr != 0)
            {
                Debug.WriteLine($"ResizePseudoConsole failed with HRESULT: 0x{hr:X8}");
            }
        }

        private static bool IsConhostShell(Shell targetShell)
        {
            if (string.IsNullOrWhiteSpace(targetShell.Path))
                return false;
            string path = targetShell.Path.Trim().Trim('"');
            return path.Equals("conhost", StringComparison.OrdinalIgnoreCase)
                || path.Equals("conhost.exe", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith("\\conhost.exe", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith("/conhost.exe", StringComparison.OrdinalIgnoreCase);
        }

        private void ConfigureConhostWindowResizeIfNeeded(int processId, short cols, short rows, bool launchViaConhost)
        {
            _useConhostWindowResize = launchViaConhost;
            if (!launchViaConhost)
            {
                _conhostProcessId = -1;
                _conhostWindow = IntPtr.Zero;
                _conhostBaseWindowWidth = 0;
                _conhostBaseWindowHeight = 0;
                _conhostBaseCols = 0;
                _conhostBaseRows = 0;
                return;
            }

            _conhostProcessId = processId;
            _conhostBaseCols = cols;
            _conhostBaseRows = rows;

            _ = Task.Run(() =>
            {
                IntPtr window = WaitForTopLevelWindow(processId, 7000, out int matchedWindowPid);
                if (window == IntPtr.Zero)
                {
                    Debug.WriteLine($"Could not find conhost window for PID {processId}.");
                    return;
                }

                _conhostWindow = window;
                _conhostWindowProcessId = matchedWindowPid;
                if (GetWindowRect(window, out RECT rect))
                {
                    _conhostBaseWindowWidth = Math.Max(1, rect.Right - rect.Left);
                    _conhostBaseWindowHeight = Math.Max(1, rect.Bottom - rect.Top);
                    Debug.WriteLine($"Bound conhost hwnd 0x{window.ToInt64():X} ({_conhostBaseWindowWidth}x{_conhostBaseWindowHeight}) from PID {matchedWindowPid} (root PID {processId}).");
                }
            });
        }

        private bool TryResizeConhostWindow(short cols, short rows)
        {
            if (!_useConhostWindowResize || _conhostProcessId <= 0)
                return false;

            if (_conhostWindow == IntPtr.Zero || !IsWindow(_conhostWindow))
            {
                _conhostWindow = WaitForTopLevelWindow(_conhostProcessId, 250, out int matchedWindowPid);
                _conhostWindowProcessId = matchedWindowPid;
                if (_conhostWindow == IntPtr.Zero)
                    return false;
            }

            if ((_conhostBaseWindowWidth <= 0 || _conhostBaseWindowHeight <= 0) && GetWindowRect(_conhostWindow, out RECT initialRect))
            {
                _conhostBaseWindowWidth = Math.Max(1, initialRect.Right - initialRect.Left);
                _conhostBaseWindowHeight = Math.Max(1, initialRect.Bottom - initialRect.Top);
            }

            if (_conhostBaseWindowWidth <= 0 || _conhostBaseWindowHeight <= 0 || _conhostBaseCols <= 0 || _conhostBaseRows <= 0)
                return false;

            if (!GetWindowRect(_conhostWindow, out RECT currentRect))
                return false;

            double xScale = (double)cols / _conhostBaseCols;
            double yScale = (double)rows / _conhostBaseRows;

            int targetWidth = Math.Max(100, (int)Math.Round(_conhostBaseWindowWidth * xScale));
            int targetHeight = Math.Max(100, (int)Math.Round(_conhostBaseWindowHeight * yScale));
            Debug.WriteLine($"Conhost resize request hwnd=0x{_conhostWindow.ToInt64():X} ownerPid={_conhostWindowProcessId} rootPid={_conhostProcessId} current={currentRect.Right - currentRect.Left}x{currentRect.Bottom - currentRect.Top} target={targetWidth}x{targetHeight} grid={cols}x{rows}.");

            var moveSw = Stopwatch.StartNew();
            bool moved = SetWindowPos(_conhostWindow, IntPtr.Zero, currentRect.Left, currentRect.Top, targetWidth, targetHeight,
                SWP_NOZORDER | SWP_NOACTIVATE | SWP_ASYNCWINDOWPOS);
            moveSw.Stop();
            if (moveSw.ElapsedMilliseconds > 100)
            {
                Debug.WriteLine($"Conhost SetWindowPos elapsed {moveSw.ElapsedMilliseconds}ms for hwnd 0x{_conhostWindow.ToInt64():X}.");
            }

            if (!moved)
            {
                int error = Marshal.GetLastWin32Error();
                Debug.WriteLine($"SetWindowPos failed for conhost hwnd 0x{_conhostWindow.ToInt64():X} with Win32 error {error}; falling back to MoveWindow.");
                moved = MoveWindow(_conhostWindow, currentRect.Left, currentRect.Top, targetWidth, targetHeight, false);
            }

            if (!moved)
            {
                int error = Marshal.GetLastWin32Error();
                Debug.WriteLine($"MoveWindow fallback failed for conhost hwnd 0x{_conhostWindow.ToInt64():X} with Win32 error {error}.");
                return false;
            }

            Debug.WriteLine($"Resized conhost hwnd 0x{_conhostWindow.ToInt64():X} to {targetWidth}x{targetHeight} for grid {cols}x{rows}.");
            return true;
        }

        private static IntPtr WaitForTopLevelWindow(int rootProcessId, int timeoutMs, out int matchedPid)
        {
            var sw = Stopwatch.StartNew();
            long lastProgressLogMs = -1000;
            matchedPid = -1;
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                IntPtr window = FindTopLevelWindowForProcessTree(rootProcessId, out matchedPid);
                if (window != IntPtr.Zero)
                {
                    LogTopLevelWindowsForProcessTree(rootProcessId, "window matched");
                    return window;
                }

                if (sw.ElapsedMilliseconds - lastProgressLogMs >= 1000)
                {
                    lastProgressLogMs = sw.ElapsedMilliseconds;
                    LogTopLevelWindowsForProcessTree(rootProcessId, $"waiting ({sw.ElapsedMilliseconds}ms)");
                }

                Thread.Sleep(50);
            }

            LogTopLevelWindowsForProcessTree(rootProcessId, $"timeout after {timeoutMs}ms");
            return IntPtr.Zero;
        }

        private static IntPtr FindTopLevelWindowForProcessTree(int rootProcessId, out int matchedPid)
        {
            var processTree = GetProcessTreeProcessIds(rootProcessId);
            return FindTopLevelWindowForProcessSet(processTree, rootProcessId, out matchedPid);
        }

        private static IntPtr FindTopLevelWindowForProcessSet(HashSet<int> processIds, int rootProcessId, out int matchedPid)
        {
            IntPtr bestWindow = IntPtr.Zero;
            int bestScore = int.MinValue;
            int bestPid = -1;
            EnumWindows((hWnd, _) =>
            {
                GetWindowThreadProcessId(hWnd, out uint windowPid);
                int pid = unchecked((int)windowPid);
                if (!processIds.Contains(pid))
                    return true;

                bool visible = IsWindowVisible(hWnd);
                string className = GetWindowClassName(hWnd);
                string title = GetWindowTitle(hWnd);
                int score = ScoreWindowCandidate(pid, rootProcessId, visible, className, title);

                if (score > bestScore)
                {
                    bestScore = score;
                    bestWindow = hWnd;
                    bestPid = pid;
                }

                return true;
            }, IntPtr.Zero);

            matchedPid = bestPid;
            return bestWindow;
        }

        private static void LogTopLevelWindowsForProcessTree(int rootProcessId, string phase)
        {
            var processTree = GetProcessTreeProcessIds(rootProcessId);
            List<string> matches = new List<string>();
            EnumWindows((hWnd, _) =>
            {
                GetWindowThreadProcessId(hWnd, out uint windowPid);
                int pid = unchecked((int)windowPid);
                if (!processTree.Contains(pid))
                    return true;

                bool visible = IsWindowVisible(hWnd);
                string className = GetWindowClassName(hWnd);
                string title = GetWindowTitle(hWnd);
                int score = ScoreWindowCandidate(pid, rootProcessId, visible, className, title);
                matches.Add($"0x{hWnd.ToInt64():X} pid={pid} visible={visible} score={score} class='{className}' title='{title}'");
                return true;
            }, IntPtr.Zero);

            if (matches.Count == 0)
            {
                Debug.WriteLine($"Conhost window probe [{phase}] rootPid={rootProcessId} tree=[{string.Join(", ", processTree.OrderBy(x => x))}]: no top-level windows found.");
                return;
            }

            Debug.WriteLine($"Conhost window probe [{phase}] rootPid={rootProcessId} tree=[{string.Join(", ", processTree.OrderBy(x => x))}]: {matches.Count} candidate(s).");
            foreach (string entry in matches)
            {
                Debug.WriteLine($"  {entry}");
            }
        }

        private static int ScoreWindowCandidate(int ownerPid, int rootProcessId, bool visible, string className, string title)
        {
            int score = 0;
            if (visible) score += 100;
            if (ownerPid != rootProcessId) score += 20;
            if (!string.IsNullOrWhiteSpace(title)) score += 10;
            if (className.Equals("ConsoleWindowClass", StringComparison.OrdinalIgnoreCase)) score += 40;
            if (className.Equals("CASCADIA_HOSTING_WINDOW_CLASS", StringComparison.OrdinalIgnoreCase)) score += 40;
            if (className.Equals("PseudoConsoleWindow", StringComparison.OrdinalIgnoreCase)) score -= 200;
            if (className.Equals("IME", StringComparison.OrdinalIgnoreCase)) score -= 120;
            if (className.Contains("MSCTFIME", StringComparison.OrdinalIgnoreCase)) score -= 120;
            if (!visible) score -= 20;
            return score;
        }

        private static HashSet<int> GetProcessTreeProcessIds(int rootProcessId)
        {
            var result = new HashSet<int> { rootProcessId };
            var childrenByParent = new Dictionary<int, List<int>>();
            IntPtr snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
            if (snapshot == INVALID_HANDLE_VALUE || snapshot == IntPtr.Zero)
                return result;

            try
            {
                PROCESSENTRY32 pe32 = new PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32>() };
                bool ok = Process32FirstW(snapshot, ref pe32);
                while (ok)
                {
                    int parent = unchecked((int)pe32.th32ParentProcessID);
                    int pid = unchecked((int)pe32.th32ProcessID);
                    if (!childrenByParent.TryGetValue(parent, out List<int>? children))
                    {
                        children = new List<int>();
                        childrenByParent[parent] = children;
                    }
                    children.Add(pid);
                    ok = Process32NextW(snapshot, ref pe32);
                }
            }
            finally
            {
                CloseHandle(snapshot);
            }

            var queue = new Queue<int>();
            queue.Enqueue(rootProcessId);
            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                if (!childrenByParent.TryGetValue(current, out List<int>? children))
                    continue;

                foreach (int childPid in children)
                {
                    if (!result.Add(childPid))
                        continue;
                    queue.Enqueue(childPid);
                }
            }

            return result;
        }

        private static string GetWindowClassName(IntPtr hWnd)
        {
            var sb = new StringBuilder(256);
            int len = GetClassName(hWnd, sb, sb.Capacity);
            return len > 0 ? sb.ToString() : string.Empty;
        }

        private static string GetWindowTitle(IntPtr hWnd)
        {
            int len = GetWindowTextLength(hWnd);
            if (len <= 0)
                return string.Empty;
            var sb = new StringBuilder(len + 1);
            return GetWindowText(hWnd, sb, sb.Capacity) > 0 ? sb.ToString() : string.Empty;
        }

        // Use UTF-8 decoder that replaces invalid bytes instead of throwing (ConPTY may send OEM/code-page or binary)
        private static readonly Encoding Utf8Relaxed = Encoding.GetEncoding(65001,
            EncoderFallback.ReplacementFallback, DecoderFallback.ReplacementFallback);

        private async Task ReadOutputLoop()
        {
            Debug.WriteLine("Started ConPTY output reader task.");

            var buffer = new byte[4096];
            while (!_disposed)
            {
                try
                {
                    if (_outputRead == null || _outputRead.IsClosed)
                        break;
                    bool ok = ReadFile(_outputRead, buffer, (uint)buffer.Length, out uint read, IntPtr.Zero);
                    if (!ok)
                    {
                        int err = Marshal.GetLastWin32Error();
                        // ERROR_BROKEN_PIPE (233) = other end closed; ERROR_NO_DATA (232) = pipe empty with no writers
                        if (err == 233 || err == 232)
                            break;
                        await Task.Delay(50);
                        continue;
                    }
                    if (read == 0)
                    {
                        await Task.Delay(10);
                        continue;
                    }
                    // Decode without throwing on invalid UTF-8 (e.g. OEM/code-page or binary from console)
                    string text = Utf8Relaxed.GetString(buffer, 0, (int)read);
                    OutputReceived?.Invoke(this, text);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"ReadOutputLoop error: {ex}");
                    // Don't rethrow: keep loop running so terminal can recover from one bad chunk
                }
                await Task.Delay(10);
            }
            Debug.WriteLine("ConPTY output reader loop exited");
        }

        public void Dispose()
        {
            _disposed = true;
            _process?.Kill();
            ClosePseudoConsole(_hPC);
            _inputRead?.Dispose();
            _inputWrite?.Dispose();
            _outputRead?.Dispose();
            _outputWrite?.Dispose();
            if (_attrListPtr != IntPtr.Zero)
            {
                DeleteProcThreadAttributeList(_attrListPtr);
                Marshal.FreeHGlobal(_attrListPtr);
                _attrListPtr = IntPtr.Zero;
            }
            if (_hPCPtr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_hPCPtr);
                _hPCPtr = IntPtr.Zero;
            }
            _conhostWindow = IntPtr.Zero;
            _conhostProcessId = -1;
            _conhostBaseWindowWidth = 0;
            _conhostBaseWindowHeight = 0;
            _conhostBaseCols = 0;
            _conhostBaseRows = 0;
            _useConhostWindowResize = false;
            _conhostWindowProcessId = -1;
        }

        // P/Invoke for Windows API access
        // Flags and structs for ConPTY and process/thread attributes
        private const uint EXTENDED_STARTUPINFO_PRESENT = 0x00080000;
        private const uint PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE = 0x0002000A;
        private const uint CREATE_NO_WINDOW = 0x08000000;
        private const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;
        private const uint STARTF_USESTDHANDLES = 0x00000100;
        private const uint STARTF_USESHOWWINDOW = 0x00000001;
        private const uint SW_HIDE = 0;
        private const uint SW_SHOWNORMAL = 1;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_ASYNCWINDOWPOS = 0x4000;
        private const uint HANDLE_FLAG_INHERIT = 0x00000001;
        private const uint TH32CS_SNAPPROCESS = 0x00000002;
        private static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        [StructLayout(LayoutKind.Sequential)] private struct COORD { public short X; public short Y; }
        [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct PROCESSENTRY32
        {
            public uint dwSize;
            public uint cntUsage;
            public uint th32ProcessID;
            public IntPtr th32DefaultHeapID;
            public uint th32ModuleID;
            public uint cntThreads;
            public uint th32ParentProcessID;
            public int pcPriClassBase;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szExeFile;
        }
        [StructLayout(LayoutKind.Sequential)] private struct PROCESS_INFORMATION { public IntPtr hProcess; public IntPtr hThread; public int dwProcessId; public int dwThreadId; }
        [StructLayout(LayoutKind.Sequential)] private struct STARTUPINFOEX { public STARTUPINFO StartupInfo; public IntPtr lpAttributeList; }
        [StructLayout(LayoutKind.Sequential)] private struct STARTUPINFO { public int cb; public IntPtr lpReserved; public IntPtr lpDesktop; public IntPtr lpTitle; public uint dwX; public uint dwY; public uint dwXSize; public uint dwYSize; public uint dwXCountChars; public uint dwYCountChars; public uint dwFillAttribute; public uint dwFlags; public short wShowWindow; public short cbReserved2; public IntPtr lpReserved2; public IntPtr hStdInput; public IntPtr hStdOutput; public IntPtr hStdError; }
        [StructLayout(LayoutKind.Sequential)] public struct SECURITY_ATTRIBUTES { public int nLength; public IntPtr lpSecurityDescriptor; public bool bInheritHandle; }

        // Windows API functions for ConPTY and process/thread management; see Windows API docs for details
        [DllImport("kernel32.dll", SetLastError = true)] private static extern void DeleteProcThreadAttributeList(IntPtr lpAttributeList);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CreatePipe(out SafeFileHandle hReadPipe, out SafeFileHandle hWritePipe, IntPtr lpPipeAttributes, uint nSize);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern int CreatePseudoConsole(COORD size, SafeFileHandle hInput, SafeFileHandle hOutput, uint dwFlags, out IntPtr phPC);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern int ResizePseudoConsole(IntPtr hPC, COORD size);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool ClosePseudoConsole(IntPtr hPC);
        [DllImport("kernel32.dll", EntryPoint = "CreateProcessW", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CreateProcessW(IntPtr lpApplicationName, IntPtr lpCommandLine, IntPtr lpProcessAttributes, IntPtr lpThreadAttributes, bool bInheritHandles, uint dwCreationFlags, IntPtr lpEnvironment, IntPtr lpCurrentDirectory, ref STARTUPINFOEX lpStartupInfo, out PROCESS_INFORMATION lpProcessInformation);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool InitializeProcThreadAttributeList(IntPtr lpAttributeList, int dwAttributeCount, uint dwFlags, ref IntPtr lpSize);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool UpdateProcThreadAttribute(IntPtr lpAttributeList, uint dwFlags, IntPtr Attribute, IntPtr lpValue, IntPtr cbSize, IntPtr lpPreviousValue, IntPtr lpReturnSize);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool ReadFile(SafeFileHandle hFile, byte[] lpBuffer, uint nNumberOfBytesToRead, out uint lpNumberOfBytesRead, IntPtr lpOverlapped);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetHandleInformation(SafeFileHandle hObject, uint dwMask, uint dwFlags);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool WriteFile(SafeHandle hFile, byte[] lpBuffer, uint nNumberOfBytesToWrite, out uint lpNumberOfBytesWritten, IntPtr lpOverlapped);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool Process32FirstW(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool Process32NextW(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CloseHandle(IntPtr hObject);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
        [DllImport("user32.dll", SetLastError = true)] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool IsWindow(IntPtr hWnd);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
        [DllImport("user32.dll", SetLastError = true)] private static extern int GetWindowTextLength(IntPtr hWnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
    }
}
