namespace WindowsComputerUse.Core;

public static class ServiceDefaults
{
    public const int Port = 17842;
    public const string BaseUrl = "http://127.0.0.1:17842";
    public static string DataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WindowsComputerUse", "data");
}

public sealed record HealthResponse(string Status, string Version);
public sealed record RectDto(double X, double Y, double Width, double Height);
public sealed record WindowDto(string Handle, int ProcessId, string Title, RectDto Bounds,
    string? ExecutablePath, string? ProcessName);
public sealed record ElementDto(int[] Path, string? Name, string ControlType, string? AutomationId,
    RectDto? Bounds, bool CanInvoke, bool CanSetValue, List<ElementDto> Children);
public sealed record TreeResponse(string Handle, ElementDto Root, int NodeCount, bool Truncated);
public sealed record ScreenshotMetadata(string Target, int Width, int Height, string Format);
public sealed record ScreenshotResult(byte[] Png, ScreenshotMetadata Metadata);
public sealed record InvokeRequest(string Handle, int[] Path);
public sealed record SetValueRequest(string Handle, int[] Path, string Value);
public sealed record ClickRequest(int X, int Y);
public sealed record KeysRequest(string? Text, ushort? VirtualKey);
public sealed record ActionResponse(bool Success);
public sealed record ElementLocator(string? AutomationId = null, string? Name = null, string? ControlType = null, ElementLocator? Ancestor = null);
public sealed record LocatedElementRequest(string Handle, ElementLocator Locator);
public sealed record LocatedSetValueRequest(string Handle, ElementLocator Locator, string Value);
public sealed record ActivationResponse(string Handle, bool IsForeground);
public sealed record ElementState(string Handle, string? Name, string? Value, string ControlType,
    string? AutomationId, bool Enabled, bool Focused, bool Offscreen, RectDto Bounds,
    bool CanInvoke, bool CanSetValue);

public static class InputLimits
{
    public const int MaxDepth = 10;
    public const int MaxNodes = 1000;
    public const int MaxPath = 10;
    public const int MaxValueLength = 4096;
    public const int MaxTextLength = 256;
    public const int MaxLocatorDepth = 20;
    public const int MaxLocatorNodes = 2000;
    public const int MaxLocatorTextLength = 256;

    public static bool ValidTree(int depth, int maxNodes) =>
        depth is >= 0 and <= MaxDepth && maxNodes is >= 1 and <= MaxNodes;

    public static bool ValidPath(int[]? path) =>
        path is not null && path.Length <= MaxPath && path.All(index => index is >= 0 and < MaxNodes);

    public static bool ValidKeys(KeysRequest? request) => request is not null &&
        (request.Text is { Length: > 0 and <= MaxTextLength } && request.VirtualKey is null ||
         request.Text is null && request.VirtualKey is > 0);

    public static bool ValidLocator(ElementLocator? locator) => locator is not null &&
        ValidCriteria(locator) && (locator.Ancestor is null ||
                                    locator.Ancestor.Ancestor is null && ValidCriteria(locator.Ancestor));

    private static bool ValidCriteria(ElementLocator locator)
    {
        static bool ValidField(string? value) => value is null ||
            value.Length <= MaxLocatorTextLength && !string.IsNullOrWhiteSpace(value);
        return (locator.AutomationId is not null || locator.Name is not null || locator.ControlType is not null) &&
            ValidField(locator.AutomationId) && ValidField(locator.Name) && ValidField(locator.ControlType);
    }
}

public static class WindowHandle
{
    public static bool TryParse(string? text, out nint handle)
    {
        handle = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var value = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? text[2..] : text;
        if (!ulong.TryParse(value, System.Globalization.NumberStyles.AllowHexSpecifier,
                System.Globalization.CultureInfo.InvariantCulture, out var number) || number == 0 ||
            number > long.MaxValue) return false;
        handle = (nint)(long)number;
        return true;
    }

    public static string Format(nint handle) => $"0x{(ulong)(long)handle:X}";
}

public sealed class AutomationException(string code, string message, int status = 422) : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
}
