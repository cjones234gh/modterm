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
        public string ShellPath { get; private set; } = string.Empty;
        public bool Started { get; set; } = false;
        public bool IsDisposed => _disposed;

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
        private IntPtr          _jobHandle = IntPtr.Zero;
        private bool            _disposeRequested;
        private int             _exitNotified;

        public event EventHandler<byte[]>? OutputReceived;
        public event EventHandler? TerminalExited;

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
        }

        public void Start(Shell targetShell, int lines, int columns)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ConPTYTerminal));

            int rows = lines;
            int cols = columns;

            try
            {
                // ConPTY-facing pipe ends must be inheritable (MS ConPTY sample). Host-side ends are not.
                IntPtr saPtr = Marshal.AllocHGlobal(Marshal.SizeOf<SECURITY_ATTRIBUTES>());
                try
                {
                    var sa = new SECURITY_ATTRIBUTES
                    {
                        nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES>(),
                        lpSecurityDescriptor = IntPtr.Zero,
                        bInheritHandle = true
                    };
                    Marshal.StructureToPtr(sa, saPtr, false);

                    // Pipe for child's STDOUT/STDERR (we read from _outputRead; ConPTY uses _outputWrite)
                    CreatePipe(out _outputRead, out _outputWrite, saPtr, 0);
                    if (!SetHandleInformation(_outputRead, HANDLE_FLAG_INHERIT, 0))
                        throw new Exception("Stdout SetHandleInformation");

                    // Pipe for child's STDIN (ConPTY uses _inputRead; we write to _inputWrite)
                    CreatePipe(out _inputRead, out _inputWrite, saPtr, 0);
                    if (!SetHandleInformation(_inputWrite, HANDLE_FLAG_INHERIT, 0))
                        throw new Exception("Stdin SetHandleInformation");
                }
                finally
                {
                    Marshal.FreeHGlobal(saPtr);
                }

                // Create pseudo console at clamped size (caller may pass pre-layout 1x1 from canvas math).
                var coord = new COORD { X = (short)cols, Y = (short)rows };
                if (CreatePseudoConsole(coord, _inputRead, _outputWrite, 0, out _hPC) != 0)
                    throw new Exception("CreatePseudoConsole failed");

                // prepare process data with attribute list that includes the HPCON handle,
                // so child process is attached to it
                IntPtr attrListSize = IntPtr.Zero;
                InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref attrListSize);
                _attrListPtr = Marshal.AllocHGlobal(attrListSize);

                // lpValue is the HPCON handle itself (MS / ConPtyShell pattern), not a pointer to it.
                _hPCPtr = IntPtr.Zero;

                // zero out the PI
                var pi = default(PROCESS_INFORMATION);

                // zero out and then set STARTUPINFOEX fields;
                var startupInfo = default(STARTUPINFOEX);
                startupInfo.StartupInfo.cb = Marshal.SizeOf<STARTUPINFOEX>();
                startupInfo.lpAttributeList = _attrListPtr;

                // Do not set STARTF_USESTDHANDLES. With PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE the
                // child's console I/O is supplied by the pseudoconsole; forcing null std handles
                // prevents the shell from ever writing to our ConPTY output pipe (runtime proven).

                if (!InitializeProcThreadAttributeList(startupInfo.lpAttributeList, 1, 0, ref attrListSize))
                    throw new Exception("InitializeProcThreadAttributeList failed");

                if (!UpdateProcThreadAttribute(startupInfo.lpAttributeList, 0, (IntPtr)PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE,
                    _hPC, (IntPtr)IntPtr.Size, IntPtr.Zero, IntPtr.Zero))
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
                envVars["LINES"] = rows.ToString();
                envVars["COLUMNS"] = cols.ToString();

                bool usesUnixLocale = UsesUnixLocaleShell(targetShell);
                if (usesUnixLocale)
                {
                    envVars["LANG"] = "en_US.UTF-8";
                    envVars["LC_ALL"] = "en_US.UTF-8";
                }

                // wsl.exe -> Linux: only vars listed in WSLENV are forwarded; merge so TERM_PROGRAM / dimensions reach bash.
                if (string.Equals(targetShell.Name, "wsl", StringComparison.OrdinalIgnoreCase)
                    || targetShell.Path.EndsWith("wsl.exe", StringComparison.OrdinalIgnoreCase))
                    MergeWslInteropEnv(envVars);

                IntPtr envBlockPtr = BuildEnvironmentBlock(envVars);
                IntPtr commandLinePtr = Marshal.StringToHGlobalUni(commandLine);

                uint creationFlags = EXTENDED_STARTUPINFO_PRESENT | CREATE_UNICODE_ENVIRONMENT;
                IntPtr envPtrForCreateProcess = envBlockPtr;
                IntPtr currentDirectory = IntPtr.Zero;
                // The pseudoconsole is passed via the attribute list (not handle inheritance),
                // so the canonical ConPTY pattern creates the process with inheritance disabled.
                bool inheritHandles = false;

                // P/Invoke requires a null in this call, not a null string, so we have to disable nullable warnings for this section
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
                try
                {
                    // Create the process with the extended startup info.
                    if (!CreateProcessW(IntPtr.Zero, commandLinePtr, IntPtr.Zero, IntPtr.Zero, inheritHandles,
                            creationFlags, envPtrForCreateProcess, currentDirectory, ref startupInfo, out pi))
                    {
                        int error = Marshal.GetLastWin32Error();
                        throw new Exception($"CreateProcess failed with error code: {error}");
                    }

                    DeleteProcThreadAttributeList(startupInfo.lpAttributeList);
                    Marshal.FreeHGlobal(_attrListPtr);
                    _attrListPtr = IntPtr.Zero;
                }
                finally
                {
                    Marshal.FreeHGlobal(envBlockPtr);
                    Marshal.FreeHGlobal(commandLinePtr);
                }
