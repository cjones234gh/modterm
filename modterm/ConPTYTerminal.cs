using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.Diagnostics;

using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

using Windows.UI;
using Modglass;

namespace modterm
{
    public partial class ConPTYTerminal : IDisposable
    {
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
        private string          _applicationName;
        private string          _arguments;
        private List<Color>     _bannerColors;

        public event EventHandler<string>? OutputReceived;

        public ConPTYTerminal()
        {
            _applicationName = string.Empty; 
            _arguments = string.Empty;
            _inputWrite = new SafeFileHandle();
            _inputRead = new SafeFileHandle();
            _outputWrite = new SafeFileHandle();
            _outputRead = new SafeFileHandle();
            _bannerColors = ModglassDisplay.GetColorWheelProgression(10f, 100, 7);
        }

        public void Start(string applicationName, string arguments, int lines, int columns)
        {
            _applicationName = applicationName;
            _arguments = arguments;

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

            // Create pseudo console (initial 120x40); 
            var coord = new COORD { X = (short)columns, Y = (short)lines };
            if (CreatePseudoConsole(coord, _inputRead, _outputWrite, 0, out _hPC) != 0)
                throw new Exception("CreatePseudoConsole failed");

            // prepare process data with attribute list that includes the HPCON handle, so child process is attached to it
            IntPtr attrListSize = IntPtr.Zero;
            InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref attrListSize);
            _attrListPtr = Marshal.AllocHGlobal(attrListSize);

            // lpValue must be a *pointer* to the HPCON (kernel reads the handle from that address), not the handle value itself.
            _hPCPtr = Marshal.AllocHGlobal(IntPtr.Size);
            Marshal.WriteIntPtr(_hPCPtr, _hPC);

            // zero out the PI
            var pi = default(PROCESS_INFORMATION);

            // zero out and then set STARTUPINFOEX fields;
            var startupInfo = default(STARTUPINFOEX);
            startupInfo.StartupInfo.cb = Marshal.SizeOf<STARTUPINFOEX>();
            startupInfo.lpAttributeList = _attrListPtr;
            startupInfo.StartupInfo.dwFlags = 0;
            // use the configured handles for the child process so it connects
            // to the ConPTY pipes; this is required for the ConPTY to work
            startupInfo.StartupInfo.dwFlags |= STARTF_USESTDHANDLES;
            startupInfo.StartupInfo.hStdInput = _inputRead.DangerousGetHandle();
            startupInfo.StartupInfo.hStdOutput = _outputWrite.DangerousGetHandle();
            startupInfo.StartupInfo.hStdError = _outputWrite.DangerousGetHandle();

            if (!InitializeProcThreadAttributeList(startupInfo.lpAttributeList, 1, 0, ref attrListSize))
                throw new Exception("InitializeProcThreadAttributeList failed");

            if (!UpdateProcThreadAttribute(startupInfo.lpAttributeList, 0, (IntPtr)PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE,
                _hPCPtr, (IntPtr)IntPtr.Size, IntPtr.Zero, IntPtr.Zero))
                throw new Exception("UpdateProcThreadAttribute failed");

            // Build full command line: application path + arguments
            string commandLine = string.IsNullOrEmpty(_arguments)
                ? $"\"{_applicationName}\""
                : $"\"{_applicationName}\" {_arguments}";

            ShellPath = commandLine;

            Debug.WriteLine($"Starting process with command line: {commandLine}");

            // P/Invoke requires a null in this call, not a null string, so we have to disable nullable warnings for this section
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
            // Create the process with the extended startup info
            if (!CreateProcess(null, commandLine, (nint)null, (nint)null, true,
                EXTENDED_STARTUPINFO_PRESENT | CREATE_NO_WINDOW, (nint)null, null, ref startupInfo, out pi))
            {
                int error = Marshal.GetLastWin32Error();
                throw new Exception($"CreateProcess failed with error code: {error}");
            }
#pragma warning restore CS8625 // Cannot convert null literal to non-nullable reference type.

