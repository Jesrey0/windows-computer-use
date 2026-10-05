using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;

namespace WindowsComputerUse.Core;

public sealed partial class WindowsAutomation
{
    public IReadOnlyList<WindowDto> Windows()
    {
        var result = new List<WindowDto>();
        Native.EnumWindows((handle, _) =>
        {
            if (!Native.IsWindowVisible(handle) || !Native.GetWindowRect(handle, out var rect) ||
                rect.Right <= rect.Left || rect.Bottom <= rect.Top)
                return true;
            var length = Native.GetWindowTextLength(handle);
            var title = new StringBuilder(Math.Min(length + 1, 32768));
            Native.GetWindowText(handle, title, title.Capacity);
            Native.GetWindowThreadProcessId(handle, out var pid);
            string? executable = null;
            string? processName = null;
            var process = Native.OpenProcess(0x1000, false, pid);
            if (process != 0)
            {
                try
                {
                    var path = new StringBuilder(32768);
                    var size = path.Capacity;
                    if (Native.QueryFullProcessImageName(process, 0, path, ref size))
                    {
                        executable = path.ToString();
                        processName = Path.GetFileName(executable);
                    }
                }
                finally { Native.CloseHandle(process); }
            }
            result.Add(new WindowDto(WindowHandle.Format(handle), (int)pid, title.ToString(),
                new RectDto(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top),
                executable, processName));
            return true;
        }, 0);
        return result;
    }

    public TreeResponse Tree(nint handle, int depth, int maxNodes)
    {
        RequireWindow(handle);
        using var automation = new UIA3Automation();
        var root = automation.FromHandle(handle);
        var count = 0;
        var truncated = false;
        T Read<T>(Func<T> read, T fallback) => UiTreeReader.Read(read, fallback, () => truncated = true);
        ElementDto Visit(AutomationElement element, int[] path, int level)
        {
            count++;
            var bounds = Read(() =>
            {
                var rect = element.BoundingRectangle;
                return (RectDto?)new RectDto(rect.Left, rect.Top, rect.Width, rect.Height);
            }, (RectDto?)null);
            var name = Read(() => element.Name, (string?)null);
            var controlType = Read(() => element.ControlType.ToString(), "Unknown");
            var automationId = Read(() => element.AutomationId, (string?)null);
            var canInvoke = Read(() => element.Patterns.Invoke.IsSupported, false);
            var canSetValue = Read(() => element.Patterns.Value.IsSupported, false);
            var children = new List<ElementDto>();
            if (level < depth)
            {
                // Keep original child indexes: an omitted/unavailable sibling must not
                // cause a displayed path to address a different element on Resolve.
                var descendants = Read(() => element.FindAllChildren(), Array.Empty<AutomationElement>());
                for (var index = 0; index < descendants.Length; index++)
                {
                    if (count >= maxNodes) { truncated = true; break; }
                    children.Add(Visit(descendants[index], [.. path, index], level + 1));
                }
            }
            return new ElementDto(path, name, controlType, automationId, bounds,
                canInvoke, canSetValue, children);
        }
        return new TreeResponse(WindowHandle.Format(handle), Visit(root, [], 0), count, truncated);
    }

    public void Invoke(nint handle, int[] path)
    {
        using var automation = new UIA3Automation();
        InvokeElement(Resolve(automation, handle, path));
    }

    public void SetValue(nint handle, int[] path, string value)
    {
        using var automation = new UIA3Automation();
        SetValueElement(Resolve(automation, handle, path), value);
    }

    private static AutomationElement Resolve(UIA3Automation automation, nint handle, int[] path)
    {
        RequireWindow(handle);
        var element = automation.FromHandle(handle);
        foreach (var index in path)
        {
            var children = element.FindAllChildren();
            if (index >= children.Length)
                throw new AutomationException("stale_element", "UI tree changed; request a fresh tree.", 409);
            element = children[index];
        }
        return element;
    }

    internal static void RequireWindow(nint handle)
    {
        if (!Native.IsWindow(handle) || Native.GetAncestor(handle, 2) != handle)
            throw new AutomationException("window_not_found", "Top-level window not found.", 404);
    }

    public ScreenshotResult Screenshot(nint? handle) => ScreenCapture.Capture(handle);
    public void Click(int x, int y) => NativeInput.Click(x, y);
    public void Keys(KeysRequest request) => NativeInput.Keys(request);
}

internal static class Native
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect { public int Left, Top, Right, Bottom; }

    internal delegate bool EnumWindowsCallback(nint handle, nint context);

    [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumWindowsCallback callback, nint context);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool IsWindowVisible(nint handle);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool IsWindow(nint handle);
    [DllImport("user32.dll")] internal static extern nint GetAncestor(nint handle, uint flags);
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetForegroundWindow(nint handle);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool BringWindowToTop(nint handle);
    [DllImport("user32.dll")] internal static extern nint SetActiveWindow(nint handle);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool AttachThreadInput(uint attach, uint attachTo, [MarshalAs(UnmanagedType.Bool)] bool attachState);
    [DllImport("kernel32.dll")] internal static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool IsIconic(nint handle);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool ShowWindow(nint handle, int command);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetWindowRect(nint handle, out Rect rect);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetWindowText(nint handle, StringBuilder text, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetWindowTextLength(nint handle);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint handle, out uint pid);
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern nint OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool QueryFullProcessImageName(nint process, uint flags, StringBuilder path, ref int size);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool CloseHandle(nint handle);

    [DllImport("user32.dll")] internal static extern nint GetDC(nint window);
    [DllImport("user32.dll")] internal static extern int ReleaseDC(nint window, nint dc);
    [DllImport("gdi32.dll")] internal static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool DeleteDC(nint dc);
    [DllImport("gdi32.dll")] internal static extern nint SelectObject(nint dc, nint obj);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool DeleteObject(nint obj);
    [DllImport("gdi32.dll", SetLastError = true)] internal static extern nint CreateDIBSection(nint dc, ref BitmapInfo info, uint usage, out nint bits, nint section, uint offset);
    [DllImport("gdi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool BitBlt(nint dest, int dx, int dy, int width, int height, nint source, int sx, int sy, uint operation);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool PrintWindow(nint window, nint dc, uint flags);
    [DllImport("user32.dll")] internal static extern int GetSystemMetrics(int index);

    [StructLayout(LayoutKind.Sequential)]
    internal struct BitmapInfo
    {
        public uint Size; public int Width, Height; public ushort Planes, BitCount;
        public uint Compression, ImageSize; public int XPelsPerMeter, YPelsPerMeter;
        public uint ClrUsed, ClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Input
    {
        public uint Type;
        public InputUnion Data;
    }
    [StructLayout(LayoutKind.Explicit)]
    internal struct InputUnion
    {
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public KeyboardInput Keyboard;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct MouseInput
    {
        public int Dx, Dy; public uint MouseData, Flags, Time; public nuint ExtraInfo;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct KeyboardInput
    {
        public ushort VirtualKey, Scan; public uint Flags, Time; public nuint ExtraInfo;
    }
    [DllImport("user32.dll", SetLastError = true)] internal static extern uint SendInput(uint count, [In] Input[] inputs, int size);
}
