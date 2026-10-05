using System.Net;
using System.Runtime.InteropServices;
using WindowsComputerUse.Core;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(server =>
{
    server.Listen(IPAddress.Loopback, ServiceDefaults.Port);
    server.Limits.MaxRequestBodySize = 16 * 1024;
});
builder.Services.AddProblemDetails();
builder.Services.AddSingleton<WindowsAutomation>();
var app = builder.Build();
var token = TokenStore.LoadOrCreate();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.Use(async (context, next) =>
{
    if (context.Request.Path == "/health") { await next(); return; }
    if (!TokenStore.Matches(context.Request.Headers.Authorization.ToString(), token))
    {
        context.Response.StatusCode = 401;
        await Results.Problem(statusCode: 401, title: "Unauthorized", extensions: new Dictionary<string, object?>
            { ["code"] = "unauthorized" }).ExecuteAsync(context);
        return;
    }
    await next();
});
app.Use(async (context, next) =>
{
    try { await next(); }
    catch (AutomationException error)
    {
        if (context.Response.HasStarted) throw;
        await Results.Problem(statusCode: error.Status, title: error.Message,
            extensions: new Dictionary<string, object?> { ["code"] = error.Code }).ExecuteAsync(context);
    }
    catch (COMException)
    {
        if (context.Response.HasStarted) throw;
        await Results.Problem(statusCode: 422, title: "UI Automation provider failed or the element became unavailable.",
            extensions: new Dictionary<string, object?> { ["code"] = "automation_failed" }).ExecuteAsync(context);
    }
});

app.MapGet("/health", () => new HealthResponse("ok", typeof(WindowsAutomation).Assembly.GetName().Version?.ToString() ?? "unknown"));
app.MapGet("/v1/capabilities", () => Capabilities.Current);
app.MapGet("/v1/windows", (WindowsAutomation automation) => automation.Windows());
app.MapGet("/v1/windows/{handle}/state", (string handle, WindowsAutomation automation) =>
    automation.State(ParseHandle(handle)));
app.MapPost("/v1/windows/{handle}/close", (string handle, WindowsAutomation automation) =>
    automation.Close(ParseHandle(handle)));
app.MapPost("/v1/windows/{handle}/activate", (string handle, WindowsAutomation automation) =>
    automation.Activate(ParseHandle(handle)));
app.MapGet("/v1/windows/{handle}/tree", (string handle, int? depth, int? maxNodes, WindowsAutomation automation) =>
{
    if (!InputLimits.ValidTree(depth ?? 3, maxNodes ?? 200) || !WindowHandle.TryParse(handle, out var hwnd))
        return Results.Problem(statusCode: 400, title: "Invalid handle or tree limits.");
    return Results.Ok(automation.Tree(hwnd, depth ?? 3, maxNodes ?? 200));
});
app.MapGet("/v1/windows/{handle}/screenshot", (string handle, WindowsAutomation automation, HttpContext context) =>
    WindowHandle.TryParse(handle, out var hwnd)
        ? Png(context, automation.Screenshot(hwnd))
        : Results.Problem(statusCode: 400, title: "Invalid handle."));
app.MapGet("/v1/desktop/screenshot", (WindowsAutomation automation, HttpContext context) => Png(context, automation.Screenshot(null)));
app.MapPost("/v1/actions/invoke", (InvokeRequest? request, WindowsAutomation automation) =>
{
    if (request is null || !InputLimits.ValidPath(request.Path) || !WindowHandle.TryParse(request.Handle, out var hwnd))
        return Results.Problem(statusCode: 400, title: "Invalid handle or element path.");
    automation.Invoke(hwnd, request.Path);
    return Results.Ok(new ActionResponse(true));
});
app.MapPost("/v1/actions/set-value", (SetValueRequest? request, WindowsAutomation automation) =>
{
    if (request is null || !InputLimits.ValidPath(request.Path) ||
        request.Value is null || request.Value.Length > InputLimits.MaxValueLength ||
        !WindowHandle.TryParse(request.Handle, out var hwnd))
        return Results.Problem(statusCode: 400, title: "Invalid handle, path or value.");
    automation.SetValue(hwnd, request.Path, request.Value);
    return Results.Ok(new ActionResponse(true));
});
app.MapPost("/v1/elements/inspect", (LocatedElementRequest? request, WindowsAutomation automation) =>
    automation.Inspect(LocatedHandle(request), request!.Locator));
