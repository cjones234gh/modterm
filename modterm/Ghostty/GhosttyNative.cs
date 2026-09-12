using System;
using System.IO;
using System.Runtime.InteropServices;

namespace modterm.Ghostty;

internal enum GhosttyResult : int
{
    Success = 0,
    OutOfMemory = -1,
    InvalidValue = -2,
    OutOfSpace = -3,
    NoValue = -4,
}

internal enum GhosttyVtKeyAction : int
{
    Release = 0,
    Press = 1,
    Repeat = 2,
}

internal enum GhosttyVtKey : int
{
    Unidentified = 0,
    Backquote, Backslash, BracketLeft, BracketRight, Comma,
    Digit0, Digit1, Digit2, Digit3, Digit4, Digit5, Digit6, Digit7, Digit8, Digit9,
    Equal, IntlBackslash, IntlRo, IntlYen,
    A, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q, R, S, T, U, V, W, X, Y, Z,
    Minus, Period, Quote, Semicolon, Slash,
    AltLeft, AltRight, Backspace, CapsLock, ContextMenu, ControlLeft, ControlRight,
    Enter, MetaLeft, MetaRight, ShiftLeft, ShiftRight, Space, Tab,
    Convert, KanaMode, NonConvert,
    Delete, End, Help, Home, Insert, PageDown, PageUp,
    ArrowDown, ArrowLeft, ArrowRight, ArrowUp,
    NumLock,
    Numpad0, Numpad1, Numpad2, Numpad3, Numpad4, Numpad5, Numpad6, Numpad7, Numpad8, Numpad9,
    NumpadAdd, NumpadBackspace, NumpadClear, NumpadClearEntry, NumpadComma, NumpadDecimal,
    NumpadDivide, NumpadEnter, NumpadEqual, NumpadMemoryAdd, NumpadMemoryClear, NumpadMemoryRecall,
    NumpadMemoryStore, NumpadMemorySubtract, NumpadMultiply, NumpadParenLeft, NumpadParenRight,
    NumpadSubtract, NumpadSeparator, NumpadUp, NumpadDown, NumpadRight, NumpadLeft, NumpadBegin,
    NumpadHome, NumpadEnd, NumpadInsert, NumpadDelete, NumpadPageUp, NumpadPageDown,
    Escape, F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,
    F13, F14, F15, F16, F17, F18, F19, F20,
}

[Flags]
internal enum GhosttyVtMods : ushort
{
    None = 0,
    Shift = 1 << 0,
    Ctrl = 1 << 1,
    Alt = 1 << 2,
    Super = 1 << 3,
    CapsLock = 1 << 4,
    NumLock = 1 << 5,
}

internal enum GhosttyFocusEvent : int { Gained = 0, Lost = 1 }

internal enum GhosttyColorScheme : int { Light = 0, Dark = 1 }

internal enum GhosttyTerminalScreen : int { Primary = 0, Alternate = 1 }

internal enum GhosttyTerminalCursorStyle : int { Bar = 0, Block = 1, Underline = 2, BlockHollow = 3 }

internal enum GhosttyClipboardLocation : int { Standard = 0, Selection = 1, Primary = 2 }

internal enum GhosttyClipboardWriteResult : int
{
    Success = 0, Denied = 1, Unsupported = 2, Busy = 3, InvalidData = 4, IoError = 5,
}

internal enum GhosttyMouseAction : int { Press = 0, Release = 1, Motion = 2 }

internal enum GhosttyMouseButtonId : int
{
    Unknown = 0, Left = 1, Right = 2, Middle = 3, Four = 4, Five = 5,
    Six = 6, Seven = 7, Eight = 8, Nine = 9, Ten = 10, Eleven = 11,
}

internal enum GhosttyMouseEncoderOption : int
{
    Event = 0, Format = 1, Size = 2, AnyButtonPressed = 3, TrackLastCell = 4,
}

