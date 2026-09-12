using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace modterm.Ghostty;

internal struct GhosttyCell
{
    public string Text;
    public GhosttyColorRgb Fg;
    public GhosttyColorRgb Bg;
    public bool FgDefault;
    public bool BgDefault;
    public bool Bold;
    public bool Italic;
    public bool Faint;
    public bool Inverse;
    public bool Invisible;
    public bool Strikethrough;
    public bool Selected;
    public int Underline;
    public byte Width;
}

internal sealed class KittyImageBlit
{
    public uint ImageId;
    public ulong Generation;
    public int Z;
    public int ViewportColumn;
    public int ViewportRow;
    public int XOffset;
    public int YOffset;
    public int PixelWidth;
    public int PixelHeight;
    public int SourceX;
    public int SourceY;
    public int SourceWidth;
    public int SourceHeight;
    public int ImageWidth;
    public int ImageHeight;
    public byte[] Rgba = [];
    public bool BelowText;
}

internal sealed class GhosttyFrame
{
    public int Rows;
    public int Cols;
    public GhosttyCell[] Cells = [];
    public int CursorX;
    public int CursorY;
    public bool CursorVisible;
    public bool CursorBlinking;
    public bool CursorInViewport;
    public GhosttyRenderStateCursorVisualStyle CursorStyle;
    public GhosttyColorRgb DefaultFg;
    public GhosttyColorRgb DefaultBg;
    public readonly List<KittyImageBlit> Images = new();
}

/// <summary>
/// Owns a libghostty-vt terminal, render-state snapshot, and input encoders.
/// All public methods except the constructor take the internal lock.
/// </summary>
internal sealed unsafe class GhosttyVtSession : IDisposable
{
    public const int DefaultScrollbackLines = 5000;

    private static readonly GhosttyMode ModeDecckm = GhosttyMode.Dec(1);
    private static readonly GhosttyMode ModeReverse = GhosttyMode.Dec(5);
    private static readonly GhosttyMode ModeMouseX10 = GhosttyMode.Dec(9);
    private static readonly GhosttyMode ModeMouseNormal = GhosttyMode.Dec(1000);
    private static readonly GhosttyMode ModeMouseButton = GhosttyMode.Dec(1002);
    private static readonly GhosttyMode ModeMouseAny = GhosttyMode.Dec(1003);
    private static readonly GhosttyMode ModeFocusEvent = GhosttyMode.Dec(1004);
    private static readonly GhosttyMode ModeAltScreen = GhosttyMode.Dec(1047);
    private static readonly GhosttyMode ModeAltScreenSave = GhosttyMode.Dec(1049);
    private static readonly GhosttyMode ModeBracketedPaste = GhosttyMode.Dec(2004);

    private readonly object _gate = new();
    private readonly byte[] _answerbackUtf8 = Encoding.UTF8.GetBytes("modterm");
    private readonly GCHandle _answerbackHandle;

    private nint _terminal;
    private nint _renderState;
    private nint _rowIterator;
    private nint _rowCells;
    private nint _keyEncoder;
    private nint _keyEvent;
    private nint _mouseEncoder;
    private nint _mouseEvent;
    private nint _kittyIterator;
    private bool _kittyIteratorReady;
    private bool _disposed;
    private bool _kittyGraphicsSupported;
    private bool _darkScheme = true;

    private ushort _cols;
    private ushort _rows;
    private uint _cellWidthPx;
    private uint _cellHeightPx;

    private GhosttyWritePtyCallback? _writePty;
    private GhosttySizeCallback? _size;
    private GhosttyColorSchemeCallback? _colorScheme;
    private GhosttyDeviceAttributesCallback? _deviceAttributes;
    private GhosttyEnquiryCallback? _enquiry;
    private GhosttyEnquiryCallback? _xtversion;
    private GhosttyClipboardWriteCallback? _clipboard;

    public event Action<byte[]>? WritePty;
    public event Action<string>? ClipboardText;

