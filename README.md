# Watch Input for Unity

Use **Apple Watch** as an input device for Unity iOS games — taps, swipes, press-and-hold
and the Digital Crown on the watch, haptic feedback back to the wrist.
Install the package and your iOS build **automatically gains a watchOS app**. No Xcode editing,
no scene setup.

<p align="center"><img src="docs/demo.gif" width="340" alt="Tapping, swiping, holding and turning the Digital Crown on an Apple Watch; the iPhone shows each input"></p>

```csharp
using WatchInput;

void Update()
{
    if (Watch.Tapped)                       Jump();
    if (Watch.TryGetSwipe(out var dir))     Dash(dir);
    if (Watch.IsPressed)                    ChargeShot();
    menu.Move(Watch.CrownSteps);            // whole Digital Crown detents this frame
}

void OnHit() => Watch.PlayHaptic(WatchHaptic.Success);
```

---

## The build step is the hard part

Unity regenerates the Xcode project from scratch on every iOS build, so a watchOS app can't simply
live in it. This package adds one from code, every build, in a single `PostProcessBuild` step:

- adds a single-target SwiftUI **watchOS app target** (`SDKROOT=watchos`, device family 4,
  `WKApplication`, companion bundle ID, `SKIP_INSTALL`, versions mirrored from Player Settings)
