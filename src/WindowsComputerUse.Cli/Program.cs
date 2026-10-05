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
    var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    switch (command)
    {
        case "health" when args.Length == 1:
            url = "/health"; break;
        case "windows" when args.Length == 1:
            url = "/v1/windows"; break;
        case "capabilities" when args.Length == 1:
            url = "/v1/capabilities"; break;
        case "state" when args.Length == 2 && WindowHandle.TryParse(args[1], out _):
            url = $"/v1/windows/{args[1]}/state"; break;
        case "close" when args.Length == 2 && WindowHandle.TryParse(args[1], out _):
            url = $"/v1/windows/{args[1]}/close"; body = new { }; break;
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
        case "find" when args.Length is >= 3 and <= 6 && WindowHandle.TryParse(args[1], out _):
            url = "/v1/elements/find";
            var locator = JsonSerializer.Deserialize<ElementLocator>(args[2], jsonOptions);
            if (!InputLimits.ValidLocator(locator)) { Usage(); return 2; }
            int?[] limits = new int?[3];
            for (var i = 3; i < args.Length; i++)
            {
                if (!int.TryParse(args[i], out var value)) { Usage(); return 2; }
                limits[i - 3] = value;
            }
            var find = new FindElementsRequest(args[1], locator!, limits[0], limits[1], limits[2]);
            if (!InputLimits.ValidFind(find)) { Usage(); return 2; }
            body = find; break;
        case "key-sequence" when args.Length == 2:
            url = "/v1/input/key-sequence";
            body = JsonSerializer.Deserialize<KeySequenceRequest>(args[1], jsonOptions);
            if (!InputLimits.ValidSequence((KeySequenceRequest?)body)) { Usage(); return 2; }
            break;
        case "move" when args.Length == 3 && int.TryParse(args[1], out var mx) && int.TryParse(args[2], out var my):
            url = "/v1/input/move"; body = new MoveRequest(mx, my); break;
        case "pointer-click" when args.Length == 5 && int.TryParse(args[1], out var cx) && int.TryParse(args[2], out var cy) && int.TryParse(args[4], out var count):
            url = "/v1/input/pointer-click";
            var click = new PointerClickRequest(cx, cy, args[3], count);
            if (!InputLimits.ValidPointerClick(click)) { Usage(); return 2; }
            body = click; break;
        case "scroll" when args.Length is 2 or 4 && int.TryParse(args[1], out var delta):
            url = "/v1/input/scroll";
            if (args.Length == 4 && (!int.TryParse(args[2], out _) || !int.TryParse(args[3], out _))) { Usage(); return 2; }
            var scroll = args.Length == 4 ? new ScrollRequest(delta, int.Parse(args[2]), int.Parse(args[3])) : new ScrollRequest(delta);
            if (!InputLimits.ValidScroll(scroll)) { Usage(); return 2; }
            body = scroll; break;
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
catch (JsonException)
{
    Usage(); return 2;
}

static void Usage() => Console.Error.WriteLine("Usage: wcu health | capabilities | windows | state <handle> | close <handle> | tree <handle> | screenshot <handle> <new-png-path> | activate <handle> | inspect <handle> <automation-id> | find <handle> '<locator-json>' [maxResults [maxNodes [maxDepth]]] | key-sequence '<json>' | move <x> <y> | pointer-click <x> <y> <left|right> <1|2> | scroll <delta> [x y]");
