# Next sprint — computer-use reliability

Proposed execution backlog from the [2026-10-06 session review](COMPUTER_USE_REVIEW_2026-10-06.md). This records planned work, not completed fixes or a promised release date. It spans WCU and the owning OpenCode Connect adapter; each change belongs in its respective repository.

**Goal:** complete a repeated color-edit / drawing / save workflow with bounded, observable recovery and fewer separate text-entry calls.

## Ordered sprint backlog

| Order | Story | Owner | Exit condition |
| --- | --- | --- | --- |
| 1 / P0 | Retire timed-out observations before admitting subsequent mutations (CU-01) | OpenCode Connect | Regression covers a new tool call after timeout, late reply, rejection, bridge reset and continued recovery |
| 2 / P0 | Preserve safe native error codes and action phases (CU-03) | OpenCode Connect | 422/409 failures distinguish provider, locator, unsupported-pattern and focus/activation cases without exporting arbitrary text |
| 3 / P0 | Reproduce and contain modal UIA stalls (CU-02) | WCU + adapter | Deliberately stalled provider cannot consume unbounded workers or block independent health/window/desktop observation |
| 4 / P1 | Expose existing Unicode text and verify dialog transitions (CU-04/05) | OpenCode Connect + WCU CLI | Focused Unicode entry, application commit and readiness are tested; uncertain actions are never silently retried |
| 5 / P1 | Make element capability state truthful (CU-06) | WCU + adapter schemas | Read-only/disabled ValuePattern controls are distinguished from writable controls; slider behavior has a diagnosed native code |

Finish the P0 recovery chain before expanding input convenience. If capacity is limited, story 5 is the first carry-over; retain its diagnostic coverage in the sprint. Do not turn this into an arbitrary macro/scripting engine.

### 1. Observation lifecycle and timeout semantics

- Extend ComputerTransport with an explicit request lifecycle/recovery contract. A wait deadline must not silently abandon ownership of an unresolved observation.
- Report condition timeout separately from stalled observation/transport timeout. Do not report a predicate as observed false when it was never obtained.
- Keep no-auto-replay behavior and acknowledged composition counts. Explain unknown application effect after cancellation.
- Deterministic tests: timeout followed immediately by a new interact call; late successful and failed replies; a queued screenshot; multiple queued calls; transport reset; shutdown while pending. Assert no unsafe mutation reaches a stale transport.
- Recovery must itself be bounded. Simply awaiting an uncancellable provider forever is not acceptable. Bridge retirement alone does not establish that a WCU provider invocation has stopped.

### 2. Useful diagnostics without upstream text

- Add an explicit scalar allowlist for WCU problem codes across the appropriate routes, with bounded phase metadata: activation, lookup/action, readback or observation.
- Keep outer sequence index and inner acknowledged exchange count distinct.
- Test malformed JSON, excessive bodies, array/object codes, unknown codes and secret-like titles/details. Only approved codes and metadata cross the boundary.
- Re-run the Size slider and Save as cases in a disposable app window, record their actual code and phase, and update the review's unresolved entries.

### 3. Modal stall investigation and containment

- Build a disposable Windows test fixture covering a button that opens a modal dialog, delayed property reads, deliberately stalled reads and unavailable controls. Compare invoke, pointer-open, inspect, focus and setValue independently.
- Establish the stalled operation and whether app responsiveness changes. Record app/host/adapter versions, handle/PID, method, phase, deadline and recovery result without credentials.
- Use a bounded isolated execution design where needed for native provider calls; specify worker/concurrency caps and termination behavior. A Task timeout or CancellationToken alone cannot interrupt arbitrary COM/provider execution.
- Acceptance: an intentionally stuck provider returns a distinct bounded failure; independent health, window listing and desktop capture remain available; repeated stalls do not leak unbounded helpers; a fresh unrelated fixture remains usable.
- Preserve user applications and artwork. Do not treat terminating Paint or restarting the whole service as normal recovery.

### 4. Text entry and application readiness

- Map a native text action through schema, discovery, bridge and sequence to WCU's existing `/v1/input/keys`. Preserve the 1–256 UTF-16-unit limit and literal NDJSON data handling; add CLI parity.
- Test empty/oversize input, accented characters, surrogate pairs, punctuation and literal shell-like strings. Check focus failure and foreground change before injection.
- Validate a bounded workflow: open color dialog → verify textbox focus → replace text → commit → confirm ready state → draw. Use existing exact waits where available; name any missing predicate needed by the fixture.
- Where UIA is unsafe, retain screenshot verification and an explicit bounded settling fallback. Do not claim SendInput acknowledgement is application readback.
- Compare the workflow's round trips and wall time against an equivalently instrumented current-version baseline. Text convenience must retain correctness, not merely reduce calls.

### 5. Capabilities and slider semantics

- Separate supported ValuePattern from writable state; account for IsReadOnly and enabled state with unavailable-provider data represented explicitly.
- Determine whether the actual Size control supports RangeValue before adding numeric APIs. If numeric support is added, include min/max/current value and explicit bounds validation.
- Cover writable edit, read-only edit, disabled edit, numeric slider, formatted display value and a provider that becomes unavailable after discovery.
- A stale capability must yield a typed failure and fresh observation, never a false success.

## Benchmark and acceptance protocol

Use a small repeatable fixture workflow rather than redrawing a portrait as a timing test. Establish an instrumented 0.4.0 baseline, then run the changed version under the same window size, display scale, app version and initial state.

1. Execute 20 color transitions, 10 bounded strokes and one save/export in each run. Verify committed colors, fixture stroke results, actual saved file dimensions/content and final application state.
2. Run five measured repetitions for baseline and candidate; retain raw samples. Report all failures/recoveries and describe the small sample size. Do not infer broad reliability or tail latency from it.
3. Separate queue delay, adapter/bridge execution, native operation, readiness wait, screenshot/tool round trip, recovery and end-to-end wall time. Existing sequence totals can include backend queue delay; per-step totals do not describe the entire session.
4. Include modal-open/close, slow and hung providers, ambiguous/stale locators, traversal limits, readonly/disabled controls, foreground loss, window movement, DPI/negative desktop origins and partial completion. Unknown/provider failure must not satisfy an absence predicate.
5. Run WCU's documented build/tests and capability acceptance plus the adapter's required checks. Verify the deployed versions independently after any separately authorized release; source tests alone do not establish live acceptance.

Definition of done: the P0 fixtures pass, the complete workflow verifies application effects, recovery is bounded, error evidence is actionable, and the before/after report includes failures and full wall time. The memento PNGs and provenance remain historical evidence.

## Follow-on backlog

Target-bound pointer/drag guards and screenshot coordinate provenance are next after the above reliability work: include capture origin, virtual-desktop bounds and geometry freshness, and reject stale foreground/geometry before injection. Broader application coverage and better drawing ergonomics follow measured results. Increasing drag budgets or adding arbitrary scripts has no demonstrated priority from these sessions.