internal enum GhosttyTerminalOption : int
{
    Userdata = 0, WritePty = 1, Bell = 2, Enquiry = 3, Xtversion = 4, TitleChanged = 5,
    Size = 6, ColorScheme = 7, DeviceAttributes = 8, Title = 9, Pwd = 10,
    ColorForeground = 11, ColorBackground = 12, ColorCursor = 13, ColorPalette = 14,
    KittyImageStorageLimit = 15, KittyImageMediumFile = 16, KittyImageMediumTempFile = 17,
    KittyImageMediumSharedMemory = 18, ApcMaxBytes = 19, ApcMaxBytesKitty = 20,
    Selection = 21, DefaultCursorStyle = 22, DefaultCursorBlink = 23, GlyphProtocol = 24,
    PwdChanged = 25, ClipboardWrite = 26, ScrollbackMaxBytes = 27, ScrollbackMaxLines = 28,
    DesktopNotification = 29, ProgressReport = 30,
}

internal enum GhosttyTerminalData : int
{
    Invalid = 0, Cols = 1, Rows = 2, CursorX = 3, CursorY = 4, CursorPendingWrap = 5,
    ActiveScreen = 6, CursorVisible = 7, KittyKeyboardFlags = 8, Scrollbar = 9,
    CursorStyle = 10, MouseTracking = 11, Title = 12, Pwd = 13, TotalRows = 14,
    ScrollbackRows = 15, WidthPx = 16, HeightPx = 17, ColorForeground = 18,
    ColorBackground = 19, ColorCursor = 20, ColorPalette = 21, ColorForegroundDefault = 22,
    ColorBackgroundDefault = 23, ColorCursorDefault = 24, ColorPaletteDefault = 25,
    KittyImageStorageLimit = 26, KittyImageMediumFile = 27, KittyImageMediumTempFile = 28,
    KittyImageMediumSharedMemory = 29, KittyGraphics = 30, Selection = 31,
    ViewportActive = 32, VtProcessingError = 33, ScrollbackMaxBytes = 34, ScrollbackMaxLines = 35,
}

internal enum GhosttyRenderStateDirty : int { False = 0, Partial = 1, Full = 2 }

internal enum GhosttyRenderStateCursorVisualStyle : int { Bar = 0, Block = 1, Underline = 2, BlockHollow = 3 }

internal enum GhosttyRenderStateData : int
{
    Invalid = 0, Cols = 1, Rows = 2, Dirty = 3, RowIterator = 4,
    ColorBackground = 5, ColorForeground = 6, ColorCursor = 7, ColorCursorHasValue = 8,
    ColorPalette = 9, CursorVisualStyle = 10, CursorVisible = 11, CursorBlinking = 12,
    CursorPasswordInput = 13, CursorViewportHasValue = 14, CursorViewportX = 15,
    CursorViewportY = 16, CursorViewportWideTail = 17,
}

internal enum GhosttyRenderStateRowData : int { Invalid = 0, Dirty = 1, Raw = 2, Cells = 3, Selection = 4 }

internal enum GhosttyRenderStateRowCellsData : int
{
    Invalid = 0, Raw = 1, Style = 2, GraphemesLength = 3, GraphemesBuffer = 4,
    BackgroundColor = 5, ForegroundColor = 6, Selected = 7, HasStyling = 8, GraphemesUtf8 = 9,
}

internal enum GhosttyCellData : int
{
    Invalid = 0, Codepoint = 1, ContentTag = 2, Wide = 3, HasText = 4, HasStyling = 5,
    StyleId = 6, HasHyperlink = 7, Protected = 8, SemanticContent = 9, ColorPalette = 10, ColorRgb = 11,
}

internal enum GhosttyCellWide : int { Narrow = 0, Wide = 1, SpacerTail = 2, SpacerHead = 3 }

internal enum GhosttyStyleColorTag : int { None = 0, Palette = 1, Rgb = 2 }