app.MapPost("/v1/elements/find", (FindElementsRequest? request, WindowsAutomation automation) =>
{
    if (!InputLimits.ValidFind(request))
        throw new AutomationException("invalid_find", "Invalid locator or find limits.", 400);
    return automation.FindElements(ParseHandle(request!.Handle), request);
});
app.MapPost("/v1/elements/focus", (LocatedElementRequest? request, WindowsAutomation automation) =>
    automation.Focus(LocatedHandle(request), request!.Locator));
app.MapPost("/v1/actions/invoke-located", (LocatedElementRequest? request, WindowsAutomation automation) =>
{
    automation.InvokeLocated(LocatedHandle(request), request!.Locator);
    return new ActionResponse(true);
});
app.MapPost("/v1/actions/set-value-located", (LocatedSetValueRequest? request, WindowsAutomation automation) =>
{
    if (request?.Value is null || request.Value.Length > InputLimits.MaxValueLength)
        throw new AutomationException("invalid_value", "Value must be at most 4096 UTF-16 units.", 400);
    var handle = ParseHandle(request.Handle);
    if (!InputLimits.ValidLocator(request.Locator))
        throw new AutomationException("invalid_locator", "Use one or more nonempty exact fields and at most one ancestor.", 400);
    automation.SetValueLocated(handle, request.Locator, request.Value);
    return new ActionResponse(true);
});
app.MapPost("/v1/input/click", (ClickRequest request, WindowsAutomation automation) =>
{
    automation.Click(request.X, request.Y);
    return Results.Ok(new ActionResponse(true));
});
app.MapPost("/v1/input/keys", (KeysRequest? request, WindowsAutomation automation) =>
{
    if (!InputLimits.ValidKeys(request))
        return Results.Problem(statusCode: 400, title: "Provide text or virtualKey, but not both.");
    automation.Keys(request!);
    return Results.Ok(new ActionResponse(true));
});
app.MapPost("/v1/input/key-sequence", (KeySequenceRequest? request, WindowsAutomation automation) =>
{
    if (!InputLimits.ValidSequence(request))
        throw new AutomationException("invalid_input", "Invalid key sequence, VK or modifiers.", 400);
    automation.Sequence(request!);
    return new ActionResponse(true);
});
app.MapPost("/v1/input/move", (MoveRequest? request, WindowsAutomation automation) =>
{
    if (request is null) throw new AutomationException("invalid_input", "Coordinates required.", 400);
    automation.Move(request.X, request.Y);
    return new ActionResponse(true);
});
app.MapPost("/v1/input/pointer-click", (PointerClickRequest? request, WindowsAutomation automation) =>
{
    if (!InputLimits.ValidPointerClick(request))
        throw new AutomationException("invalid_input", "Use left or right button and count 1 or 2.", 400);
    automation.PointerClick(request!);
    return new ActionResponse(true);
});
app.MapPost("/v1/input/scroll", (ScrollRequest? request, WindowsAutomation automation) =>
{
    if (!InputLimits.ValidScroll(request))
        throw new AutomationException("invalid_input", "Invalid wheel delta or coordinate pair.", 400);
    automation.Scroll(request!);
    return new ActionResponse(true);
});

app.Run();

static nint ParseHandle(string? text) => WindowHandle.TryParse(text, out var handle)
    ? handle : throw new AutomationException("invalid_handle", "Invalid window handle.", 400);

static nint LocatedHandle(LocatedElementRequest? request)
{
    var handle = ParseHandle(request?.Handle);
    if (!InputLimits.ValidLocator(request?.Locator))
        throw new AutomationException("invalid_locator", "Use one or more nonempty exact fields and at most one ancestor.", 400);
    return handle;
}

static IResult Png(HttpContext context, ScreenshotResult result)
{
    context.Response.Headers["X-WCU-Width"] = result.Metadata.Width.ToString(System.Globalization.CultureInfo.InvariantCulture);
    context.Response.Headers["X-WCU-Height"] = result.Metadata.Height.ToString(System.Globalization.CultureInfo.InvariantCulture);
    context.Response.Headers["X-WCU-Target"] = result.Metadata.Target;
    return Results.File(result.Png, "image/png", enableRangeProcessing: false);
}
