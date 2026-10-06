# Computer-use session review — 2026-10-06

## Scope and evidence

Review of [Stillwater Relay](../mementos/0.4.0/stillwater-relay.md) and [Quiet Presence](../mementos/0.4.0/quiet-presence.md), created through WCU 0.4.0 / assembly 0.4.0.0, OpenCode Connect 0.7.0 build `434a7f64ab3d3018` (commit `1bf2186c5ded931f577c6c404164d97fa3731d41`), and Paint 11.2605.81.0. Repository baseline: `93487fb`.

These were exploratory artwork sessions, not controlled end-to-end benchmarks. Stillwater's 46.84 seconds covers only 13 retained construction sequences: 199 steps and 54 drags. It excludes setup, discovery, recovery, screenshots, export and operator/tool round trips. Quiet Presence has no comparable action-time total. No speedup or failure-rate estimate can be inferred.

This review combines recorded session observations, source inspection, the adapter's existing tests, and one desktop-free reproduction. Session observations are not a complete raw event log. No live Paint dialog was reopened during this review; completed artwork and existing windows were preserved.

## Findings

| ID | Finding | Evidence status | Priority / owner |
| --- | --- | --- | --- |
| CU-01 | A wait can release backend serialization while its observation is unresolved | Reproduced with a fake transport; code confirms lifecycle gap | P0 / OpenCode Connect |
| CU-02 | Semantic access to Paint's color dialog accompanied repeated unresponsive instances | Observed; native root cause unresolved | P0 investigation / WCU + adapter |
| CU-03 | Native error distinctions are lost at the bridge | Confirmed in source; observed generic HTTP 422/500 errors | P0 / adapter |
| CU-04 | Input acknowledgement does not establish dialog readiness or committed text | Observed; documented SendInput semantics, not proof of lost events | P1 / adapter workflows |
| CU-05 | Unicode text exists in WCU HTTP but is inaccessible through computer tools and CLI | Confirmed capability gap | P1 / adapter + CLI |
| CU-06 | `canSetValue` describes ValuePattern support rather than actual writability | Confirmed contract weakness; exact slider failure unresolved | P1 / WCU |

### CU-01: wait timeout leaves an observation in flight

Owner: `/home/jesrey0/projects/.worktrees/opencode-computer-use/src/computer.ts`, `waitObservation` and `serialized`.

The wait uses `Promise.race` against a timer. The timer rejects the wait without cancelling or retiring `executeObserve`. The failed sequence returns, releasing its backend queue slot; a subsequent tool call can dispatch another transport request while the first observation is unresolved.

A disposable probe imported the real ComputerBackend with a fake transport. Its first request remained unresolved; a 50 ms window wait returned `wait_timeout`, `polls:1`. A subsequent move then reached the fake transport before the first request was resolved:

```json
[
  {"path":"/v1/windows","priorObservationUnresolved":false},
  {"path":"/v1/input/move","priorObservationUnresolved":true}
]
```

No desktop input was injected. This proves overlapping dispatch at the backend/transport boundary, not simultaneous execution inside Paint. The production PowerShell bridge processes requests sequentially, so the next request may queue behind the stale one. Its separate timeout may reset the bridge and reject pending requests. HTTP cancellation or killing that bridge does not terminate a native UIA call already running in WCU.

The existing test “wait deadline also bounds a stalled observation without replay or later mutation” checks only later steps in the same failed sequence. It does not submit a new call before retiring the observation. All **36 existing tests in `test/computer.test.ts` passed** on review; the extra probe reproduced this missing case.

Fix direction: distinguish an unmet condition from an observation that did not finish; retain or quarantine the request until settlement or explicit transport recovery. Bound recovery, reject unsafe follow-up mutations, preserve read-only recovery paths, and never automatically replay an uncertain action. A local timer alone is insufficient.

### CU-02: Paint modal/provider stalls

Stillwater: semantic invocation of Edit colors opened the dialog, then timed out with two acknowledged composition steps; desktop capture showed Paint “Not Responding.” Later semantic searches failed. A replacement blank instance completed the landscape.

Quiet Presence: opening Edit colors followed by semantic observation produced a 3-second wait failure with only one poll, then an unresponsive Paint instance. Another blank instance later stalled during semantic setValue with readback disabled. The completed portrait used pointer/keyboard entry and screenshots.

