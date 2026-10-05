# Architecture and operational contract

## Ownership and layout

- Canonical source: `/home/jesrey0/projects/windows-computer-use` in WSL. Build
  using the Windows .NET 10 SDK against the WSL UNC path; no Linux runtime is needed.
- `Core`: DTOs, validation, token storage, Win32 P/Invoke, FlaUI UIA3, WPF PNG
  encoding. `Host`: ASP.NET Core loopback API. `Cli`: diagnostic HTTP client only.
- Runtime: `%LOCALAPPDATA%\WindowsComputerUse\releases\<version>\{host,cli}`;
  stable `wcu-host.cmd`, `wcu.cmd`, and `current.txt`; `data\token.dpapi` and
  reserved `logs\` live outside releases. Do not put runtime state in WSL source.
- Launch host manually in the logged-in user's interactive Windows session. It
  does not install a service, scheduled task, background daemon or UAC bypass.

## HTTP v1

Kestrel binds **only `127.0.0.1:17842`**, not IPv6 or a network interface.
`GET /health` is public (`{status,version}`). All other endpoints require
`Authorization: Bearer <token>`; only the Windows user running the host/CLI can
decrypt the DPAPI CurrentUser-protected token. The host creates it once, and the
CLI discovers it from `%LOCALAPPDATA%\WindowsComputerUse\data\token.dpapi`.
Do not share bearer tokens, forward the port, or log request headers.

| Endpoint | Request / response |
| --- | --- |
| `GET /v1/windows` | Array of `{handle,processId,title,bounds,executablePath,processName}`. Access-denied process metadata is null. |
| `GET /v1/windows/{handle}/tree?depth=3&maxNodes=200` | `{handle,root,nodeCount,truncated}`. Root path `[]`; children use zero-based path segments. `depth` 0–10, `maxNodes` 1–1000. |
| `GET /v1/windows/{handle}/screenshot` | `image/png` + `X-WCU-Target`, `X-WCU-Width`, `X-WCU-Height` headers. |
| `GET /v1/desktop/screenshot` | Same PNG and metadata headers for entire virtual desktop. |
| `POST /v1/actions/invoke` | JSON `{"handle":"0x1234","path":[0,1]}`. InvokePattern only. |
| `POST /v1/actions/set-value` | JSON `{"handle":"0x1234","path":[0],"value":"text"}`. ValuePattern only, max 4096 UTF-16 units. |
| `POST /v1/input/click` | JSON `{"x":100,"y":200}` virtual-screen pixel coordinates; foreground focus is not changed. |
| `POST /v1/input/keys` | JSON `{"text":"hello"}` (1–256 UTF-16 units, Unicode SendInput) **or** `{"virtualKey":13}` (single VK down/up). |

Handles are hexadecimal (with optional `0x`); window handles can be recycled.
Element paths refer to the **current** UIA child ordering, not durable identities:
inspect a fresh tree immediately before acting, and be prepared for stale paths.
Unavailable UIA properties use safe fallbacks (`bounds: null`, missing text
as null, unsupported actions as false); a failed provider read marks the tree
`truncated`. Child paths retain original UIA sibling indexes even when another
descendant is unavailable, so they continue to address the displayed element.
Operations do not cross Windows integrity/UIPI, secure-desktop, session, or UAC
boundaries. Prefer UIA invoke/set-value; SendInput is fallback and targets the
current focused application. UIA providers can hang or change between calls;
v1 has bounded tree depth/node output but cannot impose a hard timeout inside
third-party UIA provider calls. A failure yields a problem-details response,
not a privilege escalation. Invalid coordinates, unavailable windows and
unsupported patterns have distinct errors.

Screen capture uses Win32 PrintWindow (including full-content flag) for a
window, falling back to BitBlt of *visible* desktop pixels if it fails; desktop
capture uses BitBlt of the virtual desktop. An occluded GPU/protected window can
be blank, and a minimized window may have no useful pixels. A capture is capped
at 40 megapixels and 16384 pixels per side. WPF encodes the 32-bit DIB as PNG;
no System.Drawing.Common dependency. Screen pixels and automation tree contents
are sensitive: do not send them to untrusted clients.

## Lifecycle

`publish.ps1 -Version x.y.z` makes folder-based **self-contained win-x64** host
and CLI artifacts under `artifacts/publish/x.y.z`. The Windows target needs no
.NET SDK or runtime; only the development/publishing machine needs the SDK.
Keep each published folder intact (WPF/FlaUI/native dependencies are not
single-file bundled). Run the installed copy from a Windows-local path; directly
launching a self-contained EXE from a WSL UNC source path is not a supported
runtime configuration. `install.ps1 -Version x.y.z [-PackagePath <directory>]`
copies into a GUID staging directory then renames it to a new release. It writes
stable launchers and atomically replaces `current.txt` on the same volume only
after the complete release is ready. Existing releases are never overwritten;
previous release, data and token survive an update. `update.ps1` requires an
existing installation and delegates to install. A running host continues on
the old release until restarted; never replace its files in place. Rollback:
stop the host, set `current.txt` to the previously installed version (one line,
no path), restart. Uninstall: stop the host, then `uninstall.ps1`; by default it
deletes product releases and data (including token); `-KeepData` retains data
and logs. Nothing touches unrelated programs or global .NET installs.

Windows PowerShell may require a process-scoped execution-policy override to
run local scripts. Use the installed `wcu.cmd` from Windows; WSL network
forwarding to Windows loopback varies by WSL mode, and is not part of the API
security boundary. Bind remains Windows loopback only.