internal enum GhosttyPointTag : int { Active = 0, Viewport = 1, Screen = 2, History = 3 }

internal enum GhosttyKittyGraphicsData : int { Invalid = 0, PlacementIterator = 1, Generation = 2 }

internal enum GhosttyKittyGraphicsPlacementData : int
{
    Invalid = 0, ImageId = 1, PlacementId = 2, IsVirtual = 3, XOffset = 4, YOffset = 5,
    SourceX = 6, SourceY = 7, SourceWidth = 8, SourceHeight = 9, Columns = 10, Rows = 11, Z = 12,
}

internal enum GhosttyKittyPlacementLayer : int { All = 0, BelowBackground = 1, BelowText = 2, AboveText = 3 }

internal enum GhosttyKittyGraphicsPlacementIteratorOption : int { Layer = 0 }

internal enum GhosttyKittyImageFormat : int { Rgb = 0, Rgba = 1, Png = 2, GrayAlpha = 3, Gray = 4 }

internal enum GhosttyKittyGraphicsImageData : int
{
    Invalid = 0, Id = 1, Number = 2, Width = 3, Height = 4, Format = 5, Compression = 6,
    DataPtr = 7, DataLength = 8, Generation = 9,
}

internal enum GhosttyFormatterFormat : int { Plain = 0, Vt = 1, Html = 2 }

internal enum GhosttyBuildInfoData : int
{
    Invalid = 0, Simd = 1, KittyGraphics = 2, TmuxControlMode = 3, Optimize = 4, VersionString = 5,
}

internal enum GhosttySysOption : int { Userdata = 0, DecodePng = 1, Log = 2 }

internal enum GhosttyTerminalScrollViewportTag : int { Top = 0, Bottom = 1, Delta = 2, Row = 3 }

[StructLayout(LayoutKind.Sequential)]
internal struct GhosttyColorRgb
{
    public byte R, G, B;

