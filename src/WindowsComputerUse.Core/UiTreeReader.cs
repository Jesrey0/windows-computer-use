using System.Runtime.InteropServices;
using FlaUI.Core.Exceptions;

namespace WindowsComputerUse.Core;

internal static class UiTreeReader
{
    public static bool IsProviderFailure(Exception error) => error is COMException or FlaUIException or
        PropertyNotSupportedException or PatternNotSupportedException or InvalidOperationException;

    public static T Read<T>(Func<T> read, T fallback, Action onUnavailable)
    {
        try { return read(); }
        catch (Exception error) when (IsProviderFailure(error))
        {
            onUnavailable();
            return fallback;
        }
    }
}