#pragma warning restore CS8625 // Cannot convert null literal to non-nullable reference type.

                try
                {
                    TryAssignProcessToKillOnCloseJob(pi.hProcess);

                    // get the process by PID so we can monitor/end it later;
                    try
                    {
                        _process = Process.GetProcessById(pi.dwProcessId);
                        _process.EnableRaisingEvents = true;
                        _process.Exited += OnProcessExited;
                        Debug.WriteLine($"Child process {pi.dwProcessId} attached.");
                    }
                    catch (ArgumentException ex)
                    {
                        Started = false;
                        Debug.WriteLine($"Child process {pi.dwProcessId} exited before attach.");
                        throw new InvalidOperationException($"Child process {pi.dwProcessId} exited before attach.", ex);
                    }
                }
                finally
                {
                    if (pi.hThread != IntPtr.Zero)
                        CloseHandle(pi.hThread);
                    if (pi.hProcess != IntPtr.Zero)
                        CloseHandle(pi.hProcess);

                    // The pseudoconsole/child now owns these ends. Keeping them open in the parent
                    // prevents EOF/broken-pipe detection when the shell exits.
                    _inputRead?.Dispose();
                    _outputWrite?.Dispose();
                }

                Started = true;
                Debug.WriteLine($"Started process with PID: {pi.dwProcessId}");

                // Start async reader
                _readTask = Task.Run(ReadOutputLoop);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        /// <summary>
        /// Append <c>WSLENV</c> entries so the Linux side of WSL sees the same terminal-related vars as Windows.
        /// </summary>
        private static void MergeWslInteropEnv(Dictionary<string, string> envVars)
        {
            // /u = available in WSL (Unix-style). Colon-delimited list.
            // https://learn.microsoft.com/en-us/windows/wsl/interop#share-environment-variables-between-windows-and-wsl-with-wslenv
            const string block = "TERM/u:COLORTERM/u:TERM_PROGRAM/u:FORCE_COLOR/u:LINES/u:COLUMNS/u:LANG/u";
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

        private static bool UsesUnixLocaleShell(Shell shell)
        {
            if (string.Equals(shell.Name, "wsl", StringComparison.OrdinalIgnoreCase)
                || shell.Path.EndsWith("wsl.exe", StringComparison.OrdinalIgnoreCase))
                return true;

            return shell.Path.EndsWith("bash.exe", StringComparison.OrdinalIgnoreCase);
        }

        private static IntPtr BuildEnvironmentBlock(Dictionary<string, string> envVars)
        {
            var sb = new StringBuilder();
            foreach (var kv in envVars.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
            {
                sb.Append(kv.Key);
                sb.Append('=');
                sb.Append(kv.Value);
                sb.Append('\0');
            }
            sb.Append('\0');
            return Marshal.StringToHGlobalUni(sb.ToString());
        }

        private string GetAnsiRGB(Color c)
        {
            return c.R.ToString() + ";" + c.G.ToString() + ";" + c.B.ToString() + "m";
        }
                
        public void WriteInput(string text)
        {
            bool closed = _inputWrite == null || _inputWrite.IsClosed;
            bool ok = false;
            uint sent = 0;
            if (!closed)
            {
                var bytes = Encoding.UTF8.GetBytes(text);
                ok = WriteFile(_inputWrite, bytes, (uint)bytes.Length, out sent, IntPtr.Zero);
            }
        }

        public void Resize(short cols, short rows)
        {
            Debug.WriteLine("Resize requested: " + cols + " cols, " + rows + " rows");
            ResizePseudoConsole(_hPC, new COORD { X = cols, Y = rows });
        }

        private async Task ReadOutputLoop()
        {
            Debug.WriteLine("Started ConPTY output reader task.");
            int readAttempts = 0;
            int successfulReads = 0;

            var buffer = new byte[4096];
            while (!_disposed)
            {
                try
                {
                    readAttempts++;
                    if (_outputRead == null || _outputRead.IsClosed)
                    {
                        break;
                    }
                    bool ok = ReadFile(_outputRead, buffer, (uint)buffer.Length, out uint read, IntPtr.Zero);
                    if (!ok)
                    {
                        int err = Marshal.GetLastWin32Error();
                        if (readAttempts <= 5 || err == ERROR_BROKEN_PIPE || err == ERROR_NO_DATA)
                        {
                            bool procExited = false;
                            try { procExited = _process?.HasExited ?? false; } catch { }
                        }
                        if (err == ERROR_BROKEN_PIPE || err == ERROR_NO_DATA)
                            break;
                        await Task.Delay(50);
                        continue;
                    }
                    if (read == 0)
                    {
                        await Task.Delay(10);
                        continue;
                    }
                    successfulReads++;

                    // Pass raw bytes to the VT parser so multi-byte UTF-8 sequences split
                    // across reads are not corrupted by per-chunk string decoding.
                    var chunk = new byte[read];
                    Buffer.BlockCopy(buffer, 0, chunk, 0, (int)read);
                    OutputReceived?.Invoke(this, chunk);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"ReadOutputLoop error: {ex}");
                    // Don't rethrow: keep loop running so terminal can recover from one bad chunk
                }
                await Task.Delay(10);
            }

            Debug.WriteLine("ConPTY output reader loop exited");

            if (!_disposeRequested)
            {
                try
                {
                    if (_process == null || _process.HasExited)
                        NotifyTerminalExited();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"ReadOutputLoop exit notification failed: {ex}");
                }
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _disposeRequested = true;
            Started = false;

            if (_process != null)
                _process.Exited -= OnProcessExited;

            if (_hPC != IntPtr.Zero)
            {
                ClosePseudoConsole(_hPC);
                _hPC = IntPtr.Zero;
            }

            _inputRead?.Dispose();
            _inputWrite?.Dispose();
            _outputRead?.Dispose();
            _outputWrite?.Dispose();

            if (_jobHandle != IntPtr.Zero)
            {
                CloseHandle(_jobHandle);
                _jobHandle = IntPtr.Zero;
            }

            try
            {
                if (_process != null && !_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                    _process.WaitForExit(2000);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Process cleanup error: {ex}");
            }
            finally
            {
                _process?.Dispose();
                _process = null;
            }

            if (_attrListPtr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_attrListPtr);
                _attrListPtr = IntPtr.Zero;
            }
            if (_hPCPtr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_hPCPtr);
                _hPCPtr = IntPtr.Zero;
            }
        }

        private void OnProcessExited(object? sender, EventArgs e)
        {
            Started = false;
            if (_disposeRequested)
                return;

            NotifyTerminalExited();
        }

        private void NotifyTerminalExited()
        {
            if (Interlocked.Exchange(ref _exitNotified, 1) != 0)
                return;

            TerminalExited?.Invoke(this, EventArgs.Empty);
        }

        private void TryAssignProcessToKillOnCloseJob(IntPtr processHandle)
        {
            IntPtr jobHandle = CreateJobObject(IntPtr.Zero, null);
            if (jobHandle == IntPtr.Zero)
            {
                Debug.WriteLine($"CreateJobObject failed: {Marshal.GetLastWin32Error()}");
                return;
            }

            var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
            info.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
            int length = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
            IntPtr infoPtr = Marshal.AllocHGlobal(length);

            try
            {
                Marshal.StructureToPtr(info, infoPtr, false);

                if (!SetInformationJobObject(jobHandle, JobObjectExtendedLimitInformation, infoPtr, (uint)length))
                {
                    Debug.WriteLine($"SetInformationJobObject failed: {Marshal.GetLastWin32Error()}");
                    CloseHandle(jobHandle);
                    return;
                }

                if (!AssignProcessToJobObject(jobHandle, processHandle))
                {
                    Debug.WriteLine($"AssignProcessToJobObject failed: {Marshal.GetLastWin32Error()}");
                    CloseHandle(jobHandle);
                    return;
                }

                _jobHandle = jobHandle;
            }
            finally
            {
                Marshal.FreeHGlobal(infoPtr);
            }
        }

        // P/Invoke for Windows API access
        // Flags and structs for ConPTY and process/thread attributes
        private const uint EXTENDED_STARTUPINFO_PRESENT = 0x00080000;
        private const uint PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE = 0x00020016;
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
        private const int ERROR_BROKEN_PIPE = 109;
        private const int ERROR_NO_DATA = 232;
        private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000;
        private const uint TH32CS_SNAPPROCESS = 0x00000002;
        private const int JobObjectExtendedLimitInformation = 9;
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
        [StructLayout(LayoutKind.Sequential)] public struct SECURITY_ATTRIBUTES { public int nLength; public IntPtr lpSecurityDescriptor; [MarshalAs(UnmanagedType.Bool)] public bool bInheritHandle; }
        [StructLayout(LayoutKind.Sequential)] private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }
        [StructLayout(LayoutKind.Sequential)] private struct IO_COUNTERS
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }
        [StructLayout(LayoutKind.Sequential)] private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }

        // Windows API functions for ConPTY and process/thread management; see Windows API docs for details
        [DllImport("kernel32.dll", SetLastError = true)] private static extern void DeleteProcThreadAttributeList(IntPtr lpAttributeList);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CreatePipe(out SafeFileHandle hReadPipe, out SafeFileHandle hWritePipe, IntPtr lpPipeAttributes, uint nSize);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern int CreatePseudoConsole(COORD size, SafeFileHandle hInput, SafeFileHandle hOutput, uint dwFlags, out IntPtr phPC);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern int ResizePseudoConsole(IntPtr hPC, COORD size);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool ClosePseudoConsole(IntPtr hPC);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetInformationJobObject(IntPtr hJob, int JobObjectInfoClass, IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);
        [DllImport("kernel32.dll", EntryPoint = "CreateProcessW", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CreateProcessW(IntPtr lpApplicationName, IntPtr lpCommandLine, IntPtr lpProcessAttributes, IntPtr lpThreadAttributes, bool bInheritHandles, uint dwCreationFlags, IntPtr lpEnvironment, IntPtr lpCurrentDirectory, ref STARTUPINFOEX lpStartupInfo, out PROCESS_INFORMATION lpProcessInformation);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool InitializeProcThreadAttributeList(IntPtr lpAttributeList, int dwAttributeCount, uint dwFlags, ref IntPtr lpSize);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool UpdateProcThreadAttribute(IntPtr lpAttributeList, uint dwFlags, IntPtr Attribute, IntPtr lpValue, IntPtr cbSize, IntPtr lpPreviousValue, IntPtr lpReturnSize);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool ReadFile(SafeFileHandle hFile, byte[] lpBuffer, uint nNumberOfBytesToRead, out uint lpNumberOfBytesRead, IntPtr lpOverlapped);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetHandleInformation(SafeFileHandle hObject, uint dwMask, uint dwFlags);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetHandleInformation(SafeFileHandle hObject, out uint lpdwFlags);
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