    public static GhosttyColorRgb FromBytes(byte r, byte g, byte b) => new() { R = r, G = g, B = b };
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct GhosttyString
{
    public readonly nint Ptr;
    public readonly nuint Len;

    public GhosttyString(nint ptr, nuint len)
    {
        Ptr = ptr;
        Len = len;
    }

    public string ToUtf8String()
    {
        if (Ptr == nint.Zero || Len == 0)
            return string.Empty;
        return Marshal.PtrToStringUTF8(Ptr, checked((int)Len)) ?? string.Empty;
    }
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct GhosttyMode
{
    public readonly ushort Value;
    public GhosttyMode(ushort value) => Value = value;

    public static GhosttyMode Dec(ushort value) => new((ushort)(value & 0x7FFF));
    public static GhosttyMode Ansi(ushort value) => new((ushort)((value & 0x7FFF) | 0x8000));
}

[StructLayout(LayoutKind.Sequential)]
internal struct GhosttySizeReportSize
{
    public ushort Rows;
    public ushort Columns;
    public uint CellWidth;
    public uint CellHeight;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct GhosttyDeviceAttributesPrimary
{
    public ushort ConformanceLevel;
    public fixed ushort Features[64];
    public nuint NumFeatures;

    public void SetFeature(int index, ushort value)
    {
        if ((uint)index >= 64)
            throw new ArgumentOutOfRangeException(nameof(index));
        Features[index] = value;
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct GhosttyDeviceAttributesSecondary
{
    public ushort DeviceType, FirmwareVersion, RomCartridge;
}

[StructLayout(LayoutKind.Sequential)]
internal struct GhosttyDeviceAttributesTertiary
{
    public uint UnitId;
}

[StructLayout(LayoutKind.Sequential)]
internal struct GhosttyDeviceAttributes
{
    public GhosttyDeviceAttributesPrimary Primary;
    public GhosttyDeviceAttributesSecondary Secondary;
    public GhosttyDeviceAttributesTertiary Tertiary;
}

[StructLayout(LayoutKind.Sequential)]
internal struct GhosttyTerminalScrollbar
{
    public ulong Total;
    public ulong Offset;
    public ulong Length;
}

[StructLayout(LayoutKind.Explicit, Size = 16)]
internal struct GhosttyTerminalScrollViewportValue
{
    [FieldOffset(0)] public nint Delta;
    [FieldOffset(0)] public nuint Row;
}

[StructLayout(LayoutKind.Sequential)]
internal struct GhosttyTerminalScrollViewport
{
    public GhosttyTerminalScrollViewportTag Tag;
    public GhosttyTerminalScrollViewportValue Value;

    public static GhosttyTerminalScrollViewport Bottom() => new() { Tag = GhosttyTerminalScrollViewportTag.Bottom };
    public static GhosttyTerminalScrollViewport AbsoluteRow(nuint row) => new()
    {
        Tag = GhosttyTerminalScrollViewportTag.Row,
        Value = new GhosttyTerminalScrollViewportValue { Row = row },
    };
}

[StructLayout(LayoutKind.Explicit, Size = 24)]
internal struct GhosttyPoint
{
    [FieldOffset(0)] public GhosttyPointTag Tag;
    [FieldOffset(8)] public ushort X;
    [FieldOffset(12)] public uint Y;

    public static GhosttyPoint Viewport(ushort x, uint y) => new() { Tag = GhosttyPointTag.Viewport, X = x, Y = y };
    public static GhosttyPoint Screen(ushort x, uint y) => new() { Tag = GhosttyPointTag.Screen, X = x, Y = y };
}

[StructLayout(LayoutKind.Sequential)]
internal struct GhosttyGridRef
{
    public nuint Size;
    public nint Node;
    public ushort X;
    public ushort Y;

    public static GhosttyGridRef CreateSized() => new() { Size = (nuint)Marshal.SizeOf<GhosttyGridRef>() };
}

[StructLayout(LayoutKind.Sequential)]
internal struct GhosttySelectionRange
{
    public nuint Size;
    public GhosttyGridRef Start;
    public GhosttyGridRef End;
    [MarshalAs(UnmanagedType.U1)] public bool Rectangle;

    public static GhosttySelectionRange CreateSized() => new()
    {
        Size = (nuint)Marshal.SizeOf<GhosttySelectionRange>(),
        Start = GhosttyGridRef.CreateSized(),
        End = GhosttyGridRef.CreateSized(),
    };
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct GhosttyTerminalSelectionFormatOptions
{
    public nuint Size;
    public GhosttyFormatterFormat Format;
    [MarshalAs(UnmanagedType.U1)] public bool Unwrap;
    [MarshalAs(UnmanagedType.U1)] public bool Trim;
    public GhosttySelectionRange* Selection;

    public static GhosttyTerminalSelectionFormatOptions CreateSized() => new()
    {
        Size = (nuint)Marshal.SizeOf<GhosttyTerminalSelectionFormatOptions>(),
    };
}

[StructLayout(LayoutKind.Explicit, Size = 8)]
internal struct GhosttyStyleColorValue
{
    [FieldOffset(0)] public byte Palette;
    [FieldOffset(0)] public GhosttyColorRgb Rgb;
    [FieldOffset(0)] public ulong Padding;
}

[StructLayout(LayoutKind.Sequential)]
internal struct GhosttyStyleColor
{
    public GhosttyStyleColorTag Tag;
    public GhosttyStyleColorValue Value;
}

[StructLayout(LayoutKind.Sequential)]
internal struct GhosttyStyle
{
    public nuint Size;
    public GhosttyStyleColor ForegroundColor;
    public GhosttyStyleColor BackgroundColor;
    public GhosttyStyleColor UnderlineColor;
    [MarshalAs(UnmanagedType.U1)] public bool Bold;
    [MarshalAs(UnmanagedType.U1)] public bool Italic;
    [MarshalAs(UnmanagedType.U1)] public bool Faint;
    [MarshalAs(UnmanagedType.U1)] public bool Blink;
    [MarshalAs(UnmanagedType.U1)] public bool Inverse;
    [MarshalAs(UnmanagedType.U1)] public bool Invisible;
    [MarshalAs(UnmanagedType.U1)] public bool Strikethrough;
    [MarshalAs(UnmanagedType.U1)] public bool Overline;
    public int Underline;

    public static GhosttyStyle CreateSized() => new() { Size = (nuint)Marshal.SizeOf<GhosttyStyle>() };
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct GhosttyBuffer
{
    public byte* Pointer;
    public nuint Capacity;
    public nuint Length;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct GhosttyRenderStateColors
{
    public nuint Size;
    public GhosttyColorRgb Background;
    public GhosttyColorRgb Foreground;
    public GhosttyColorRgb Cursor;
    [MarshalAs(UnmanagedType.U1)] public bool CursorHasValue;
    private fixed byte _palette[256 * 3];

    public static GhosttyRenderStateColors CreateSized() => new() { Size = (nuint)Marshal.SizeOf<GhosttyRenderStateColors>() };
}

[StructLayout(LayoutKind.Sequential)]
internal struct GhosttyMousePosition
{
    public float X;
    public float Y;
}

[StructLayout(LayoutKind.Sequential)]
internal struct GhosttyMouseEncoderSize
{
    public nuint Size;
    public uint ScreenWidth;
    public uint ScreenHeight;
    public uint CellWidth;
    public uint CellHeight;
    public uint PaddingTop;
    public uint PaddingBottom;
    public uint PaddingRight;
    public uint PaddingLeft;

    public static GhosttyMouseEncoderSize CreateSized() => new() { Size = (nuint)Marshal.SizeOf<GhosttyMouseEncoderSize>() };
}

[StructLayout(LayoutKind.Sequential)]
internal struct GhosttyKittyGraphicsPlacementRenderInfo
{
    public nuint Size;
    public uint PixelWidth;
    public uint PixelHeight;
    public uint GridColumns;
    public uint GridRows;
    public int ViewportColumn;
    public int ViewportRow;
    [MarshalAs(UnmanagedType.U1)] public bool ViewportVisible;
    public uint SourceX;
    public uint SourceY;
    public uint SourceWidth;
    public uint SourceHeight;

    public static GhosttyKittyGraphicsPlacementRenderInfo CreateSized() =>
        new() { Size = (nuint)Marshal.SizeOf<GhosttyKittyGraphicsPlacementRenderInfo>() };
}

[StructLayout(LayoutKind.Sequential)]
internal struct GhosttyClipboardContent
{
    public GhosttyString Mime;
    public GhosttyString Data;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct GhosttyClipboardWrite
{
    public nuint Size;
    public GhosttyClipboardLocation Location;
    public GhosttyClipboardContent* Contents;
    public nuint ContentsLength;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct GhosttyAllocatorVtable
{
    public delegate* unmanaged[Cdecl]<void*, nuint, byte, nuint, void*> Alloc;
    public delegate* unmanaged[Cdecl]<void*, void*, nuint, byte, nuint, nuint, byte> Resize;
    public delegate* unmanaged[Cdecl]<void*, void*, nuint, byte, nuint, nuint, void*> Remap;
    public delegate* unmanaged[Cdecl]<void*, void*, nuint, byte, nuint, void> Free;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct GhosttyAllocator
{
    public void* Context;
    public GhosttyAllocatorVtable* Vtable;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct GhosttySysImage
{
    public uint Width;
    public uint Height;
    public byte* Data;
    public nuint DataLength;
}

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate void GhosttyWritePtyCallback(nint terminal, nint userdata, nint data, nuint len);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal unsafe delegate byte GhosttySizeCallback(nint terminal, nint userdata, GhosttySizeReportSize* size);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal unsafe delegate byte GhosttyColorSchemeCallback(nint terminal, nint userdata, GhosttyColorScheme* scheme);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal unsafe delegate byte GhosttyDeviceAttributesCallback(nint terminal, nint userdata, GhosttyDeviceAttributes* attributes);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate GhosttyString GhosttyEnquiryCallback(nint terminal, nint userdata);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal unsafe delegate GhosttyClipboardWriteResult GhosttyClipboardWriteCallback(
    nint terminal, nint userdata, GhosttyClipboardWrite* write);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal unsafe delegate byte GhosttySysDecodePngCallback(
    void* userdata, GhosttyAllocator* allocator, byte* data, nuint dataLength, GhosttySysImage* output);

internal static unsafe class GhosttyNative
{
    private const string Lib = "ghostty-vt";

    static GhosttyNative()
    {
        NativeLibrary.SetDllImportResolver(typeof(GhosttyNative).Assembly, Resolve);
    }

    private static nint Resolve(string libraryName, System.Reflection.Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!string.Equals(libraryName, Lib, StringComparison.Ordinal))
            return nint.Zero;

        string file = OperatingSystem.IsWindows() ? "ghostty-vt.dll" : "libghostty-vt.so";
        string baseDir = AppContext.BaseDirectory;
        string rid = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "win-arm64" : "win-x64";
        string[] candidates =
        [
            Path.Combine(baseDir, file),
            Path.Combine(baseDir, "runtimes", rid, "native", file),
            Path.Combine(Path.GetDirectoryName(assembly.Location) ?? string.Empty, file),
        ];
        foreach (string path in candidates)
        {
            if (NativeLibrary.TryLoad(path, out nint handle))
                return handle;
        }

        return NativeLibrary.TryLoad(libraryName, assembly, searchPath, out nint fallback) ? fallback : nint.Zero;
    }

    public static void ThrowIfFailed(GhosttyResult result, string operation)
    {
        if (result != GhosttyResult.Success)
            throw new InvalidOperationException($"{operation} failed with {result}.");
    }

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ghostty_terminal_new")]
    public static extern GhosttyResult TerminalNew(nint allocator, out nint terminal, ushort columns, ushort rows);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ghostty_terminal_free")]
    public static extern void TerminalFree(nint terminal);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ghostty_terminal_reset")]
    public static extern void TerminalReset(nint terminal);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ghostty_terminal_resize")]
    public static extern GhosttyResult TerminalResize(nint terminal, ushort cols, ushort rows, uint cellWidthPx, uint cellHeightPx);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ghostty_terminal_set")]
    public static extern GhosttyResult TerminalSet(nint terminal, GhosttyTerminalOption option, void* value);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ghostty_terminal_get")]
    public static extern GhosttyResult TerminalGet(nint terminal, GhosttyTerminalData data, void* output);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ghostty_terminal_vt_write")]
    public static extern void TerminalVtWrite(nint terminal, byte* data, nuint len);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ghostty_terminal_scroll_viewport")]
    public static extern void TerminalScrollViewport(nint terminal, GhosttyTerminalScrollViewport behavior);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ghostty_terminal_mode_get")]
    public static extern GhosttyResult TerminalModeGet(nint terminal, GhosttyMode mode, [MarshalAs(UnmanagedType.U1)] out bool value);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ghostty_terminal_grid_ref")]
    public static extern GhosttyResult TerminalGridRef(nint terminal, GhosttyPoint point, ref GhosttyGridRef reference);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ghostty_terminal_selection_format_buf")]
    public static extern GhosttyResult TerminalSelectionFormatBuffer(
        nint terminal, GhosttyTerminalSelectionFormatOptions options, byte* buffer, nuint bufferLength, out nuint written);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ghostty_render_state_new")]
    public static extern GhosttyResult RenderStateNew(nint allocator, out nint state);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ghostty_render_state_free")]
    public static extern void RenderStateFree(nint state);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ghostty_render_state_update")]
    public static extern GhosttyResult RenderStateUpdate(nint state, nint terminal);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ghostty_render_state_get")]
    public static extern GhosttyResult RenderStateGet(nint state, GhosttyRenderStateData data, void* output);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ghostty_render_state_colors_get")]
    public static extern GhosttyResult RenderStateColorsGet(nint state, ref GhosttyRenderStateColors colors);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ghostty_render_state_row_iterator_new")]
    public static extern GhosttyResult RenderStateRowIteratorNew(nint allocator, out nint iterator);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ghostty_render_state_row_iterator_free")]
    public static extern void RenderStateRowIteratorFree(nint iterator);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ghostty_render_state_row_iterator_next")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool RenderStateRowIteratorNext(nint iterator);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ghostty_render_state_row_get")]
    public static extern GhosttyResult RenderStateRowGet(nint iterator, GhosttyRenderStateRowData data, void* output);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ghostty_render_state_row_cells_new")]
    public static extern GhosttyResult RenderStateRowCellsNew(nint allocator, out nint cells);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ghostty_render_state_row_cells_free")]
    public static extern void RenderStateRowCellsFree(nint cells);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ghostty_render_state_row_cells_next")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool RenderStateRowCellsNext(nint cells);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ghostty_render_state_row_cells_get")]
    public static extern GhosttyResult RenderStateRowCellsGet(nint cells, GhosttyRenderStateRowCellsData data, void* output);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ghostty_cell_get")]
    public static extern GhosttyResult CellGet(ulong cell, GhosttyCellData data, void* output);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ghostty_key_event_new")]
    public static extern GhosttyResult KeyEventNew(nint allocator, out nint keyEvent);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ghostty_key_event_free")]
    public static extern void KeyEventFree(nint keyEvent);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ghostty_key_event_set_action")]
    public static extern void KeyEventSetAction(nint keyEvent, GhosttyVtKeyAction action);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ghostty_key_event_set_key")]
    public static extern void KeyEventSetKey(nint keyEvent, GhosttyVtKey key);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ghostty_key_event_set_mods")]
    public static extern void KeyEventSetMods(nint keyEvent, GhosttyVtMods mods);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ghostty_key_event_set_consumed_mods")]
    public static extern void KeyEventSetConsumedMods(nint keyEvent, GhosttyVtMods mods);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ghostty_key_event_set_composing")]
    public static extern void KeyEventSetComposing(nint keyEvent, [MarshalAs(UnmanagedType.U1)] bool composing);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ghostty_key_event_set_utf8")]
    public static extern void KeyEventSetUtf8(nint keyEvent, byte* utf8, nuint len);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ghostty_key_event_set_unshifted_codepoint")]
    public static extern void KeyEventSetUnshiftedCodepoint(nint keyEvent, uint codepoint);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ghostty_key_encoder_new")]
    public static extern GhosttyResult KeyEncoderNew(nint allocator, out nint encoder);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ghostty_key_encoder_free")]
    public static extern void KeyEncoderFree(nint encoder);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ghostty_key_encoder_setopt_from_terminal")]
    public static extern void KeyEncoderSetoptFromTerminal(nint encoder, nint terminal);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ghostty_key_encoder_encode")]
    public static extern GhosttyResult KeyEncoderEncode(nint encoder, nint keyEvent, byte* outBuf, nuint outBufSize, out nuint outLen);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ghostty_mouse_event_new")]
    public static extern GhosttyResult MouseEventNew(nint allocator, out nint mouseEvent);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ghostty_mouse_event_free")]
    public static extern void MouseEventFree(nint mouseEvent);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ghostty_mouse_event_set_action")]
    public static extern void MouseEventSetAction(nint mouseEvent, GhosttyMouseAction action);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ghostty_mouse_event_set_button")]
    public static extern void MouseEventSetButton(nint mouseEvent, GhosttyMouseButtonId button);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ghostty_mouse_event_clear_button")]
    public static extern void MouseEventClearButton(nint mouseEvent);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ghostty_mouse_event_set_mods")]
    public static extern void MouseEventSetMods(nint mouseEvent, GhosttyVtMods mods);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ghostty_mouse_event_set_position")]
    public static extern void MouseEventSetPosition(nint mouseEvent, GhosttyMousePosition position);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ghostty_mouse_encoder_new")]
    public static extern GhosttyResult MouseEncoderNew(nint allocator, out nint encoder);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ghostty_mouse_encoder_free")]
    public static extern void MouseEncoderFree(nint encoder);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ghostty_mouse_encoder_setopt")]
    public static extern void MouseEncoderSetopt(nint encoder, GhosttyMouseEncoderOption option, void* value);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ghostty_mouse_encoder_setopt_from_terminal")]
    public static extern void MouseEncoderSetoptFromTerminal(nint encoder, nint terminal);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ghostty_mouse_encoder_encode")]
    public static extern GhosttyResult MouseEncoderEncode(nint encoder, nint mouseEvent, byte* output, nuint outputLength, out nuint written);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ghostty_paste_encode")]
    public static extern GhosttyResult PasteEncode(
        byte* data, nuint dataLen, [MarshalAs(UnmanagedType.U1)] bool bracketed,
        byte* buffer, nuint bufferLen, out nuint written);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ghostty_focus_encode")]
    public static extern GhosttyResult FocusEncode(GhosttyFocusEvent @event, byte* buffer, nuint bufferLength, out nuint written);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ghostty_build_info")]
    public static extern GhosttyResult BuildInfo(GhosttyBuildInfoData data, void* output);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ghostty_color_palette_default")]
    public static extern void ColorPaletteDefault(GhosttyColorRgb* output);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ghostty_sys_set")]
    public static extern GhosttyResult SysSet(GhosttySysOption option, void* value);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ghostty_alloc")]
    public static extern byte* Alloc(GhosttyAllocator* allocator, nuint len);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ghostty_free")]
    public static extern void Free(GhosttyAllocator* allocator, byte* ptr, nuint len);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ghostty_kitty_graphics_get")]
    public static extern GhosttyResult KittyGraphicsGet(nint graphics, GhosttyKittyGraphicsData data, void* output);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ghostty_kitty_graphics_image")]
    public static extern nint KittyGraphicsImage(nint graphics, uint imageId);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ghostty_kitty_graphics_image_get")]
    public static extern GhosttyResult KittyGraphicsImageGet(nint image, GhosttyKittyGraphicsImageData data, void* output);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ghostty_kitty_graphics_placement_iterator_new")]
    public static extern GhosttyResult KittyGraphicsPlacementIteratorNew(nint allocator, out nint iterator);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ghostty_kitty_graphics_placement_iterator_free")]
    public static extern void KittyGraphicsPlacementIteratorFree(nint iterator);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ghostty_kitty_graphics_placement_iterator_set")]
    public static extern GhosttyResult KittyGraphicsPlacementIteratorSet(
        nint iterator, GhosttyKittyGraphicsPlacementIteratorOption option, void* value);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ghostty_kitty_graphics_placement_next")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool KittyGraphicsPlacementNext(nint iterator);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ghostty_kitty_graphics_placement_get")]
    public static extern GhosttyResult KittyGraphicsPlacementGet(nint iterator, GhosttyKittyGraphicsPlacementData data, void* output);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ghostty_kitty_graphics_placement_render_info")]
    public static extern GhosttyResult KittyGraphicsPlacementRenderInfoGet(
        nint iterator, nint image, nint terminal, GhosttyKittyGraphicsPlacementRenderInfo* output);
}