            // get the process by PID so we can monitor/end it later;
            _process = Process.GetProcessById(pi.dwProcessId);
            Started = true;
            Debug.WriteLine($"Started process with PID: {pi.dwProcessId}");

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

        private string GetAnsiRGB(Color c)
        {
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
            Debug.WriteLine($"Resizing ConPTY to {cols} cols x {rows} rows");
            if (_hPC != IntPtr.Zero && cols > 10 && rows > 5)
                ResizePseudoConsole(_hPC, new COORD { X = cols, Y = rows });
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
        }

        // P/Invoke for Windows API access
        // Flags and structs for ConPTY and process/thread attributes
        private const uint EXTENDED_STARTUPINFO_PRESENT = 0x00080000;
        private const uint PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE = 0x0002000A;
        private const uint CREATE_NO_WINDOW = 0x08000000;
        private const uint STARTF_USESTDHANDLES = 0x00000100;
        private const uint HANDLE_FLAG_INHERIT = 0x00000001;
        [StructLayout(LayoutKind.Sequential)] private struct COORD { public short X; public short Y; }
        [StructLayout(LayoutKind.Sequential)] private struct PROCESS_INFORMATION { public IntPtr hProcess; public IntPtr hThread; public int dwProcessId; public int dwThreadId; }
        [StructLayout(LayoutKind.Sequential)] private struct STARTUPINFOEX { public STARTUPINFO StartupInfo; public IntPtr lpAttributeList; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct STARTUPINFO { public int cb; public string lpReserved; public string lpDesktop; public string lpTitle; public uint dwX; public uint dwY; public uint dwXSize; public uint dwYSize; public uint dwXCountChars; public uint dwYCountChars; public uint dwFillAttribute; public uint dwFlags; public short wShowWindow; public short cbReserved2; public IntPtr lpReserved2; public IntPtr hStdInput; public IntPtr hStdOutput; public IntPtr hStdError; }
        [StructLayout(LayoutKind.Sequential)] public struct SECURITY_ATTRIBUTES { public int nLength; public IntPtr lpSecurityDescriptor; public bool bInheritHandle; }

        // Windows API functions for ConPTY and process/thread management; see Windows API docs for details
        [DllImport("kernel32.dll", SetLastError = true)] private static extern void DeleteProcThreadAttributeList(IntPtr lpAttributeList);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CreatePipe(out SafeFileHandle hReadPipe, out SafeFileHandle hWritePipe, IntPtr lpPipeAttributes, uint nSize);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern int CreatePseudoConsole(COORD size, SafeFileHandle hInput, SafeFileHandle hOutput, uint dwFlags, out IntPtr phPC);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool ResizePseudoConsole(IntPtr hPC, COORD size);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool ClosePseudoConsole(IntPtr hPC);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CreateProcess(string lpApplicationName, string lpCommandLine, IntPtr lpProcessAttributes, IntPtr lpThreadAttributes, bool bInheritHandles, uint dwCreationFlags, IntPtr lpEnvironment, string lpCurrentDirectory, ref STARTUPINFOEX lpStartupInfo, out PROCESS_INFORMATION lpProcessInformation);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool InitializeProcThreadAttributeList(IntPtr lpAttributeList, int dwAttributeCount, uint dwFlags, ref IntPtr lpSize);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool UpdateProcThreadAttribute(IntPtr lpAttributeList, uint dwFlags, IntPtr Attribute, IntPtr lpValue, IntPtr cbSize, IntPtr lpPreviousValue, IntPtr lpReturnSize);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool ReadFile(SafeFileHandle hFile, byte[] lpBuffer, uint nNumberOfBytesToRead, out uint lpNumberOfBytesRead, IntPtr lpOverlapped);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetHandleInformation(SafeFileHandle hObject, uint dwMask, uint dwFlags);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool WriteFile(SafeHandle hFile, byte[] lpBuffer, uint nNumberOfBytesToWrite, out uint lpNumberOfBytesWritten, IntPtr lpOverlapped);
    }
}
