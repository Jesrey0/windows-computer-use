using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using WindowsComputerUse.Core;

if (args.Length == 0)
{
    Usage();
    return 2;
}

try
{
    using var client = new HttpClient { BaseAddress = new Uri(ServiceDefaults.BaseUrl), Timeout = TimeSpan.FromSeconds(30) };
    if (args[0] != "health")
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenStore.Load());
    var command = args[0];
    string url;
    object? body = null;
    switch (command)
    {
        case "health" when args.Length == 1:
            url = "/health"; break;
        case "windows" when args.Length == 1:
            url = "/v1/windows"; break;
        case "tree" when args.Length == 2 && WindowHandle.TryParse(args[1], out _):
            url = $"/v1/windows/{args[1]}/tree"; break;
        case "screenshot" when args.Length == 3 && WindowHandle.TryParse(args[1], out _):
            url = $"/v1/windows/{args[1]}/screenshot"; break;
        case "activate" when args.Length == 2 && WindowHandle.TryParse(args[1], out _):
            url = $"/v1/windows/{args[1]}/activate"; body = new { }; break;
        case "inspect" when args.Length == 3 && WindowHandle.TryParse(args[1], out _):
            url = "/v1/elements/inspect";
            body = new LocatedElementRequest(args[1], new ElementLocator(args[2], null, null, null));
            if (!InputLimits.ValidLocator(((LocatedElementRequest)body).Locator)) { Usage(); return 2; }
            break;
        default:
            Usage(); return 2;
    }
    using var response = body is null ? await client.GetAsync(url) : await client.PostAsJsonAsync(url, body);
    if (!response.IsSuccessStatusCode)
    {
        Console.Error.WriteLine($"HTTP {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return 1;
    }
    if (command == "screenshot")
    {
        await using var output = new FileStream(args[2], FileMode.CreateNew, FileAccess.Write);
        await response.Content.CopyToAsync(output);
        Console.WriteLine($"Saved {args[2]}");
    }
    else
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Console.WriteLine(JsonSerializer.Serialize(json.RootElement, new JsonSerializerOptions { WriteIndented = true }));
    }
    return 0;
}
catch (Exception error) when (error is HttpRequestException or TaskCanceledException or IOException or
                              System.Security.Cryptography.CryptographicException or UnauthorizedAccessException)
{
    // Never print token material or paths to protected token files.
    Console.Error.WriteLine("Cannot reach the local host, access its token, or write output; check the service and per-user installation.");
    return 1;
}

static void Usage() => Console.Error.WriteLine("Usage: wcu health | windows | tree <hex-handle> | screenshot <hex-handle> <new-png-path> | activate <hex-handle> | inspect <hex-handle> <automation-id>");
