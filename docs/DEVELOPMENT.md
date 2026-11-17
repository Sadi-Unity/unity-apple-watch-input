# How this package was built

One developer, AI coding agents, less than a day — on top of earlier experience shipping an
Apple Watch–controlled iOS game. This document is about the *process*: who did what, how the work was
checked, and what the checks caught. The commit history is the timeline.

## Roles

| | Done by |
|---|---|
| Scope, architecture, protocol design, API design | developer (with a planning assistant) |
| Specifications / prompts for each round | developer (with a planning assistant) |
| Writing the code | AI coding agent (via Unity MCP) |
| Code review (reading every change from disk) | developer (with a review assistant) |
| Builds, signing, on-device testing, measurements | developer |
| Diagnosis of on-device failures | developer (with a review assistant) |

## The loop

Every round followed the same steps:

1. **Spec.** One small, testable goal per round, an explicit file list and a *do-not-touch* list.
2. **Phase A — read-only plan.** The agent reads the existing code and writes a plan: data layouts,
   state machines, file list, risks. It is not allowed to write files.
3. **Review the plan.** Wrong assumptions are corrected here, where they are cheapest.
4. **Phase B — implement** against the corrected plan. The agent re-reads every file from disk,
   refreshes Unity and reports the raw console. Reports separate *verified* from *done but not verified*.
5. **Review the diff from disk**, not from the agent's summary. Small fixes are made directly.
6. **Device test** with a concrete checklist and pasted logs. Nothing in the README is a number that wasn't measured.

The rounds: build automation → watch app + native bridge → public C# API + editor simulation →
settings panel + signing + icon → tests → sample.

## Defects caught

Review of plans and diffs:

1. **Invented API.** A plan relied on `WCSession.delegateQueue`, which does not exist.
2. **Gesture state machine.** After a swipe the recogniser returned to *idle* while the finger was still down,
   so the rest of the same touch would have produced phantom taps. Added a *consumed* state.
3. **Digital Crown trailing flush.** Rate-limited crown deltas left the last few detents on the watch until the next
   rotation. Added a trailing flush.
4. **Unneeded `unsafe`.** A blittable struct array is pinned by the marshaller without a copy.
5. **Timer-free watchdog.** The press timeout is checked lazily when the state is read instead of by a timer thread.
6. **Plugin platforms.** The Objective-C++ bridge was enabled for tvOS/visionOS, where WatchConnectivity doesn't exist.
7. **Event ordering.** Events fired before the frame state was complete, so a `PressBegan` handler saw
   `IsPressed == false`. The pump now builds the whole frame first, then fires events.
8. **Lost setting.** `DebugLogging` set before the input source existed was silently dropped.
9. **Time-based crown decay** was proposed and rejected: input behaviour that depends on wall-clock time is
   hard to test and surprising.

On device / in tests:

10. **IL2CPP stripped the runtime assembly.** On device the bridge never started and nothing was logged. The build
    output showed the native plugin and the startup registration were present, but the assembly was missing from the
    IL2CPP C++ output: nothing in a user project references it, so the linker never processed it.
    Fixed with `[assembly: AlwaysLinkAssembly]`.
11. **Stale input — the first fix was wrong.** Taps made while the game was closed arrived as a burst at launch.
    The first fix dropped packets while the session was not reachable. The device disproved it: iOS wakes the app in
    the background for watch messages, so the session *is* reachable and the burst still arrived (15 taps after a
    background/foreground cycle). The real fix checks the **age of the input**, not the state of the connection:
    the watch timestamps every packet, the phone drops anything older than 1 s and anything that arrives while the
    app is not active. Verified: 10 taps while closed → 10 dropped, ages 24–28 s.
12. **Subscriber isolation (found by a test).** With two subscribers, an exception in the first prevented the second
    from running. `GetInvocationList()` would allocate every frame, so events now use a handler array that is rebuilt
    only on subscribe/unsubscribe and invoked with a per-handler `try/catch`.
13. **Measurement changed a constant.** Packet ages during the first second of a session reached 493 ms — just under
    the original 500 ms stale threshold. Raised to 1000 ms; real stale packets were 24 s old.
14. **Misleading watch status.** The watch showed "Connected" while the iPhone game was closed (watch-side
    reachability doesn't mean the iPhone app is running) and "Open the iPhone app" while the wrist was down.
    The phone now publishes its active state through the application context, and the watch reads the dimmed-screen state.

## Example spec (abridged)

```
ROUND 3b — stale input fix
Finding (diagnosis done, you implement): dropping packets while the session is not
reachable does not work. iOS wakes the app in the background for watch messages;
packets are delivered while reachable, buffered, and read in one burst on resume.

Changes
1. Protocol v2: input packets = [ver u8][type u8][sentMs f64 LE][payload].
2. Phone: ageMs = now - sentMs; drop if > threshold; debug log min/avg/max age.
3. Phone: drop input while the app is not active; on resign-active clear the
   ring buffer, crown accumulator and press state.
4. Remove the reachability check from the input path.

Step 0 (report before writing): every encoder and every parser with line numbers,
and the new payload offsets as a table.

Do not touch: gesture state machine, public C# API.
Report: verified / done-not-verified / for you to check on device.
```

## Measurements

All numbers in the README come from an iPhone 16 Pro Max + Apple Watch Series 8 on watchOS 26.6:
latency from packet timestamps (debug log statistics over 50-packet windows), press keep-alive intervals from
receive timestamps, screen-awake times with a stopwatch.
