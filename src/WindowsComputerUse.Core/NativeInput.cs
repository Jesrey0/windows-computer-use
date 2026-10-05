using System.ComponentModel;
using System.Runtime.InteropServices;

namespace WindowsComputerUse.Core;

internal static class NativeInput
{
    public static void Click(int x, int y)
    {
        var left = Native.GetSystemMetrics(76);
        var top = Native.GetSystemMetrics(77);
        var width = Native.GetSystemMetrics(78);
        var height = Native.GetSystemMetrics(79);
        if (width < 2 || height < 2 || x < left || y < top ||
            (long)x >= (long)left + width || (long)y >= (long)top + height)
            throw new AutomationException("invalid_coordinates", "Click must be within the virtual desktop.", 400);
        var dx = (int)((long)(x - left) * 65535 / (width - 1));
        var dy = (int)((long)(y - top) * 65535 / (height - 1));
        Send([
            new Native.Input { Type = 0, Data = new Native.InputUnion { Mouse = new Native.MouseInput { Dx = dx, Dy = dy, Flags = 0x8000 | 0x4000 | 0x0001 } } },
            new Native.Input { Type = 0, Data = new Native.InputUnion { Mouse = new Native.MouseInput { Flags = 0x0002 } } },
            new Native.Input { Type = 0, Data = new Native.InputUnion { Mouse = new Native.MouseInput { Flags = 0x0004 } } }
        ]);
    }

    public static void Keys(KeysRequest request)
    {
        if (!InputLimits.ValidKeys(request))
            throw new AutomationException("invalid_input", "Provide either text (1-256 UTF-16 units) or a nonzero virtualKey.", 400);
        var inputs = new List<Native.Input>();
        if (request.Text is { } text)
        {
            foreach (var ch in text)
            {
                inputs.Add(Key(0, ch, 0x0004)); // KEYEVENTF_UNICODE
                inputs.Add(Key(0, ch, 0x0004 | 0x0002));
            }
        }
        else
        {
            inputs.Add(Key(request.VirtualKey!.Value, 0, 0));
            inputs.Add(Key(request.VirtualKey.Value, 0, 0x0002));
        }
        Send([.. inputs]);
    }

    private static Native.Input Key(ushort key, ushort scan, uint flags) => new()
    {
        Type = 1, Data = new Native.InputUnion { Keyboard = new Native.KeyboardInput { VirtualKey = key, Scan = scan, Flags = flags } }
    };

    private static void Send(Native.Input[] inputs)
    {
        var written = Native.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Native.Input>());
        if (written != inputs.Length)
            throw new AutomationException("input_blocked", "SendInput failed; check the interactive session, desktop and UIPI integrity level. " +
                $"Windows error: {new Win32Exception(Marshal.GetLastWin32Error()).NativeErrorCode}.");
    }
}
