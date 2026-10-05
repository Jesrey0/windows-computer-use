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
public sealed record KeyChord(ushort Key, ushort[]? Modifiers = null);
public sealed record KeySequenceRequest(KeyChord[]? Chords);
public sealed record MoveRequest(int X, int Y);
public sealed record PointerClickRequest(int X, int Y, string Button, int Count);
public sealed record ScrollRequest(int Delta, int? X = null, int? Y = null);
public sealed record DragPoint([property: System.Text.Json.Serialization.JsonRequired] int X,
    [property: System.Text.Json.Serialization.JsonRequired] int Y);
public sealed record DragRequest(DragPoint[]? Points, int DurationMs = 0, int? StepsPerSegment = null);
public sealed record DragMetadata(double ElapsedMs, int EmittedInputCount, int Steps);
public sealed record DragResponse(bool Success, DragMetadata? Metadata = null);
public sealed record WindowState(string Handle, int ProcessId, string Title, RectDto Bounds,
    string? ExecutablePath, string? ProcessName, bool Visible, bool Minimized, bool Maximized,
    bool Foreground, bool Enabled);
public sealed record CloseWindowResponse(string Handle, bool RequestPosted, bool Disappeared);
public sealed record FindElementsRequest(string Handle, ElementLocator Locator, int? MaxResults = null,
    int? MaxNodes = null, int? MaxDepth = null);
public sealed record FoundElementState(string Handle, string? Name, string ControlType, string? AutomationId,
    bool Enabled, bool Focused, bool Offscreen, RectDto Bounds, bool CanInvoke, bool CanSetValue);
public sealed record FoundElement(FoundElementState State, ElementLocator? Locator, bool LocatorUnique);
public sealed record FindElementsResponse(string Handle, List<FoundElement> Elements, int VisitedNodes, bool Complete);
public sealed record CapabilitiesResponse(string ApiVersion, string HostVersion, string[] LocatorFields,
    string[] SemanticActions, string[] InputActions, string[] WindowActions, CapabilityLimits Limits);
public sealed record CapabilityLimits(int MaxLocatorDepth, int MaxLocatorNodes, int MaxLocatorTextLength,
    int MaxFindResults, int MaxKeySequence, int MaxVirtualKey, int MaxWheelDelta,
    int MaxTextLength, int MaxValueLength, int MaxTreeDepth, int MaxTreeNodes, int MaxClickCount,
    int MaxDragPoints, int MaxDragDurationMs, int MaxDragSteps);
public static class Capabilities
{
    public static CapabilitiesResponse Current => new("v1", typeof(WindowsAutomation).Assembly.GetName().Version?.ToString() ?? "unknown",
        ["automationId", "name", "controlType"],
        ["tree", "inspect", "find-elements", "focus", "invoke", "set-value"],
        ["click", "keys", "key-sequence", "move", "pointer-click", "scroll", "drag"],
        ["list", "state", "activate", "close", "screenshot"],
        new(InputLimits.MaxLocatorDepth, InputLimits.MaxLocatorNodes, InputLimits.MaxLocatorTextLength,
            InputLimits.MaxFindResults, InputLimits.MaxKeySequence, InputLimits.MaxVirtualKey,
            InputLimits.MaxWheelDelta, InputLimits.MaxTextLength, InputLimits.MaxValueLength,
            InputLimits.MaxDepth, InputLimits.MaxNodes, 2,
            InputLimits.MaxDragPoints, InputLimits.MaxDragDurationMs, InputLimits.MaxDragSteps));
}
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
    public const int MaxFindResults = 100;
    public const int MaxKeySequence = 32;
    public const int MaxVirtualKey = 254;
    public const int MaxWheelDelta = 1200;
    public const int MaxDragPoints = 128;
    public const int MaxDragDurationMs = 5000;
    // Includes the initial pointer move; button events do not consume this budget.
    public const int MaxDragSteps = 512;

    public static bool ValidDrag(DragRequest? request) => request?.Points is
        { Length: >= 2 and <= MaxDragPoints } points && points.All(point => point is not null) &&
        request.DurationMs is >= 0 and <= MaxDragDurationMs &&
        (request.StepsPerSegment is null || request.StepsPerSegment is >= 1 and < MaxDragSteps &&
            1L + (points.Length - 1L) * request.StepsPerSegment.Value <= MaxDragSteps);

    internal static int DragStepsPerSegment(DragRequest request) => request.StepsPerSegment ??
        Math.Min(16, (MaxDragSteps - 1) / (request.Points!.Length - 1));

    public static bool ValidSequence(KeySequenceRequest? request) => request?.Chords is
        { Length: >= 1 and <= MaxKeySequence } chords && chords.All(chord => chord is not null &&
            chord.Key is >= 1 and <= MaxVirtualKey && chord.Modifiers is { Length: <= 3 } modifiers &&
            modifiers.All(key => key is 0x10 or 0x11 or 0x12) && modifiers.Distinct().Count() == modifiers.Length &&
            !modifiers.Contains(chord.Key));

    public static bool ValidPointerClick(PointerClickRequest? request) => request is not null &&
        request.Button is "left" or "right" && request.Count is 1 or 2;
    public static bool ValidScroll(ScrollRequest? request) => request is not null &&
        request.Delta is >= -MaxWheelDelta and <= MaxWheelDelta and not 0 &&
        (request.X.HasValue == request.Y.HasValue);
    public static bool ValidFind(FindElementsRequest? request) => request is not null &&
        ValidLocator(request.Locator) && (request.MaxResults is null or >= 1 and <= MaxFindResults) &&
        (request.MaxNodes is null or >= 1 and <= MaxLocatorNodes) &&
        (request.MaxDepth is null or >= 0 and <= MaxLocatorDepth);

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