    public GhosttyVtSession(ushort columns, ushort rows, nuint scrollbackLines = DefaultScrollbackLines)
    {
        _cols = Math.Max((ushort)1, columns);
        _rows = Math.Max((ushort)1, rows);
        _answerbackHandle = GCHandle.Alloc(_answerbackUtf8, GCHandleType.Pinned);

        GhosttyNative.ThrowIfFailed(
            GhosttyNative.TerminalNew(nint.Zero, out _terminal, _cols, _rows),
            "ghostty_terminal_new");
        try
        {
            nuint lines = scrollbackLines;
            GhosttyNative.ThrowIfFailed(
                GhosttyNative.TerminalSet(_terminal, GhosttyTerminalOption.ScrollbackMaxLines, &lines),
                "ghostty_terminal_set(scrollback_max_lines)");

            GhosttyNative.ThrowIfFailed(GhosttyNative.RenderStateNew(nint.Zero, out _renderState), "ghostty_render_state_new");
            GhosttyNative.ThrowIfFailed(GhosttyNative.RenderStateRowIteratorNew(nint.Zero, out _rowIterator), "ghostty_render_state_row_iterator_new");
            GhosttyNative.ThrowIfFailed(GhosttyNative.RenderStateRowCellsNew(nint.Zero, out _rowCells), "ghostty_render_state_row_cells_new");
            GhosttyNative.ThrowIfFailed(GhosttyNative.KeyEncoderNew(nint.Zero, out _keyEncoder), "ghostty_key_encoder_new");
            GhosttyNative.ThrowIfFailed(GhosttyNative.KeyEventNew(nint.Zero, out _keyEvent), "ghostty_key_event_new");
            GhosttyNative.ThrowIfFailed(GhosttyNative.MouseEncoderNew(nint.Zero, out _mouseEncoder), "ghostty_mouse_encoder_new");
            GhosttyNative.ThrowIfFailed(GhosttyNative.MouseEventNew(nint.Zero, out _mouseEvent), "ghostty_mouse_event_new");

            bool kitty = false;
            if (GhosttyNative.BuildInfo(GhosttyBuildInfoData.KittyGraphics, &kitty) == GhosttyResult.Success)
                _kittyGraphicsSupported = kitty;

            if (_kittyGraphicsSupported)
            {
                GhosttyNative.ThrowIfFailed(
                    GhosttyNative.KittyGraphicsPlacementIteratorNew(nint.Zero, out _kittyIterator),
                    "ghostty_kitty_graphics_placement_iterator_new");
                _kittyIteratorReady = true;
                ConfigureKitty();
            }

            WireCallbacks();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public bool ApplicationCursor
    {
        get { lock (_gate) return GetMode(ModeDecckm); }
    }

    public bool AlternateScreen
    {
        get
        {
            lock (_gate)
            {
                GhosttyTerminalScreen screen = default;
                if (GhosttyNative.TerminalGet(_terminal, GhosttyTerminalData.ActiveScreen, &screen) == GhosttyResult.Success
                    && screen == GhosttyTerminalScreen.Alternate)
                    return true;
                return GetMode(ModeAltScreen) || GetMode(ModeAltScreenSave);
            }
        }
    }

    public bool BracketedPaste
    {
        get { lock (_gate) return GetMode(ModeBracketedPaste); }
    }

    public bool FocusReporting
    {
        get { lock (_gate) return GetMode(ModeFocusEvent); }
    }

    public bool MouseTracking
    {
        get
        {
            lock (_gate)
            {
                bool tracking = false;
                if (GhosttyNative.TerminalGet(_terminal, GhosttyTerminalData.MouseTracking, &tracking) == GhosttyResult.Success)
                    return tracking;
                return GetMode(ModeMouseX10) || GetMode(ModeMouseNormal) || GetMode(ModeMouseButton) || GetMode(ModeMouseAny);
            }
        }
    }

    public bool MouseX10
    {
        get { lock (_gate) return GetMode(ModeMouseX10); }
    }

    public bool MouseButtonTracking
    {
        get { lock (_gate) return GetMode(ModeMouseButton) || GetMode(ModeMouseAny); }
    }

    public bool MouseAnyTracking
    {
        get { lock (_gate) return GetMode(ModeMouseAny); }
    }

    public bool IsScrolledBack
    {
        get
        {
            lock (_gate)
            {
                bool active = true;
                if (GhosttyNative.TerminalGet(_terminal, GhosttyTerminalData.ViewportActive, &active) == GhosttyResult.Success)
                    return !active;
                return false;
            }
        }
    }

    public void Write(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
            return;

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            fixed (byte* ptr = data)
                GhosttyNative.TerminalVtWrite(_terminal, ptr, (nuint)data.Length);
        }
    }

    public void Resize(int columns, int rows, uint cellWidthPx, uint cellHeightPx)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _cols = (ushort)Math.Clamp(columns, 1, ushort.MaxValue);
            _rows = (ushort)Math.Clamp(rows, 1, ushort.MaxValue);
            _cellWidthPx = cellWidthPx;
            _cellHeightPx = cellHeightPx;
            GhosttyNative.ThrowIfFailed(
                GhosttyNative.TerminalResize(_terminal, _cols, _rows, cellWidthPx, cellHeightPx),
                "ghostty_terminal_resize");
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            GhosttyNative.TerminalReset(_terminal);
            ClearSelectionLocked();
            GhosttyNative.TerminalScrollViewport(_terminal, GhosttyTerminalScrollViewport.Bottom());
        }
    }

    public void ScrollBackBy(int rowsTowardHistory)
    {
        if (rowsTowardHistory == 0)
            return;

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            GhosttyTerminalScrollbar bar = GetScrollbarLocked();
            ulong visible = bar.Length;
            ulong maxOffset = bar.Total > visible ? bar.Total - visible : 0;
            long next = (long)bar.Offset - rowsTowardHistory;
            if (next < 0)
                next = 0;
            if ((ulong)next > maxOffset)
                next = (long)maxOffset;
            GhosttyNative.TerminalScrollViewport(
                _terminal,
                GhosttyTerminalScrollViewport.AbsoluteRow((nuint)next));
        }
    }

    public void ScrollToBottom()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            GhosttyNative.TerminalScrollViewport(_terminal, GhosttyTerminalScrollViewport.Bottom());
        }
    }

    public void ApplyPalette(ReadOnlySpan<GhosttyColorRgb> palette, GhosttyColorRgb foreground, GhosttyColorRgb background)
    {
        if (palette.Length != 256)
            throw new ArgumentException("Palette must contain 256 colors.", nameof(palette));

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            GhosttyNative.ThrowIfFailed(
                GhosttyNative.TerminalSet(_terminal, GhosttyTerminalOption.ColorForeground, &foreground),
                "ghostty_terminal_set(color_foreground)");
            GhosttyNative.ThrowIfFailed(
                GhosttyNative.TerminalSet(_terminal, GhosttyTerminalOption.ColorBackground, &background),
                "ghostty_terminal_set(color_background)");
            fixed (GhosttyColorRgb* ptr = palette)
            {
                GhosttyNative.ThrowIfFailed(
                    GhosttyNative.TerminalSet(_terminal, GhosttyTerminalOption.ColorPalette, ptr),
                    "ghostty_terminal_set(color_palette)");
            }

            int luminance = (background.R * 299 + background.G * 587 + background.B * 114) / 1000;
            _darkScheme = luminance < 128;
        }
    }

    public void SetDefaultCursor(bool underline, bool blink)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            GhosttyTerminalCursorStyle style = underline
                ? GhosttyTerminalCursorStyle.Underline
                : GhosttyTerminalCursorStyle.Block;
            GhosttyNative.ThrowIfFailed(
                GhosttyNative.TerminalSet(_terminal, GhosttyTerminalOption.DefaultCursorStyle, &style),
                "ghostty_terminal_set(default_cursor_style)");
            GhosttyNative.ThrowIfFailed(
                GhosttyNative.TerminalSet(_terminal, GhosttyTerminalOption.DefaultCursorBlink, &blink),
                "ghostty_terminal_set(default_cursor_blink)");
        }
    }

    public static void FillDefaultPalette(Span<GhosttyColorRgb> destination)
    {
        if (destination.Length < 256)
            throw new ArgumentException("Destination must hold 256 colors.", nameof(destination));
        fixed (GhosttyColorRgb* ptr = destination)
            GhosttyNative.ColorPaletteDefault(ptr);
    }

    public void SetSelection(int startCol, int startRow, int endCol, int endRow)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!TryGridRefLocked(startCol, startRow, out GhosttyGridRef start)
                || !TryGridRefLocked(endCol, endRow, out GhosttyGridRef end))
            {
                ClearSelectionLocked();
                return;
            }

            GhosttySelectionRange range = GhosttySelectionRange.CreateSized();
            range.Start = start;
            range.End = end;
            GhosttyNative.ThrowIfFailed(
                GhosttyNative.TerminalSet(_terminal, GhosttyTerminalOption.Selection, &range),
                "ghostty_terminal_set(selection)");
        }
    }

    public void ClearSelection()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ClearSelectionLocked();
        }
    }

    public string GetSelectedText()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            GhosttyTerminalSelectionFormatOptions options = GhosttyTerminalSelectionFormatOptions.CreateSized();
            options.Format = GhosttyFormatterFormat.Plain;
            options.Unwrap = true;
            options.Trim = true;

            GhosttyResult probe = GhosttyNative.TerminalSelectionFormatBuffer(
                _terminal, options, null, 0, out nuint required);
            if (probe == GhosttyResult.NoValue || required == 0)
                return string.Empty;
            if (probe != GhosttyResult.OutOfSpace)
                GhosttyNative.ThrowIfFailed(probe, "ghostty_terminal_selection_format_buf(probe)");

            byte[] buffer = new byte[checked((int)required)];
            fixed (byte* ptr = buffer)
            {
                GhosttyNative.ThrowIfFailed(
                    GhosttyNative.TerminalSelectionFormatBuffer(
                        _terminal, options, ptr, (nuint)buffer.Length, out nuint written),
                    "ghostty_terminal_selection_format_buf");
                return Encoding.UTF8.GetString(buffer, 0, checked((int)written));
            }
        }
    }

    public byte[] EncodeKey(
        GhosttyVtKey key,
        GhosttyVtKeyAction action,
        GhosttyVtMods mods,
        string? text,
        uint unshiftedCodepoint)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            GhosttyNative.KeyEncoderSetoptFromTerminal(_keyEncoder, _terminal);
            GhosttyNative.KeyEventSetAction(_keyEvent, action);
            GhosttyNative.KeyEventSetKey(_keyEvent, key);
            GhosttyNative.KeyEventSetMods(_keyEvent, mods);
            GhosttyNative.KeyEventSetConsumedMods(_keyEvent, GhosttyVtMods.None);
            GhosttyNative.KeyEventSetComposing(_keyEvent, false);
            GhosttyNative.KeyEventSetUnshiftedCodepoint(_keyEvent, unshiftedCodepoint);
            SetKeyTextLocked(text);
            return EncodeKeyLocked();
        }
    }

    public byte[] EncodeMouse(
        GhosttyMouseAction action,
        GhosttyMouseButtonId? button,
        float x,
        float y,
        GhosttyVtMods mods,
        bool anyButtonPressed,
        uint screenWidth,
        uint screenHeight,
        uint cellWidth,
        uint cellHeight,
        uint paddingLeft,
        uint paddingTop)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            GhosttyNative.MouseEncoderSetoptFromTerminal(_mouseEncoder, _terminal);
            GhosttyMouseEncoderSize size = GhosttyMouseEncoderSize.CreateSized();
            size.ScreenWidth = screenWidth;
            size.ScreenHeight = screenHeight;
            size.CellWidth = cellWidth;
            size.CellHeight = cellHeight;
            size.PaddingLeft = paddingLeft;
            size.PaddingTop = paddingTop;
            GhosttyNative.MouseEncoderSetopt(_mouseEncoder, GhosttyMouseEncoderOption.Size, &size);
            bool pressed = anyButtonPressed;
            GhosttyNative.MouseEncoderSetopt(_mouseEncoder, GhosttyMouseEncoderOption.AnyButtonPressed, &pressed);
            bool trackLast = true;
            GhosttyNative.MouseEncoderSetopt(_mouseEncoder, GhosttyMouseEncoderOption.TrackLastCell, &trackLast);

            GhosttyNative.MouseEventSetAction(_mouseEvent, action);
            GhosttyNative.MouseEventSetMods(_mouseEvent, mods);
            GhosttyNative.MouseEventSetPosition(_mouseEvent, new GhosttyMousePosition { X = x, Y = y });
            if (button is GhosttyMouseButtonId id)
                GhosttyNative.MouseEventSetButton(_mouseEvent, id);
            else
                GhosttyNative.MouseEventClearButton(_mouseEvent);

            GhosttyResult probe = GhosttyNative.MouseEncoderEncode(_mouseEncoder, _mouseEvent, null, 0, out nuint needed);
            if (probe != GhosttyResult.OutOfSpace && probe != GhosttyResult.Success)
                GhosttyNative.ThrowIfFailed(probe, "ghostty_mouse_encoder_encode(probe)");
            if (needed == 0)
                return [];

            byte[] data = new byte[checked((int)needed)];
            fixed (byte* ptr = data)
            {
                GhosttyNative.ThrowIfFailed(
                    GhosttyNative.MouseEncoderEncode(_mouseEncoder, _mouseEvent, ptr, (nuint)data.Length, out nuint written),
                    "ghostty_mouse_encoder_encode");
                if (written == (nuint)data.Length)
                    return data;
                byte[] trimmed = new byte[checked((int)written)];
                Array.Copy(data, trimmed, trimmed.Length);
                return trimmed;
            }
        }
    }

    public byte[] EncodePaste(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        byte[] utf8 = Encoding.UTF8.GetBytes(text);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            bool bracketed = GetMode(ModeBracketedPaste);
            return EncodePasteLocked(utf8, bracketed);
        }
    }

    public byte[] EncodeFocus(bool gained)
    {
        GhosttyFocusEvent ev = gained ? GhosttyFocusEvent.Gained : GhosttyFocusEvent.Lost;
        GhosttyResult probe = GhosttyNative.FocusEncode(ev, null, 0, out nuint needed);
        if (probe != GhosttyResult.OutOfSpace)
            GhosttyNative.ThrowIfFailed(probe, "ghostty_focus_encode(probe)");
        if (needed == 0)
            return [];
        byte[] data = new byte[checked((int)needed)];
        fixed (byte* ptr = data)
        {
            GhosttyNative.ThrowIfFailed(
                GhosttyNative.FocusEncode(ev, ptr, (nuint)data.Length, out nuint written),
                "ghostty_focus_encode");
            if (written == (nuint)data.Length)
                return data;
            byte[] trimmed = new byte[checked((int)written)];
            Array.Copy(data, trimmed, trimmed.Length);
            return trimmed;
        }
    }

    public void Capture(GhosttyFrame frame)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            GhosttyNative.ThrowIfFailed(GhosttyNative.RenderStateUpdate(_renderState, _terminal), "ghostty_render_state_update");

            ushort cols = 0, rows = 0;
            GhosttyNative.ThrowIfFailed(
                GhosttyNative.RenderStateGet(_renderState, GhosttyRenderStateData.Cols, &cols),
                "ghostty_render_state_get(cols)");
            GhosttyNative.ThrowIfFailed(
                GhosttyNative.RenderStateGet(_renderState, GhosttyRenderStateData.Rows, &rows),
                "ghostty_render_state_get(rows)");

            frame.Cols = cols;
            frame.Rows = rows;
            EnsureCellBuffer(frame, cols * rows);
            frame.Images.Clear();

            GhosttyRenderStateColors colors = GhosttyRenderStateColors.CreateSized();
            GhosttyNative.ThrowIfFailed(GhosttyNative.RenderStateColorsGet(_renderState, ref colors), "ghostty_render_state_colors_get");
            frame.DefaultFg = colors.Foreground;
            frame.DefaultBg = colors.Background;

            bool cursorVisible = true;
            GhosttyNative.RenderStateGet(_renderState, GhosttyRenderStateData.CursorVisible, &cursorVisible);
            bool cursorBlinking = true;
            GhosttyNative.RenderStateGet(_renderState, GhosttyRenderStateData.CursorBlinking, &cursorBlinking);
            GhosttyRenderStateCursorVisualStyle cursorStyle = GhosttyRenderStateCursorVisualStyle.Block;
            GhosttyNative.RenderStateGet(_renderState, GhosttyRenderStateData.CursorVisualStyle, &cursorStyle);
            bool hasCursor = false;
            GhosttyNative.RenderStateGet(_renderState, GhosttyRenderStateData.CursorViewportHasValue, &hasCursor);
            ushort cursorX = 0, cursorY = 0;
            if (hasCursor)
            {
                GhosttyNative.RenderStateGet(_renderState, GhosttyRenderStateData.CursorViewportX, &cursorX);
                GhosttyNative.RenderStateGet(_renderState, GhosttyRenderStateData.CursorViewportY, &cursorY);
            }

            frame.CursorVisible = cursorVisible;
            frame.CursorBlinking = cursorBlinking;
            frame.CursorStyle = cursorStyle;
            frame.CursorInViewport = hasCursor;
            frame.CursorX = cursorX;
            frame.CursorY = cursorY;

            nint iterator = _rowIterator;
            GhosttyNative.ThrowIfFailed(
                GhosttyNative.RenderStateGet(_renderState, GhosttyRenderStateData.RowIterator, &iterator),
                "ghostty_render_state_get(row_iterator)");
            _rowIterator = iterator;

            int rowIndex = 0;
            while (rowIndex < rows && GhosttyNative.RenderStateRowIteratorNext(_rowIterator))
            {
                nint cells = _rowCells;
                GhosttyNative.ThrowIfFailed(
                    GhosttyNative.RenderStateRowGet(_rowIterator, GhosttyRenderStateRowData.Cells, &cells),
                    "ghostty_render_state_row_get(cells)");
                _rowCells = cells;

                int colIndex = 0;
                while (colIndex < cols && GhosttyNative.RenderStateRowCellsNext(_rowCells))
                {
                    frame.Cells[rowIndex * cols + colIndex] = ReadCellLocked(colors);
                    colIndex++;
                }

                for (; colIndex < cols; colIndex++)
                    frame.Cells[rowIndex * cols + colIndex] = EmptyCell(colors);

                rowIndex++;
            }

            for (; rowIndex < rows; rowIndex++)
            {
                for (int colIndex = 0; colIndex < cols; colIndex++)
                    frame.Cells[rowIndex * cols + colIndex] = EmptyCell(colors);
            }

            if (_kittyGraphicsSupported)
                CaptureKittyLocked(frame);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;

            if (_kittyIterator != nint.Zero)
            {
                GhosttyNative.KittyGraphicsPlacementIteratorFree(_kittyIterator);
                _kittyIterator = nint.Zero;
            }
            if (_mouseEvent != nint.Zero)
            {
                GhosttyNative.MouseEventFree(_mouseEvent);
                _mouseEvent = nint.Zero;
            }
            if (_mouseEncoder != nint.Zero)
            {
                GhosttyNative.MouseEncoderFree(_mouseEncoder);
                _mouseEncoder = nint.Zero;
            }
            if (_keyEvent != nint.Zero)
            {
                GhosttyNative.KeyEventFree(_keyEvent);
                _keyEvent = nint.Zero;
            }
            if (_keyEncoder != nint.Zero)
            {
                GhosttyNative.KeyEncoderFree(_keyEncoder);
                _keyEncoder = nint.Zero;
            }
            if (_rowCells != nint.Zero)
            {
                GhosttyNative.RenderStateRowCellsFree(_rowCells);
                _rowCells = nint.Zero;
            }
            if (_rowIterator != nint.Zero)
            {
                GhosttyNative.RenderStateRowIteratorFree(_rowIterator);
                _rowIterator = nint.Zero;
            }
            if (_renderState != nint.Zero)
            {
                GhosttyNative.RenderStateFree(_renderState);
                _renderState = nint.Zero;
            }
            if (_terminal != nint.Zero)
            {
                GhosttyNative.TerminalFree(_terminal);
                _terminal = nint.Zero;
            }

            if (_answerbackHandle.IsAllocated)
                _answerbackHandle.Free();
        }
    }

    private void ConfigureKitty()
    {
        ulong storage = 32UL * 1024UL * 1024UL;
        GhosttyNative.TerminalSet(_terminal, GhosttyTerminalOption.KittyImageStorageLimit, &storage);
        ulong apc = 64UL * 1024UL * 1024UL;
        GhosttyNative.TerminalSet(_terminal, GhosttyTerminalOption.ApcMaxBytesKitty, &apc);
        bool enabled = true;
        GhosttyNative.TerminalSet(_terminal, GhosttyTerminalOption.KittyImageMediumFile, &enabled);
        GhosttyNative.TerminalSet(_terminal, GhosttyTerminalOption.KittyImageMediumSharedMemory, &enabled);
        byte[] dir = Encoding.UTF8.GetBytes(Path.GetTempPath());
        fixed (byte* dirPtr = dir)
        {
            GhosttyString native = new((nint)dirPtr, (nuint)dir.Length);
            GhosttyNative.TerminalSet(_terminal, GhosttyTerminalOption.KittyImageMediumTempFile, &native);
        }
    }

    private void WireCallbacks()
    {
        _writePty = OnWritePty;
        _size = OnSize;
        _colorScheme = OnColorScheme;
        _deviceAttributes = OnDeviceAttributes;
        _enquiry = OnEnquiry;
        _xtversion = OnEnquiry;
        _clipboard = OnClipboardWrite;

        nint writePty = Marshal.GetFunctionPointerForDelegate(_writePty);
        nint size = Marshal.GetFunctionPointerForDelegate(_size);
        nint colorScheme = Marshal.GetFunctionPointerForDelegate(_colorScheme);
        nint da = Marshal.GetFunctionPointerForDelegate(_deviceAttributes);
        nint enquiry = Marshal.GetFunctionPointerForDelegate(_enquiry);
        nint xtversion = Marshal.GetFunctionPointerForDelegate(_xtversion);
        nint clipboard = Marshal.GetFunctionPointerForDelegate(_clipboard);

        GhosttyNative.ThrowIfFailed(GhosttyNative.TerminalSet(_terminal, GhosttyTerminalOption.WritePty, (void*)writePty), "set write-pty");
        GhosttyNative.ThrowIfFailed(GhosttyNative.TerminalSet(_terminal, GhosttyTerminalOption.Size, (void*)size), "set size");
        GhosttyNative.ThrowIfFailed(GhosttyNative.TerminalSet(_terminal, GhosttyTerminalOption.ColorScheme, (void*)colorScheme), "set color-scheme");
        GhosttyNative.ThrowIfFailed(GhosttyNative.TerminalSet(_terminal, GhosttyTerminalOption.DeviceAttributes, (void*)da), "set da");
        GhosttyNative.ThrowIfFailed(GhosttyNative.TerminalSet(_terminal, GhosttyTerminalOption.Enquiry, (void*)enquiry), "set enquiry");
        GhosttyNative.ThrowIfFailed(GhosttyNative.TerminalSet(_terminal, GhosttyTerminalOption.Xtversion, (void*)xtversion), "set xtversion");
        GhosttyNative.ThrowIfFailed(GhosttyNative.TerminalSet(_terminal, GhosttyTerminalOption.ClipboardWrite, (void*)clipboard), "set clipboard");
    }

    private bool GetMode(GhosttyMode mode)
    {
        if (GhosttyNative.TerminalModeGet(_terminal, mode, out bool value) != GhosttyResult.Success)
            return false;
        return value;
    }

    private GhosttyTerminalScrollbar GetScrollbarLocked()
    {
        GhosttyTerminalScrollbar bar = default;
        GhosttyNative.ThrowIfFailed(
            GhosttyNative.TerminalGet(_terminal, GhosttyTerminalData.Scrollbar, &bar),
            "ghostty_terminal_get(scrollbar)");
        return bar;
    }

    private void ClearSelectionLocked()
    {
        GhosttyNative.TerminalSet(_terminal, GhosttyTerminalOption.Selection, null);
    }

    private bool TryGridRefLocked(int col, int row, out GhosttyGridRef reference)
    {
        reference = GhosttyGridRef.CreateSized();
        int safeCol = Math.Clamp(col, 0, Math.Max(0, _cols - 1));
        int safeRow = Math.Clamp(row, 0, Math.Max(0, _rows - 1));
        GhosttyPoint point = GhosttyPoint.Viewport((ushort)safeCol, (uint)safeRow);
        GhosttyResult result = GhosttyNative.TerminalGridRef(_terminal, point, ref reference);
        return result == GhosttyResult.Success;
    }

    private void SetKeyTextLocked(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            GhosttyNative.KeyEventSetUtf8(_keyEvent, null, 0);
            return;
        }

        byte[] utf8 = Encoding.UTF8.GetBytes(text);
        fixed (byte* ptr = utf8)
            GhosttyNative.KeyEventSetUtf8(_keyEvent, ptr, (nuint)utf8.Length);
    }

    private byte[] EncodeKeyLocked()
    {
        GhosttyResult probe = GhosttyNative.KeyEncoderEncode(_keyEncoder, _keyEvent, null, 0, out nuint needed);
        if (probe != GhosttyResult.OutOfSpace && probe != GhosttyResult.Success)
            GhosttyNative.ThrowIfFailed(probe, "ghostty_key_encoder_encode(probe)");
        if (needed == 0)
            return [];

        byte[] data = new byte[checked((int)needed)];
        fixed (byte* ptr = data)
        {
            GhosttyNative.ThrowIfFailed(
                GhosttyNative.KeyEncoderEncode(_keyEncoder, _keyEvent, ptr, (nuint)data.Length, out nuint written),
                "ghostty_key_encoder_encode");
            if (written == (nuint)data.Length)
                return data;
            byte[] trimmed = new byte[checked((int)written)];
            Array.Copy(data, trimmed, trimmed.Length);
            return trimmed;
        }
    }

    private static byte[] EncodePasteLocked(byte[] utf8, bool bracketed)
    {
        fixed (byte* dataPtr = utf8)
        {
            GhosttyResult probe = GhosttyNative.PasteEncode(
                dataPtr, (nuint)utf8.Length, bracketed, null, 0, out nuint required);
            if (probe != GhosttyResult.OutOfSpace)
                GhosttyNative.ThrowIfFailed(probe, "ghostty_paste_encode(probe)");
            if (required == 0)
                return [];
            byte[] result = new byte[checked((int)required)];
            fixed (byte* outPtr = result)
            {
                GhosttyNative.ThrowIfFailed(
                    GhosttyNative.PasteEncode(
                        dataPtr, (nuint)utf8.Length, bracketed, outPtr, (nuint)result.Length, out nuint written),
                    "ghostty_paste_encode");
                if (written == (nuint)result.Length)
                    return result;
                byte[] trimmed = new byte[checked((int)written)];
                Array.Copy(result, trimmed, trimmed.Length);
                return trimmed;
            }
        }
    }

    private static void EnsureCellBuffer(GhosttyFrame frame, int count)
    {
        if (frame.Cells.Length < count)
            frame.Cells = new GhosttyCell[count];
    }

    private static GhosttyCell EmptyCell(in GhosttyRenderStateColors colors)
    {
        return new GhosttyCell
        {
            Text = " ",
            Fg = colors.Foreground,
            Bg = colors.Background,
            FgDefault = true,
            BgDefault = true,
            Width = 1,
        };
    }

    private GhosttyCell ReadCellLocked(in GhosttyRenderStateColors colors)
    {
        ulong raw = 0;
        GhosttyNative.RenderStateRowCellsGet(_rowCells, GhosttyRenderStateRowCellsData.Raw, &raw);

        GhosttyCellWide wide = GhosttyCellWide.Narrow;
        GhosttyNative.CellGet(raw, GhosttyCellData.Wide, &wide);

        GhosttyStyle style = GhosttyStyle.CreateSized();
        GhosttyNative.RenderStateRowCellsGet(_rowCells, GhosttyRenderStateRowCellsData.Style, &style);

        bool selected = false;
        GhosttyNative.RenderStateRowCellsGet(_rowCells, GhosttyRenderStateRowCellsData.Selected, &selected);

        GhosttyColorRgb fg = colors.Foreground;
        GhosttyColorRgb bg = colors.Background;
        bool hasFg = GhosttyNative.RenderStateRowCellsGet(
            _rowCells, GhosttyRenderStateRowCellsData.ForegroundColor, &fg) == GhosttyResult.Success;
        bool hasBg = GhosttyNative.RenderStateRowCellsGet(
            _rowCells, GhosttyRenderStateRowCellsData.BackgroundColor, &bg) == GhosttyResult.Success;

        string text = ReadGraphemeLocked();
        byte width = wide switch
        {
            GhosttyCellWide.Wide => 2,
            GhosttyCellWide.SpacerHead or GhosttyCellWide.SpacerTail => 0,
            _ => 1,
        };

        return new GhosttyCell
        {
            Text = text,
            Fg = hasFg ? fg : colors.Foreground,
            Bg = hasBg ? bg : colors.Background,
            FgDefault = !hasFg || ColorsEqual(fg, colors.Foreground),
            BgDefault = !hasBg || ColorsEqual(bg, colors.Background),
            Bold = style.Bold,
            Italic = style.Italic,
            Faint = style.Faint,
            Inverse = style.Inverse,
            Invisible = style.Invisible,
            Strikethrough = style.Strikethrough,
            Selected = selected,
            Underline = style.Underline,
            Width = width,
        };
    }

    private string ReadGraphemeLocked()
    {
        GhosttyBuffer buffer = default;
        GhosttyResult probe = GhosttyNative.RenderStateRowCellsGet(
            _rowCells, GhosttyRenderStateRowCellsData.GraphemesUtf8, &buffer);
        if (probe == GhosttyResult.Success && buffer.Length == 0)
            return " ";
        if (probe != GhosttyResult.OutOfSpace && probe != GhosttyResult.Success)
            return " ";

        int length = checked((int)buffer.Length);
        if (length <= 0)
            return " ";

        byte[] utf8 = new byte[length];
        fixed (byte* ptr = utf8)
        {
            buffer.Pointer = ptr;
            buffer.Capacity = (nuint)utf8.Length;
            if (GhosttyNative.RenderStateRowCellsGet(
                    _rowCells, GhosttyRenderStateRowCellsData.GraphemesUtf8, &buffer) != GhosttyResult.Success)
                return " ";
            string text = Encoding.UTF8.GetString(utf8, 0, checked((int)buffer.Length));
            return string.IsNullOrEmpty(text) ? " " : text;
        }
    }

    private void CaptureKittyLocked(GhosttyFrame frame)
    {
        if (!_kittyIteratorReady)
            return;

        nint graphics = nint.Zero;
        GhosttyResult result = GhosttyNative.TerminalGet(_terminal, GhosttyTerminalData.KittyGraphics, &graphics);
        if (result == GhosttyResult.NoValue || graphics == nint.Zero)
            return;
        if (result != GhosttyResult.Success)
            return;

        GhosttyKittyPlacementLayer layer = GhosttyKittyPlacementLayer.All;
        GhosttyNative.KittyGraphicsPlacementIteratorSet(
            _kittyIterator, GhosttyKittyGraphicsPlacementIteratorOption.Layer, &layer);
        nint iterator = _kittyIterator;
        if (GhosttyNative.KittyGraphicsGet(graphics, GhosttyKittyGraphicsData.PlacementIterator, &iterator) != GhosttyResult.Success)
            return;
        _kittyIterator = iterator;

        while (GhosttyNative.KittyGraphicsPlacementNext(_kittyIterator))
        {
            uint imageId = 0;
            GhosttyNative.KittyGraphicsPlacementGet(_kittyIterator, GhosttyKittyGraphicsPlacementData.ImageId, &imageId);
            if (imageId == 0)
                continue;

            nint image = GhosttyNative.KittyGraphicsImage(graphics, imageId);
            if (image == nint.Zero)
                continue;

            GhosttyKittyGraphicsPlacementRenderInfo info = GhosttyKittyGraphicsPlacementRenderInfo.CreateSized();
            if (GhosttyNative.KittyGraphicsPlacementRenderInfoGet(_kittyIterator, image, _terminal, &info) != GhosttyResult.Success
                || !info.ViewportVisible)
                continue;

            if (!TryCopyImageRgba(image, out byte[] rgba, out int width, out int height, out ulong generation))
                continue;

            int z = 0;
            GhosttyNative.KittyGraphicsPlacementGet(_kittyIterator, GhosttyKittyGraphicsPlacementData.Z, &z);
            uint xOff = 0, yOff = 0;
            GhosttyNative.KittyGraphicsPlacementGet(_kittyIterator, GhosttyKittyGraphicsPlacementData.XOffset, &xOff);
            GhosttyNative.KittyGraphicsPlacementGet(_kittyIterator, GhosttyKittyGraphicsPlacementData.YOffset, &yOff);

            frame.Images.Add(new KittyImageBlit
            {
                ImageId = imageId,
                Generation = generation,
                Z = z,
                ViewportColumn = info.ViewportColumn,
                ViewportRow = info.ViewportRow,
                XOffset = (int)xOff,
                YOffset = (int)yOff,
                PixelWidth = (int)info.PixelWidth,
                PixelHeight = (int)info.PixelHeight,
                SourceX = (int)info.SourceX,
                SourceY = (int)info.SourceY,
                SourceWidth = (int)info.SourceWidth,
                SourceHeight = (int)info.SourceHeight,
                ImageWidth = width,
                ImageHeight = height,
                Rgba = rgba,
                BelowText = z < 0,
            });
        }
    }

    private static bool TryCopyImageRgba(nint image, out byte[] rgba, out int width, out int height, out ulong generation)
    {
        rgba = [];
        width = 0;
        height = 0;
        generation = 0;

        uint w = 0, h = 0;
        GhosttyKittyImageFormat format = GhosttyKittyImageFormat.Rgba;
        nint dataPtr = nint.Zero;
        nuint dataLength = 0;
        if (GhosttyNative.KittyGraphicsImageGet(image, GhosttyKittyGraphicsImageData.Width, &w) != GhosttyResult.Success
            || GhosttyNative.KittyGraphicsImageGet(image, GhosttyKittyGraphicsImageData.Height, &h) != GhosttyResult.Success
            || GhosttyNative.KittyGraphicsImageGet(image, GhosttyKittyGraphicsImageData.Format, &format) != GhosttyResult.Success
            || GhosttyNative.KittyGraphicsImageGet(image, GhosttyKittyGraphicsImageData.DataPtr, &dataPtr) != GhosttyResult.Success
            || GhosttyNative.KittyGraphicsImageGet(image, GhosttyKittyGraphicsImageData.DataLength, &dataLength) != GhosttyResult.Success)
        {
            return false;
        }

        ulong generationValue = 0;
        GhosttyNative.KittyGraphicsImageGet(image, GhosttyKittyGraphicsImageData.Generation, &generationValue);
        generation = generationValue;
        if (dataPtr == nint.Zero || dataLength == 0 || w == 0 || h == 0)
            return false;

        byte[] raw = new byte[checked((int)dataLength)];
        Marshal.Copy(dataPtr, raw, 0, raw.Length);
        width = checked((int)w);
        height = checked((int)h);
        rgba = format switch
        {
            GhosttyKittyImageFormat.Rgba => raw,
            GhosttyKittyImageFormat.Rgb => ExpandRgb(raw, width, height),
            GhosttyKittyImageFormat.Gray => ExpandGray(raw, width, height),
            GhosttyKittyImageFormat.GrayAlpha => ExpandGrayAlpha(raw, width, height),
            _ => [],
        };
        return rgba.Length > 0;
    }

    private static byte[] ExpandRgb(byte[] rgb, int width, int height)
    {
        int pixels = checked(width * height);
        if (rgb.Length < pixels * 3)
            return [];
        byte[] rgba = new byte[pixels * 4];
        int s = 0, d = 0;
        for (int i = 0; i < pixels; i++)
        {
            rgba[d++] = rgb[s++];
            rgba[d++] = rgb[s++];
            rgba[d++] = rgb[s++];
            rgba[d++] = 255;
        }
        return rgba;
    }

    private static byte[] ExpandGray(byte[] gray, int width, int height)
    {
        int pixels = checked(width * height);
        if (gray.Length < pixels)
            return [];
        byte[] rgba = new byte[pixels * 4];
        int d = 0;
        for (int i = 0; i < pixels; i++)
        {
            byte y = gray[i];
            rgba[d++] = y;
            rgba[d++] = y;
            rgba[d++] = y;
            rgba[d++] = 255;
        }
        return rgba;
    }

    private static byte[] ExpandGrayAlpha(byte[] grayAlpha, int width, int height)
    {
        int pixels = checked(width * height);
        if (grayAlpha.Length < pixels * 2)
            return [];
        byte[] rgba = new byte[pixels * 4];
        int s = 0, d = 0;
        for (int i = 0; i < pixels; i++)
        {
            byte y = grayAlpha[s++];
            byte a = grayAlpha[s++];
            rgba[d++] = y;
            rgba[d++] = y;
            rgba[d++] = y;
            rgba[d++] = a;
        }
        return rgba;
    }

    private static bool ColorsEqual(GhosttyColorRgb a, GhosttyColorRgb b)
        => a.R == b.R && a.G == b.G && a.B == b.B;

    private void OnWritePty(nint terminal, nint userdata, nint data, nuint len)
    {
        if (len == 0 || WritePty is null)
            return;
        try
        {
            byte[] bytes = new byte[checked((int)len)];
            Marshal.Copy(data, bytes, 0, bytes.Length);
            WritePty.Invoke(bytes);
        }
        catch
        {
            // Native callbacks must not throw.
        }
    }

    private byte OnSize(nint terminal, nint userdata, GhosttySizeReportSize* size)
    {
        *size = new GhosttySizeReportSize
        {
            Rows = _rows,
            Columns = _cols,
            CellWidth = _cellWidthPx,
            CellHeight = _cellHeightPx,
        };
        return 1;
    }

    private byte OnColorScheme(nint terminal, nint userdata, GhosttyColorScheme* scheme)
    {
        *scheme = _darkScheme ? GhosttyColorScheme.Dark : GhosttyColorScheme.Light;
        return 1;
    }

    private byte OnDeviceAttributes(nint terminal, nint userdata, GhosttyDeviceAttributes* attributes)
    {
        *attributes = default;
        attributes->Primary.ConformanceLevel = 62;
        int count = 0;
        attributes->Primary.SetFeature(count++, 1);
        attributes->Primary.SetFeature(count++, 6);
        attributes->Primary.SetFeature(count++, 22);
        attributes->Primary.NumFeatures = (nuint)count;
        attributes->Secondary.DeviceType = 1;
        attributes->Secondary.FirmwareVersion = 10;
        attributes->Tertiary.UnitId = 0x00464F4F;
        return 1;
    }

    private GhosttyString OnEnquiry(nint terminal, nint userdata)
    {
        return new GhosttyString(_answerbackHandle.AddrOfPinnedObject(), (nuint)_answerbackUtf8.Length);
    }

    private GhosttyClipboardWriteResult OnClipboardWrite(
        nint terminal, nint userdata, GhosttyClipboardWrite* write)
    {
        if (write is null || ClipboardText is null)
            return GhosttyClipboardWriteResult.Unsupported;

        try
        {
            if (write->ContentsLength == 0 || write->Contents is null)
                return GhosttyClipboardWriteResult.InvalidData;

            for (nuint i = 0; i < write->ContentsLength; i++)
            {
                GhosttyClipboardContent content = write->Contents[i];
                string mime = content.Mime.ToUtf8String();
                if (mime.Length == 0 || mime.StartsWith("text/", StringComparison.OrdinalIgnoreCase))
                {
                    string text = content.Data.ToUtf8String();
                    if (text.Length > 0)
                        ClipboardText.Invoke(text);
                    return GhosttyClipboardWriteResult.Success;
                }
            }

            return GhosttyClipboardWriteResult.Unsupported;
        }
        catch
        {
            return GhosttyClipboardWriteResult.IoError;
        }
    }
}
