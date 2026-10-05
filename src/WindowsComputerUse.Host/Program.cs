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
app.MapGet("/v1/windows", (WindowsAutomation automation) => automation.Windows());
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

app.Run();

static IResult Png(HttpContext context, ScreenshotResult result)
{
    context.Response.Headers["X-WCU-Width"] = result.Metadata.Width.ToString(System.Globalization.CultureInfo.InvariantCulture);
    context.Response.Headers["X-WCU-Height"] = result.Metadata.Height.ToString(System.Globalization.CultureInfo.InvariantCulture);
    context.Response.Headers["X-WCU-Target"] = result.Metadata.Target;
    return Results.File(result.Png, "image/png", enableRangeProcessing: false);
}
