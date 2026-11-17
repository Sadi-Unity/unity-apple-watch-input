# Changelog

## [0.1.0] - 2026-11-17

First release.

### Added
- Automatic watchOS app target in Unity iOS builds (`PostProcessBuild`): sources, build settings, embed phase,
  WatchConnectivity link, app icon, generated Swift config, signing inherited from Player Settings.
- Project Settings › Watch Input panel with live validation and a non-blocking pre-build check.
- Watch app: tap, press-and-hold, four-way swipe, Digital Crown (unbounded, detent haptics), status text.
- Haptics from Unity to the watch (9 types).
- `WatchInput.Watch` static API with per-frame state and events, driven from the PlayerLoop.
- Editor / non-iOS simulation with mouse, touch and keyboard (Input System and legacy Input Manager).
- Timestamped binary protocol (v2); stale and background input is dropped.
- Basic Demo sample.
- 39 EditMode tests.
