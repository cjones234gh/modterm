using System;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using modterm.Ghostty;

namespace modterm;

internal static unsafe class GhosttyPngDecoder
{
    private static GhosttySysDecodePngCallback? _callback;

    public static void Install()
    {
        _callback ??= Decode;
        nint function = System.Runtime.InteropServices.Marshal.GetFunctionPointerForDelegate(_callback);
        GhosttyNative.ThrowIfFailed(
            GhosttyNative.SysSet(GhosttySysOption.DecodePng, (void*)function),
            "ghostty_sys_set(decode_png)");
    }

    private static byte Decode(
        void* userdata,
        GhosttyAllocator* allocator,
        byte* data,
        nuint dataLength,
        GhosttySysImage* output)
    {
        if (data is null || output is null || dataLength == 0)
            return 0;

        try
        {
            byte[] png = new byte[checked((int)dataLength)];
            System.Runtime.InteropServices.Marshal.Copy((nint)data, png, 0, png.Length);

            using var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
            {
                writer.WriteBytes(png);
                writer.StoreAsync().AsTask().GetAwaiter().GetResult();
            }

            stream.Seek(0);
            BitmapDecoder decoder = BitmapDecoder.CreateAsync(stream).AsTask().GetAwaiter().GetResult();
            PixelDataProvider pixels = decoder.GetPixelDataAsync(
                BitmapPixelFormat.Rgba8,
                BitmapAlphaMode.Straight,
                new BitmapTransform(),
                ExifOrientationMode.IgnoreExifOrientation,
                ColorManagementMode.DoNotColorManage).AsTask().GetAwaiter().GetResult();
            byte[] rgba = pixels.DetachPixelData();
            uint width = decoder.PixelWidth;
            uint height = decoder.PixelHeight;
            nuint rgbaLength = (nuint)rgba.Length;
            byte* dest = GhosttyNative.Alloc(allocator, rgbaLength);
            if (dest is null)
                return 0;

            System.Runtime.InteropServices.Marshal.Copy(rgba, 0, (nint)dest, rgba.Length);
            output->Width = width;
            output->Height = height;
            output->Data = dest;
            output->DataLength = rgbaLength;
            return 1;
        }
        catch
        {
            return 0;
        }
    }
}
