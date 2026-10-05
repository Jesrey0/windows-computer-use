using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WindowsComputerUse.Core;

internal static class ScreenCapture
{
    public static ScreenshotResult Capture(nint? handle)
    {
        if (handle.HasValue) WindowsAutomation.RequireWindow(handle.Value);
        var x = Native.GetSystemMetrics(76);
        var y = Native.GetSystemMetrics(77);
        var width = Native.GetSystemMetrics(78);
        var height = Native.GetSystemMetrics(79);
        if (handle.HasValue)
        {
            if (!Native.GetWindowRect(handle.Value, out var rect))
                throw new AutomationException("capture_failed", "Could not get window bounds.");
            x = rect.Left; y = rect.Top;
            width = rect.Right - x; height = rect.Bottom - y;
        }
        // Bound both allocation and response size, including pathological virtual displays/windows.
        if (width <= 0 || height <= 0 || width > 16384 || height > 16384 ||
            (long)width * height > 40_000_000)
            throw new AutomationException("capture_too_large", "Capture area is empty or exceeds 40 megapixels.");

        var desktopDc = Native.GetDC(0);
        if (desktopDc == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        nint memoryDc = 0, bitmap = 0, previous = 0;
        try
        {
            memoryDc = Native.CreateCompatibleDC(desktopDc);
            if (memoryDc == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            var info = new Native.BitmapInfo { Size = (uint)Marshal.SizeOf<Native.BitmapInfo>(),
                Width = width, Height = -height, Planes = 1, BitCount = 32 };
            bitmap = Native.CreateDIBSection(desktopDc, ref info, 0, out var bits, 0, 0);
            if (bitmap == 0 || bits == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            previous = Native.SelectObject(memoryDc, bitmap);
            if (previous == 0 || previous == -1) throw new Win32Exception(Marshal.GetLastWin32Error());

            // PrintWindow can capture some covered windows. Providers may return false or blank
            // (notably GPU-backed/protected surfaces); BitBlt fallback captures visible pixels.
            if (handle is null || !Native.PrintWindow(handle.Value, memoryDc, 2))
            {
                if (!Native.BitBlt(memoryDc, 0, 0, width, height, desktopDc, x, y, 0x00CC0020 | 0x40000000))
                    throw new AutomationException("capture_failed", "Windows could not capture this surface.");
            }

            var stride = checked(width * 4);
            var pixels = new byte[checked(stride * height)];
            Marshal.Copy(bits, pixels, 0, pixels.Length);
            var source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr32, null, pixels, stride);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            return new ScreenshotResult(stream.ToArray(), new ScreenshotMetadata(
                handle.HasValue ? WindowHandle.Format(handle.Value) : "desktop", width, height, "png"));
        }
        finally
        {
            if (previous != 0 && previous != -1) Native.SelectObject(memoryDc, previous);
            if (bitmap != 0) Native.DeleteObject(bitmap);
            if (memoryDc != 0) Native.DeleteDC(memoryDc);
            Native.ReleaseDC(0, desktopDc);
        }
    }
}