- copies the Swift sources from the package and adds them with **relative** paths
- adds the **Embed Watch Content** phase and moves it before Unity's postprocess phase
  (the Xcode API can't reorder phases, so this is a guarded text edit that warns instead of failing silently)
- links `WatchConnectivity.framework` to `UnityFramework`
- generates the watch **app icon** asset catalog and a Swift config file from your settings
- **inherits signing** from Player Settings (automatic or manual) — it never changes the signing of your iOS app
- is **idempotent**: Unity's *Append* builds don't add anything twice

Everything is configured from **Project Settings › Watch Input**, and a pre-build check turns
missing setup into one readable line in the Unity console instead of an Xcode error.

## Install

Requirements: Unity 6 (6000.0+) with iOS Build Support, Xcode, an iPhone paired with an Apple Watch.

**Package Manager › + › Add package from git URL**

```
https://github.com/Sadi-Unity/unity-apple-watch-input.git
```

Then:

1. **Player Settings › iOS**: set *Signing Team ID* (and *Automatically Sign* if you use it).
2. **Project Settings › Watch Input**: watch app name, icon (1024×1024 PNG, no alpha), Digital Crown options.
3. Build for iOS. Run from Xcode. If the watch app doesn't appear on the watch, install it from the
   iPhone **Watch** app (see Troubleshooting).

**Sample:** Package Manager › Watch Input for Unity › Samples › **Basic Demo** › Import —
a scene that shows every input on screen and plays haptics back.
The haptic played for each input is chosen in the Inspector on the *Watch Input Demo* object — or call
`Watch.PlayHaptic(...)` from your own code; the package carries input, the feel is up to your game.

## API

All members are static on `WatchInput.Watch`. Per-frame values are valid for the frame they were produced in,
like Unity's `Input` class. Nothing has to be added to a scene.

| Member | |
|---|---|
| `Tapped`, `TapCount` | tap(s) this frame |
| `TryGetSwipe(out WatchSwipe dir)` | next swipe this frame (`Up`, `Down`, `Left`, `Right`) |
| `PressStarted`, `IsPressed`, `PressEnded` | press-and-hold (≥ 0.3 s) |
| `CrownDelta` | Digital Crown rotation this frame, in detents (float) |
| `CrownSteps` | whole detents this frame; the fraction carries over to the next frame |
| `PlayHaptic(WatchHaptic)` | haptic on the watch (9 types, rate-limited to 80 ms) |
| `IsReachable`, `Status` | connection state |
| `DebugLogging` | native logs, including packet-age (latency) statistics |
| events `Tap`, `Swiped`, `PressBegan`, `PressReleased`, `CrownRotated` | same data as callbacks |

**In the Editor** (and on non-iOS platforms) the same API is driven by mouse/touch and keyboard:
click = tap, hold = press, arrow keys / WASD = swipe, scroll wheel = crown. Works with the Input System
package and the legacy Input Manager; the active source is logged once at startup.

## Design decisions

- **Recognition happens on the watch.** A single `DragGesture` state machine turns touches into
  tap / press / swipe; the phone receives meaningful events, not raw touches.
- **Small binary protocol** over `WCSession.sendMessageData` (2–14 bytes per message, version byte first)
  instead of dictionary messages.
- **Every input carries its send time.** iOS can wake the app in the background for a watch message and
  delivers messages sent while the app was closed when it next launches. Packets older than 1 s, and packets
  that arrive while the app is not active, are dropped — the game never receives a burst of stale taps.
- **Press is a state channel, not just two events.** While held, the watch repeats a keep-alive every 250 ms;
  if the phone hears nothing for 600 ms it releases the press itself. A lost "release" can't leave a button stuck.
- **Pull model across the native boundary.** The Objective-C++ bridge buffers events in a fixed ring buffer;
  C# drains it once per frame into a preallocated blittable struct array through a C ABI.
  No `UnitySendMessage`, no callbacks from foreign threads, **no per-frame GC allocations**.
- **PlayerLoop instead of a GameObject.** The input pump is injected into `PreUpdate`, survives scene loads and
  works with domain reload disabled. Frame state is completed before any event fires, and every subscriber is
  isolated: one throwing handler doesn't stop the others.
- **`[assembly: AlwaysLinkAssembly]`.** The package's only entry point is a `RuntimeInitializeOnLoadMethod`;
  without this attribute IL2CPP silently stripped the whole assembly from player builds.
- **Phone state is published with `updateApplicationContext`** (latest-value semantics, delivered even when the
  watch app isn't running), so the watch can tell "open the game on your iPhone" apart from "raise your wrist".

More detail: [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md).

## Limitations (measured, not assumed)

Measured on iPhone 16 Pro Max + Apple Watch Series 8, watchOS 26.6, default display settings.
Other models and settings will differ.

- **Foreground only.** Input flows while the watch app is open and the screen is awake.
  There is no background session (no HealthKit workout, no `WKExtendedRuntimeSession`).
  - Untouched, the screen stayed awake ~70 s.
  - With a touch every 5–15 s it never slept (tested > 2 min).
  - Lowering the wrist dims the screen and pauses the connection; raising it reconnects immediately.
- **Latency** (watch send → phone receive, from packet timestamps): typically 55–110 ms average with peaks
  around 175 ms once the session is warm; up to ~490 ms in the first second after connecting. Watch and phone
  clocks are synchronised by the system, so these numbers include any small clock offset.
- **Press keep-alive** intervals were observed between ~205 and ~450 ms (target 250 ms); the 600 ms release
  timeout leaves ~150 ms of margin.
- **Manual signing** is implemented but has only been tested with automatic signing on a device.
- Swipes that start at the very edge of the watch screen may be taken by the system.

## Troubleshooting

- **Watch app not installed / not updated after a build** — open the iPhone **Watch** app and install it from
  *Available Apps*. If it doesn't update, delete it from the watch and install again.
- **"Open the game on your iPhone" on the watch** — the iPhone app is not in the foreground.
- **Signing errors in Xcode** — check the `[WatchInput] Preflight:` lines in the Unity console; set Signing Team ID
  in Player Settings rather than in Xcode (Xcode edits are lost on the next build).
- **Nothing arrives in Unity** — set `Watch.DebugLogging = true` and watch the Xcode console for `[WatchInput]` lines.

## Tests

39 EditMode tests cover the build step against a hand-written fixture Xcode project (target creation,
idempotency, build settings, signing branches, icon catalog, embed-phase ordering), the settings validators
and the runtime input pump (crown remainder, event ordering, subscriber isolation, swipe queue).
To run them in your project add `"testables": ["com.sadiakdemir.watch-input"]` to `Packages/manifest.json`.

## How this was built

Built in less than a day by one developer working with AI coding agents, on top of prior experience shipping
an Apple Watch–controlled iOS game. The agents wrote the code; architecture, protocol design, specifications,
code review, on-device diagnosis and measurement were done by hand, in a plan → review → implement → review →
device-test loop. Review and device testing caught a dozen defects, including an invented API, a silent IL2CPP
stripping issue and a wrong first hypothesis about stale input that the device disproved.
The process and the defect log: [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md).

## License

[MIT](LICENSE)