Source: WCU's [semantic implementation](../src/WindowsComputerUse.Core/WindowsAutomation.Semantic.cs) calls UIA synchronously, directly from [HTTP handlers](../src/WindowsComputerUse.Host/Program.cs). There is no process-isolated provider executor or request cancellation propagated into those calls. [Architecture](ARCHITECTURE.md) already documents that a client timeout cannot kill a third-party provider call.

This explains a containment gap, but does not identify the Paint freeze's cause. Modal invocation, locator traversal, property access, activation fallback and provider behavior must be isolated in a disposable fixture. Do not label the issue a proven STA deadlock, or equate replacing a Paint process with a fix.

### CU-03: insufficient error diagnostics

`src/computerBridgeScript.ts` exports only HTTP status for most failures. It preserves exactly two absence codes, only from a 404 inspect response. WCU already distinguishes `locator_limit_exceeded`, `locator_provider_failed`, `unsupported_pattern`, `action_failed`, focus and activation errors. Those distinctions are currently discarded.

Consequently, the observed Save as traversal 422 and Size slider setValue 422 cannot be diagnosed retrospectively. Readback can also fail after a mutation has been acknowledged. Composition `completedSteps` counts acknowledged HTTP exchanges, not proven absence of application effects.

Preserve a fixed allowlist of native scalar codes and a bounded operation phase. Continue suppressing upstream titles, exception text, response bodies and credentials. Unknown codes must remain generic failures; provider errors must never count as element absence.

### CU-04 / CU-05: dialog readiness and text entry

Rapidly batching textbox focus, Ctrl+A, hexadecimal typing, Tab and OK sometimes acknowledged every action while the expected color was not committed. Shape menu transitions also required later observation. Separate calls with approximately 200 ms spacing and screenshots completed the portrait; some custom-color transitions cost roughly 20–26 seconds including tool round trips. These are exploratory observations, not a measured percentile.

WCU [NativeInput.Keys](../src/WindowsComputerUse.Core/NativeInput.cs) already supports Unicode SendInput via `POST /v1/input/keys`, bounded to 1–256 UTF-16 units. The adapter schema and bridge allowlist expose keySequence but not this text operation; the CLI likewise lacks a text command.

Expose the existing primitive rather than constructing text from layout-dependent VK chords. It still needs verified focus and application-specific commit/readiness checks. Unicode entry alone does not establish that Paint consumed or validated the text. Use exact state predicates where reliable; any fallback settling interval must be explicit and bounded.

### CU-06: semantic capability accuracy

Paint's Size slider reported `canSetValue:true` and value “1 pixel”; setting “3” failed with HTTP 422. Focus plus arrow keys produced verified “4 pixel” and later “2 pixel.”

WCU computes the flag from `element.Patterns.Value.IsSupported`. It does not check ValuePattern.IsReadOnly; its setter supports ValuePattern only, with no numeric RangeValue operation. This is a confirmed limitation in how callers interpret the flag. It does **not** prove that this slider requires RangeValue or that read-only status caused this particular 422.

Report pattern support separately from writability, and inspect numeric patterns before deciding to add numeric setting. Disabled, read-only and formatted-value controls need separate fixtures and truthful failures.

## Other edge cases and boundaries

| Case | Interpretation / required handling |
| --- | --- |
| Save as semantic traversal returned 422 | Unresolved; capture safe native code and traversal limits before changing search behavior. Owned modal roots may differ from the parent window. |
| Active Paint ellipse changed color during palette edits | Paint's uncommitted shape behavior; commit/deselect before changing color. Not evidence of incorrect WCU rasterization. |
| Flood fill crossed connected same-color regions or left patches | Paint's region-fill semantics and artwork construction; inspect pixels and undo/rebuild boundaries. Not a demonstrated input defect. |
| Resizing/recentering changed canvas origin; maximized window bounds included negative offsets | Re-observe capture origin, display bounds and canvas geometry. Test DPI, negative desktop coordinates and movement between capture and action. |
| Another application can gain foreground between activation and pointer input | Input is currently desktop-global. Add explicit target guards in a later story; fail before injection when foreground/geometry is stale. No competing-input incident was demonstrated here. |
| Error after an action, lost bridge, or dialog disappears mid-operation | Reconcile visible state and saved files before deciding to repeat. Zero acknowledged exchanges does not establish zero effects. |

## Review outcome

The immediate bottleneck was reliable dialog transitions and recovery, rather than insufficient drag capacity. The next sprint is specified in [NEXT_SPRINT.md](NEXT_SPRINT.md). This review changes documentation only; no runtime fix, release, service restart or deployment is claimed.
