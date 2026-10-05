using System.ComponentModel;
using System.Runtime.InteropServices;

namespace WindowsComputerUse.Core;

internal static class NativeInput
{
    public static void Click(int x, int y)
    {
        Move(x, y);
        ClickButton("left", 1);
    }

    public static void Move(int x, int y) => Move(x, y, false);

    private static void Move(int x, int y, bool noCoalesce)
    {
        var left = Native.GetSystemMetrics(76);
        var top = Native.GetSystemMetrics(77);
        var width = Native.GetSystemMetrics(78);
        var height = Native.GetSystemMetrics(79);
        var (dx, dy) = AbsoluteCoordinates(x, y, left, top, width, height);
        // Drag vertices must survive WM_MOUSEMOVE coalescing, including immediate strokes.
        // Existing move/click/scroll retain their original flags.
        Send([Mouse(0x8000 | 0x4000 | 0x0001 | (noCoalesce ? 0x2000u : 0u), 0, dx, dy)]);
    }

    internal static (int X, int Y) AbsoluteCoordinates(int x, int y, int left, int top, int width, int height)
    {
        if (width < 2 || height < 2 || x < left || y < top ||
            (long)x >= (long)left + width || (long)y >= (long)top + height)
            throw new AutomationException("invalid_coordinates", "Click must be within the virtual desktop.", 400);
        var dx = (int)(((long)x - left) * 65535 / (width - 1));
        var dy = (int)(((long)y - top) * 65535 / (height - 1));
        return (dx, dy);
    }

    public static DragResponse Drag(DragRequest? request)
    {
        var points = DragGesture.Interpolate(request);
        var drag = request!;
        var left = Native.GetSystemMetrics(76);
        var top = Native.GetSystemMetrics(77);
        var width = Native.GetSystemMetrics(78);
        var height = Native.GetSystemMetrics(79);
        // Reject an off-desktop path before pressing. Move still rechecks the desktop
        // in case display configuration changes during the gesture.
        foreach (var point in drag.Points!)
            AbsoluteCoordinates(point.X, point.Y, left, top, width, height);
        var metadata = DragGesture.Execute(points, drag.DurationMs,
            point => Move(point.X, point.Y, true),
            () => Send([Mouse(0x0002)]), () => Send([Mouse(0x0004)]));
        return new(true, metadata);
    }

    public static void PointerClick(PointerClickRequest request)
    {
        if (!InputLimits.ValidPointerClick(request))
            throw new AutomationException("invalid_input", "Use left or right button and count 1 or 2.", 400);
        Move(request.X, request.Y);
        ClickButton(request.Button, request.Count);
    }

    private static void ClickButton(string button, int count)
    {
        var down = button == "left" ? 0x0002u : 0x0008u;
        var up = button == "left" ? 0x0004u : 0x0010u;
        for (var i = 0; i < count; i++)
            SendPair(Mouse(down), Mouse(up));
    }

    public static void Scroll(ScrollRequest request)
    {
        if (!InputLimits.ValidScroll(request))
            throw new AutomationException("invalid_input", "Wheel delta must be nonzero within +/-1200; x and y must occur together.", 400);
        if (request.X is { } x) Move(x, request.Y!.Value);
        Send([Mouse(0x0800, unchecked((uint)request.Delta))]);
    }

    private static Native.Input Mouse(uint flags, uint data = 0, int dx = 0, int dy = 0) => new()
    {
        Type = 0, Data = new Native.InputUnion { Mouse = new Native.MouseInput { Flags = flags, MouseData = data, Dx = dx, Dy = dy } }
    };

    internal static IReadOnlyList<(ushort Key, bool Down)> KeyOrder(KeyChord chord)
    {
        // Caller validates first; canonical order regardless of supplied modifier order.
        var modifiers = new ushort[] { 0x11, 0x10, 0x12 }.Where(key => chord.Modifiers!.Contains(key)).ToArray();
        return [.. modifiers.Select(key => (key, true)), (chord.Key, true), (chord.Key, false),
            .. modifiers.Reverse().Select(key => (key, false))];
    }

    public static void Sequence(KeySequenceRequest request)
    {
        if (!InputLimits.ValidSequence(request))
            throw new AutomationException("invalid_input", "Invalid key sequence, VK or modifiers.", 400);
        foreach (var chord in request.Chords!)
        {
            var pressed = new List<ushort>();
            Exception? failure = null;
            try
            {
                foreach (var (key, down) in KeyOrder(chord))
                {
                    Send([Key(key, 0, down ? 0u : 0x0002u)]);
                    if (down) pressed.Add(key);
                    else pressed.Remove(key);
                }
            }
            catch (Exception error) { failure = error; throw; }
            finally
            {
                Exception? releaseFailure = null;
                for (var i = pressed.Count - 1; i >= 0; i--)
                {
                    try { Send([Key(pressed[i], 0, 0x0002)]); }
                    catch (Exception error) { releaseFailure ??= error; }
                }
                if (failure is null && releaseFailure is not null) throw releaseFailure;
            }
        }
    }

    public static void Keys(KeysRequest request)
    {
        if (!InputLimits.ValidKeys(request))
            throw new AutomationException("invalid_input", "Provide either text (1-256 UTF-16 units) or a nonzero virtualKey.", 400);
        if (request.Text is { } text)
        {
            foreach (var ch in text)
                SendPair(Key(0, ch, 0x0004), Key(0, ch, 0x0004 | 0x0002));
        }
        else
            SendPair(Key(request.VirtualKey!.Value, 0, 0), Key(request.VirtualKey.Value, 0, 0x0002));
    }

    private static void SendPair(Native.Input down, Native.Input up)
    {
        Send([down]);
        try { Send([up]); }
        catch
        {
            try { Send([up]); } catch (AutomationException) { /* best-effort release */ }
            throw;
        }
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
