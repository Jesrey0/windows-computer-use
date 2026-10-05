using System.Runtime.InteropServices;
using FlaUI.Core.Exceptions;

namespace WindowsComputerUse.Core;

internal static class UiTreeReader
{
    public static T Read<T>(Func<T> read, T fallback, Action onUnavailable)
    {
        try { return read(); }
        catch (Exception error) when (error is COMException or FlaUIException or
                                      PropertyNotSupportedException or PatternNotSupportedException or
                                      InvalidOperationException)
        {
            onUnavailable();
            return fallback;
        }
    }
}
