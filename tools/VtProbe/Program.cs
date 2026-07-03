using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using XtermSharp;

// VtProbe: diagnostic harness for modterm/XtermSharp rendering issues.
//
//   VtProbe capture "<command line>" <cols> <rows> <seconds> <outfile> [keyscript]
//     Runs the command under a real ConPTY and records the raw VT byte stream.
//     keyscript: optional file with lines "<delay-ms> <text|hex:XX..>" to send as input.
//
//   VtProbe replay <infile> <cols> <rows> [--screens]
//     Feeds the captured bytes through XtermSharp and dumps the final screen,
//     plus all parser errors/unknown sequences.
//
//   VtProbe seqdump <infile>
//     Prints a human-readable trace of the escape sequences in the capture.

internal static class Program
{
    static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("usage: capture|replay|seqdump ...");
            return 2;
        }

        Console.OutputEncoding = Encoding.UTF8;
        switch (args[0])
        {
            case "capture":
                return Capture(args[1], int.Parse(args[2]), int.Parse(args[3]), int.Parse(args[4]), args[5],
                    args.Length > 6 ? args[6] : null);
            case "replay":
                return Replay(args[1], int.Parse(args[2]), int.Parse(args[3]), args.Length > 4 ? args[4] : null,
                    args.Length > 5 ? int.Parse(args[5]) : 4096);
            case "seqdump":
                return SeqDump(args[1], args.Length > 2 ? args[2] : null);
            case "scan":
                return Scan(args[1], int.Parse(args[2]), int.Parse(args[3]), args[4], int.Parse(args[5]));
            case "frames":
                return Frames(args[1], int.Parse(args[2]), int.Parse(args[3]), args[4]);
            default:
                Console.Error.WriteLine("unknown mode " + args[0]);
                return 2;
        }
    }

    // ------------------------------------------------------------------ replay

    class ProbeDelegate : ITerminalDelegate
    {
        public void ShowCursor(Terminal source) { }
        public void SetTerminalTitle(Terminal source, string title) { }
        public void SetTerminalIconTitle(Terminal source, string title) { }
        public void SizeChanged(Terminal source) { }
        public void Send(byte[] data) { }
        public string WindowCommand(Terminal source, WindowManipulationCommand command, params int[] args) => null;
        public bool IsProcessTrusted() => true;
    }

    static int Replay(string file, int cols, int rows, string outFile, int chunkSize)
    {
        var bytes = File.ReadAllBytes(file);
        var terminal = new Terminal(new ProbeDelegate(), new TerminalOptions { Cols = cols, Rows = rows, Scrollback = 5000, ConvertEol = false });

        // Feed in chunks like the real reader does (4096-byte reads).
        for (int off = 0; off < bytes.Length; off += chunkSize)
        {
            int len = Math.Min(chunkSize, bytes.Length - off);
            var chunk = new byte[len];
            Array.Copy(bytes, off, chunk, 0, len);
            terminal.Feed(chunk, len);
        }

        var sb = new StringBuilder();
        sb.AppendLine($"=== final screen ({cols}x{rows}), YBase={terminal.Buffer.YBase} ===");
        DumpScreen(terminal, cols, rows, sb);
        if (outFile != null)
            File.WriteAllText(outFile, sb.ToString(), new UTF8Encoding(false));
        else
            Console.WriteLine(sb.ToString());
        return 0;
    }

    static void DumpScreen(Terminal terminal, int cols, int rows, StringBuilder sb)
    {
        var buffer = terminal.Buffer;
        for (int r = 0; r < rows; r++)
        {
            int idx = buffer.YBase + r;
            var line = idx < buffer.Lines.Length ? buffer.Lines[idx] : null;
            var lineSb = new StringBuilder();
            for (int c = 0; c < cols; c++)
            {
                if (line == null || c >= line.Length) { lineSb.Append(' '); continue; }
                var cd = line[c];
                if (cd.Code == 0)
                    lineSb.Append(' ');
                else
                {
                    uint rune = (uint)cd.Rune;
                    lineSb.Append(rune <= 0x10FFFF ? char.ConvertFromUtf32((int)rune) : "?");
                }
            }
            sb.AppendLine($"{r,3}|{lineSb}|");
        }
    }

    // Dumps every synchronized-update frame of the capture to numbered text files.
    static int Frames(string file, int cols, int rows, string outDir)
    {
        var bytes = File.ReadAllBytes(file);
        var terminal = new Terminal(new ProbeDelegate(), new TerminalOptions { Cols = cols, Rows = rows, Scrollback = 5000, ConvertEol = false });
        Directory.CreateDirectory(outDir);

        var marker = Encoding.ASCII.GetBytes("\x1b[?2026l");
        int fed = 0, frame = 0;
        for (int i = 0; i + marker.Length <= bytes.Length; i++)
        {
            bool match = true;
            for (int k = 0; k < marker.Length; k++)
                if (bytes[i + k] != marker[k]) { match = false; break; }
            if (!match) continue;

            int end = i + marker.Length;
            var chunk = new byte[end - fed];
            Array.Copy(bytes, fed, chunk, 0, end - fed);
            terminal.Feed(chunk, chunk.Length);
            fed = end;

            var sb = new StringBuilder();
            sb.AppendLine($"=== frame {frame} ending at byte {end} ===");
            DumpScreen(terminal, cols, rows, sb);
            File.WriteAllText(Path.Combine(outDir, $"frame_{frame:D3}.txt"), sb.ToString(), new UTF8Encoding(false));
            frame++;
        }
        Console.WriteLine($"{frame} frames -> {outDir}");
        return 0;
    }

    // Feeds the capture in small chunks; after each chunk, verifies that the top
    // box's left border (column 0, rows 1..borderRows) is not blank. Reports the
    // first frames where the border is broken.
    static int Scan(string file, int cols, int rows, string outFile, int chunkSize)
    {
        var bytes = File.ReadAllBytes(file);
        var terminal = new Terminal(new ProbeDelegate(), new TerminalOptions { Cols = cols, Rows = rows, Scrollback = 5000, ConvertEol = false });

        // Split the stream at end-of-synchronized-update markers (CSI ?2026l): each
        // marker is the end of one complete application frame, the only points where
        // the screen is expected to be consistent.
        var marker = Encoding.ASCII.GetBytes("\x1b[?2026l");
        var frameEnds = new List<int>();
        for (int i = 0; i + marker.Length <= bytes.Length; i++)
        {
            bool match = true;
            for (int k = 0; k < marker.Length; k++)
                if (bytes[i + k] != marker[k]) { match = false; break; }
            if (match) frameEnds.Add(i + marker.Length);
        }
        Console.WriteLine($"{frameEnds.Count} synchronized-update frames");

        var sb = new StringBuilder();
        int reported = 0;
        int fed = 0;
        uint[,] prev = null;
        foreach (var end in frameEnds)
        {
            if (reported >= 8)
                break;
            while (fed < end)
            {
                int len = Math.Min(chunkSize, end - fed);
                var chunk = new byte[len];
                Array.Copy(bytes, fed, chunk, 0, len);
                terminal.Feed(chunk, len);
                fed += len;
            }

            var buffer = terminal.Buffer;

            // Snapshot the viewport, diff box-drawing cells that became blank.
            var snapshot = new uint[rows, cols];
            for (int r = 0; r < rows; r++)
            {
                var line = buffer.Lines[buffer.YBase + r];
                for (int c = 0; c < cols; c++)
                    snapshot[r, c] = line[c].Code == 0 ? ' ' : (uint)line[c].Rune;
            }

            if (prev != null)
            {
                var broken = new List<string>();
                for (int r = 0; r < rows; r++)
                {
                    for (int c = 0; c < cols; c++)
                    {
                        uint was = prev[r, c];
                        uint now = snapshot[r, c];
                        bool wasBorder = (was >= 0x2500 && was <= 0x257F);
                        if (wasBorder && now == ' ')
                            broken.Add($"({r},{c}) {char.ConvertFromUtf32((int)was)}->blank");
                    }
                }

                if (broken.Count > 0)
                {
                    reported++;
                    sb.AppendLine($"--- frame ending at byte {end}: border cells blanked: {string.Join(" ", broken)} ---");
                    DumpScreen(terminal, cols, rows, sb);
                }
            }
            prev = snapshot;
        }

        if (reported == 0)
            sb.AppendLine("no broken border frames found");
        File.WriteAllText(outFile, sb.ToString(), new UTF8Encoding(false));
        Console.WriteLine($"scan complete, {reported} broken frames -> {outFile}");
        return 0;
    }

    // ------------------------------------------------------------------ seqdump

    static int SeqDump(string file, string outFile)
    {
        var bytes = File.ReadAllBytes(file);
        var text = Encoding.UTF8.GetString(bytes);
        var sb = new StringBuilder();
        foreach (var ch in text)
        {
            switch (ch)
            {
                case '\x1b': sb.Append("\nESC "); break;
                case '\r': sb.Append("<CR>"); break;
                case '\n': sb.Append("<LF>\n"); break;
                case '\a': sb.Append("<BEL>"); break;
                case '\b': sb.Append("<BS>"); break;
                case '\t': sb.Append("<TAB>"); break;
                default:
                    if (ch < 0x20) sb.Append($"<{(int)ch:X2}>");
                    else sb.Append(ch);
                    break;
            }
        }
        if (outFile != null)
            File.WriteAllText(outFile, sb.ToString(), new UTF8Encoding(false));
        else
            Console.WriteLine(sb.ToString());
        return 0;
    }

    // ------------------------------------------------------------------ capture

    [DllImport("kernel32.dll")] static extern bool FreeConsole();

    static int Capture(string commandLine, int cols, int rows, int seconds, string outFile, string keyScript)
    {
        // Detach from the parent console so the child binds exclusively to the
        // pseudoconsole (a parent console otherwise leaks through as std handles).
        FreeConsole();
        var pty = new MiniPty();
        pty.Start(commandLine, cols, rows);

        var output = new MemoryStream();
        var readThread = new Thread(() =>
        {
            var buf = new byte[4096];
            while (true)
            {
                if (!pty.Read(buf, out uint read) || read == 0)
                    break;
                lock (output) output.Write(buf, 0, (int)read);
            }
        }) { IsBackground = true };
        readThread.Start();

        var keys = new List<(int delayMs, byte[] data)>();
        if (keyScript != null)
        {
            foreach (var raw in File.ReadAllLines(keyScript))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                int sp = line.IndexOf(' ');
                int delay = int.Parse(line.Substring(0, sp));
                string payload = line.Substring(sp + 1);
                byte[] data = payload.StartsWith("hex:")
                    ? Convert.FromHexString(payload.Substring(4))
                    : Encoding.UTF8.GetBytes(payload.Replace("\\e", "\x1b").Replace("\\r", "\r"));
                keys.Add((delay, data));
            }
        }

        var sw = Stopwatch.StartNew();
        int keyIdx = 0;
        int elapsedForKeys = 0;
        while (sw.Elapsed.TotalSeconds < seconds)
        {
            Thread.Sleep(50);
            if (keyIdx < keys.Count)
            {
                if (sw.ElapsedMilliseconds >= elapsedForKeys + keys[keyIdx].delayMs)
                {
                    elapsedForKeys += keys[keyIdx].delayMs;
                    pty.Write(keys[keyIdx].data);
                    keyIdx++;
                }
            }
        }

        pty.Kill();
        readThread.Join(2000);

        lock (output) File.WriteAllBytes(outFile, output.ToArray());
        try { Console.WriteLine($"captured {output.Length} bytes -> {outFile}"); } catch { }
        return 0;
    }

    // Minimal ConPTY host, mirrors modterm's ConPTYTerminal setup.
    class MiniPty
    {
        IntPtr hPC;
        SafeFileHandle inputWrite, inputRead, outputWrite, outputRead;
        Process process;

        public void Start(string commandLine, int cols, int rows)
        {
            IntPtr saPtr = Marshal.AllocHGlobal(Marshal.SizeOf<SECURITY_ATTRIBUTES>());
            var sa = new SECURITY_ATTRIBUTES { nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES>(), bInheritHandle = true };
            Marshal.StructureToPtr(sa, saPtr, false);
            CreatePipe(out outputRead, out outputWrite, saPtr, 0);
            SetHandleInformation(outputRead, 1, 0);
            CreatePipe(out inputRead, out inputWrite, saPtr, 0);
            SetHandleInformation(inputWrite, 1, 0);
            Marshal.FreeHGlobal(saPtr);

            if (CreatePseudoConsole(new COORD { X = (short)cols, Y = (short)rows }, inputRead, outputWrite, 0, out hPC) != 0)
                throw new Exception("CreatePseudoConsole failed");

            IntPtr attrSize = IntPtr.Zero;
            InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref attrSize);
            IntPtr attrList = Marshal.AllocHGlobal(attrSize);
            var si = default(STARTUPINFOEX);
            si.StartupInfo.cb = Marshal.SizeOf<STARTUPINFOEX>();
            si.lpAttributeList = attrList;
            if (!InitializeProcThreadAttributeList(attrList, 1, 0, ref attrSize))
                throw new Exception("InitializeProcThreadAttributeList failed: " + Marshal.GetLastWin32Error());
            if (!UpdateProcThreadAttribute(attrList, 0, (IntPtr)0x00020016, hPC, (IntPtr)IntPtr.Size, IntPtr.Zero, IntPtr.Zero))
                throw new Exception("UpdateProcThreadAttribute failed: " + Marshal.GetLastWin32Error());

            IntPtr cmdPtr = Marshal.StringToHGlobalUni(commandLine);

            // Console children of a console parent get the parent's redirected std
            // pipes duplicated into them even with bInheritHandles=false, which
            // bypasses the pseudoconsole. Clear our std handles so the child binds
            // its std handles to the pseudoconsole instead (GUI apps like modterm
            // get this for free).
            IntPtr oldIn = GetStdHandle(-10), oldOut = GetStdHandle(-11), oldErr = GetStdHandle(-12);
            SetStdHandle(-10, IntPtr.Zero);
            SetStdHandle(-11, IntPtr.Zero);
            SetStdHandle(-12, IntPtr.Zero);

            bool ok = CreateProcessW(IntPtr.Zero, cmdPtr, IntPtr.Zero, IntPtr.Zero, false,
                    0x00080000, IntPtr.Zero, IntPtr.Zero, ref si, out var pi);
            int err = Marshal.GetLastWin32Error();

            SetStdHandle(-10, oldIn);
            SetStdHandle(-11, oldOut);
            SetStdHandle(-12, oldErr);

            if (!ok)
                throw new Exception("CreateProcess failed: " + err);
            Marshal.FreeHGlobal(cmdPtr);

            process = Process.GetProcessById(pi.dwProcessId);
            CloseHandle(pi.hThread);
            CloseHandle(pi.hProcess);
            inputRead.Dispose();
            outputWrite.Dispose();
        }

        public bool Read(byte[] buf, out uint read) => ReadFile(outputRead, buf, (uint)buf.Length, out read, IntPtr.Zero);

        public void Write(byte[] data) => WriteFile(inputWrite, data, (uint)data.Length, out _, IntPtr.Zero);

        public void Kill()
        {
            try { if (process != null && !process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            if (hPC != IntPtr.Zero) ClosePseudoConsole(hPC);
        }

        [StructLayout(LayoutKind.Sequential)] struct COORD { public short X; public short Y; }
        [StructLayout(LayoutKind.Sequential)] struct SECURITY_ATTRIBUTES { public int nLength; public IntPtr lpSecurityDescriptor; [MarshalAs(UnmanagedType.Bool)] public bool bInheritHandle; }
        [StructLayout(LayoutKind.Sequential)] struct PROCESS_INFORMATION { public IntPtr hProcess; public IntPtr hThread; public int dwProcessId; public int dwThreadId; }
        [StructLayout(LayoutKind.Sequential)] struct STARTUPINFOEX { public STARTUPINFO StartupInfo; public IntPtr lpAttributeList; }
        [StructLayout(LayoutKind.Sequential)] struct STARTUPINFO { public int cb; public IntPtr lpReserved; public IntPtr lpDesktop; public IntPtr lpTitle; public uint dwX; public uint dwY; public uint dwXSize; public uint dwYSize; public uint dwXCountChars; public uint dwYCountChars; public uint dwFillAttribute; public uint dwFlags; public short wShowWindow; public short cbReserved2; public IntPtr lpReserved2; public IntPtr hStdInput; public IntPtr hStdOutput; public IntPtr hStdError; }

        [DllImport("kernel32.dll", SetLastError = true)] static extern bool CreatePipe(out SafeFileHandle hReadPipe, out SafeFileHandle hWritePipe, IntPtr lpPipeAttributes, uint nSize);
        [DllImport("kernel32.dll", SetLastError = true)] static extern int CreatePseudoConsole(COORD size, SafeFileHandle hInput, SafeFileHandle hOutput, uint dwFlags, out IntPtr phPC);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool ClosePseudoConsole(IntPtr hPC);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool SetHandleInformation(SafeFileHandle hObject, uint dwMask, uint dwFlags);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool InitializeProcThreadAttributeList(IntPtr lpAttributeList, int dwAttributeCount, uint dwFlags, ref IntPtr lpSize);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool UpdateProcThreadAttribute(IntPtr lpAttributeList, uint dwFlags, IntPtr Attribute, IntPtr lpValue, IntPtr cbSize, IntPtr lpPreviousValue, IntPtr lpReturnSize);
        [DllImport("kernel32.dll", EntryPoint = "CreateProcessW", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)] static extern bool CreateProcessW(IntPtr lpApplicationName, IntPtr lpCommandLine, IntPtr lpProcessAttributes, IntPtr lpThreadAttributes, bool bInheritHandles, uint dwCreationFlags, IntPtr lpEnvironment, IntPtr lpCurrentDirectory, ref STARTUPINFOEX lpStartupInfo, out PROCESS_INFORMATION lpProcessInformation);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool ReadFile(SafeFileHandle hFile, byte[] lpBuffer, uint nNumberOfBytesToRead, out uint lpNumberOfBytesRead, IntPtr lpOverlapped);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool WriteFile(SafeFileHandle hFile, byte[] lpBuffer, uint nNumberOfBytesToWrite, out uint lpNumberOfBytesWritten, IntPtr lpOverlapped);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool CloseHandle(IntPtr hObject);
        [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr GetStdHandle(int nStdHandle);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool SetStdHandle(int nStdHandle, IntPtr hHandle);
    }
}
