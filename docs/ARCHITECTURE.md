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
| `POST /v1/windows/{handle}/activate` | No body. Restores if minimized, first attempts ordinary Win32 foreground activation, and returns `{handle,isForeground:true}` **only** after `GetForegroundWindow` confirms this handle. If that ordinary attempt is denied, it temporarily joins same-desktop input queues with `AttachThreadInput`, retries, then immediately detaches. If a packaged/UWP-style window still rejects Win32 activation, UIA top-level `SetFocus` is attempted as a final compatibility fallback; the foreground handle is still the sole success authority. |
| `GET /v1/windows/{handle}/tree?depth=3&maxNodes=200` | `{handle,root,nodeCount,truncated}`. Root path `[]`; children use zero-based path segments. `depth` 0–10, `maxNodes` 1–1000. |
| `GET /v1/windows/{handle}/screenshot` | `image/png` + `X-WCU-Target`, `X-WCU-Width`, `X-WCU-Height` headers. |
| `GET /v1/desktop/screenshot` | Same PNG and metadata headers for entire virtual desktop. |
| `POST /v1/actions/invoke` | JSON `{"handle":"0x1234","path":[0,1]}`. InvokePattern only. |
| `POST /v1/actions/set-value` | JSON `{"handle":"0x1234","path":[0],"value":"text"}`. ValuePattern only, max 4096 UTF-16 units. |
| `POST /v1/elements/inspect` | JSON `{"handle":"0x1234","locator":{"automationId":"editor","controlType":"Edit"}}`. Returns a fresh `ElementState` (name, value if ValuePattern supported, control type, automation ID, enabled, focused, offscreen, bounds, Invoke/Value support). No full tree returned. |
| `POST /v1/elements/focus` | Same locator request; direct UIA SetFocus and readback. Returns state only if the selected window is foreground **and** UIA reports that element has keyboard focus. Does not call the activation operation; activate explicitly first. |
| `POST /v1/actions/invoke-located` | Same locator request; fresh lookup, then InvokePattern. Success acknowledges the pattern call, not its application-specific effect. |
| `POST /v1/actions/set-value-located` | JSON `{"handle":"0x1234","locator":{"automationId":"editor"},"value":"text"}`; fresh lookup, then ValuePattern. Verify via inspect. |
| `POST /v1/input/click` | JSON `{"x":100,"y":200}` virtual-screen pixel coordinates; foreground focus is not changed. |
| `POST /v1/input/keys` | JSON `{"text":"hello"}` (1–256 UTF-16 units, Unicode SendInput) **or** `{"virtualKey":13}` (single VK down/up). |

Handles are hexadecimal (with optional `0x`); window handles can be recycled.
Semantic locators use one or more **case-sensitive, ordinal, exact** `automationId`,
`name`, and `controlType` fields (up to 256 UTF-16 units each). An optional
`ancestor` uses the same fields, must itself uniquely identify one element, and
scopes matching to its *descendants*; nested ancestors are invalid. Window root
may be an ancestor. Examples: `{"automationId":"num7Button"}` and
`{"automationId":"ValidationText","controlType":"Edit","ancestor":
{"name":"WCU Semantic Validation","controlType":"Window"}}`. No selector
language, fuzzy matching or hidden element cache. Each inspect/focus/invoke/
set-value call resolves against the current UIA tree, scans at most 2000 nodes
through depth 20 inclusive per search (shared node budget for scoped searches),
and refuses to claim uniqueness only when nodes beyond that boundary exist or
another search budget is exceeded. Locators can themselves become stale
when UI labels/IDs change; client should re-observe or use more stable fields.
Unavailable provider data aborts semantic lookup rather than returning a false
no-match or a false unique match.

Locator failures use problem-details `code`: `invalid_locator` (400),
`locator_not_found` / `locator_ancestor_not_found` (404),
`locator_ambiguous` / `locator_ancestor_ambiguous` (409),
`locator_limit_exceeded` / `locator_provider_failed` (422). Activation fails
with `activation_not_confirmed` (409) when Windows refuses foreground access;
focus fails with `element_not_focusable` (422), `focus_not_confirmed` (409), or
`focus_unverifiable` (422) rather than pretending success. `element_state_unavailable`
(422) and `action_failed` (422) are distinct from `unsupported_pattern` (422)
on semantic actions. Windows
foreground rules, desktop/session isolation and UIPI/UAC are not bypassed. The activation helper is limited to standard same-desktop input-thread attachment; failure to attach or foreground still returns `activation_not_confirmed`.

The core does **not** wait for UI transitions. Clients can poll inspect at a
bounded cadence/deadline and decide which state is sufficient for their task;
focus/activation success reflects immediately observed state. UIA providers may
still stall in a native call, so an HTTP timeout is not a Windows provider kill.

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

## Operator semantic acceptance (after an operator-owned release)

From an interactive Windows PowerShell session, start the *new* installed host,
open Calculator in Standard mode (this test clears its display and enters 7),
and run from this repository:

```powershell
.\scripts\semantic-acceptance.ps1 -ExpectedVersion '0.2.4'  # replace with actual new version
# For localized Calculator titles, supply its handle from `wcu windows`:
.\scripts\semantic-acceptance.ps1 -ExpectedVersion '0.2.4' -CalculatorHandle 0x123456
```

The script uses the per-user token (never prints it), creates and removes a
disposable WPF edit/button/status window, and verifies scoped locate → fresh
inspect → activate → focus → ValuePattern/readback → InvokePattern/readback.
It then verifies Calculator's exact `clearButton`/`num7Button` locators and
`CalculatorResults` readback without paths. Its polling is bounded and lives
only in the acceptance client, not the service. Calculator automation IDs can
vary by Windows release; inspect the live tree if this app-specific assertion
fails. The existing `scripts/acceptance.ps1` remains for legacy API coverage.
