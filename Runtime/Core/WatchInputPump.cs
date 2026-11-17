using System;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;
using WatchInput.Native;
using WatchInput.Sources;

namespace WatchInput.Core
{
    // Marker type identifying our system inside the PlayerLoop.
    internal struct WatchInputUpdatePump { }

    internal static class WatchInputPump
    {
        private const int EventBufferCapacity = 16;
        private static readonly WI_Event[] EventBuffer = new WI_Event[EventBufferCapacity];

        internal static IWatchInputSource ActiveSource { get; private set; }
        private static float _crownRemainder;
        private static bool _isPumpInstalled;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            RemovePlayerLoop();
            Watch.ResetAllState();
            ActiveSource = null;
            _crownRemainder = 0f;
            _isPumpInstalled = false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void InitializeAndInstall()
        {
            if (ActiveSource == null)
            {
#if UNITY_IOS && !UNITY_EDITOR
                ActiveSource = new NativeWatchSource();
                Debug.Log("[WatchInput] Active Source: NativeWatchSource (iOS Device)");
#else
                ActiveSource = new SimulatedWatchSource();
#if ENABLE_INPUT_SYSTEM && WATCH_INPUT_SYSTEM_AVAILABLE
                Debug.Log("[WatchInput] Active Source: SimulatedWatchSource (Editor / Standalone, New Input System)");
#elif ENABLE_LEGACY_INPUT_MANAGER
                Debug.Log("[WatchInput] Active Source: SimulatedWatchSource (Editor / Standalone, Legacy Input Manager)");
#else
                Debug.Log("[WatchInput] Active Source: SimulatedWatchSource (Editor / Standalone, No Active Input System)");
#endif
#endif
                ActiveSource.Initialize();
                // Apply a DebugLogging value the user may have set before the source existed.
                ActiveSource.SetDebugLogging(Watch.DebugLogging);
            }

            InstallPlayerLoop();
            Application.quitting -= RemovePlayerLoop;
            Application.quitting += RemovePlayerLoop;
        }

        private static void InstallPlayerLoop()
        {
            if (_isPumpInstalled)
            {
                return;
            }

            PlayerLoopSystem rootLoop = PlayerLoop.GetCurrentPlayerLoop();
            if (rootLoop.subSystemList == null)
            {
                return;
            }

            for (int i = 0; i < rootLoop.subSystemList.Length; i++)
            {
                if (rootLoop.subSystemList[i].type == typeof(PreUpdate))
                {
                    PlayerLoopSystem preUpdate = rootLoop.subSystemList[i];
                    if (preUpdate.subSystemList != null)
                    {
                        for (int j = 0; j < preUpdate.subSystemList.Length; j++)
                        {
                            if (preUpdate.subSystemList[j].type == typeof(WatchInputUpdatePump))
                            {
                                _isPumpInstalled = true;
                                return;
                            }
                        }
                    }

                    int currentLength = preUpdate.subSystemList != null ? preUpdate.subSystemList.Length : 0;
                    PlayerLoopSystem[] newSubSystems = new PlayerLoopSystem[currentLength + 1];
                    if (currentLength > 0)
                    {
                        Array.Copy(preUpdate.subSystemList, newSubSystems, currentLength);
                    }

                    newSubSystems[currentLength] = new PlayerLoopSystem
                    {
                        type = typeof(WatchInputUpdatePump),
                        updateDelegate = UpdatePump
                    };

                    preUpdate.subSystemList = newSubSystems;
                    rootLoop.subSystemList[i] = preUpdate;
                    PlayerLoop.SetPlayerLoop(rootLoop);
                    _isPumpInstalled = true;
                    break;
                }
            }
        }

        private static void RemovePlayerLoop()
        {
            if (!_isPumpInstalled)
            {
                return;
            }

            PlayerLoopSystem rootLoop = PlayerLoop.GetCurrentPlayerLoop();
            if (rootLoop.subSystemList == null)
            {
                _isPumpInstalled = false;
                return;
            }

            for (int i = 0; i < rootLoop.subSystemList.Length; i++)
            {
                if (rootLoop.subSystemList[i].type == typeof(PreUpdate))
                {
                    PlayerLoopSystem preUpdate = rootLoop.subSystemList[i];
                    if (preUpdate.subSystemList != null)
                    {
                        int foundIndex = -1;
                        for (int j = 0; j < preUpdate.subSystemList.Length; j++)
                        {
                            if (preUpdate.subSystemList[j].type == typeof(WatchInputUpdatePump))
                            {
                                foundIndex = j;
                                break;
                            }
                        }

                        if (foundIndex != -1)
                        {
                            PlayerLoopSystem[] newSubSystems = new PlayerLoopSystem[preUpdate.subSystemList.Length - 1];
                            for (int j = 0, k = 0; j < preUpdate.subSystemList.Length; j++)
                            {
                                if (j != foundIndex)
                                {
                                    newSubSystems[k++] = preUpdate.subSystemList[j];
                                }
                            }
                            preUpdate.subSystemList = newSubSystems;
                            rootLoop.subSystemList[i] = preUpdate;
                            PlayerLoop.SetPlayerLoop(rootLoop);
                        }
                    }
                    break;
                }
            }

            _isPumpInstalled = false;
        }

        private static void UpdatePump()
        {
            if (ActiveSource == null)
            {
                return;
            }

            // 1. Reset per-frame state flags and queues
            Watch.ResetFrameState();

            // 2. Poll session status
            Watch.Status = ActiveSource.GetStatus();

            // 3. Drain discrete events and build this frame's state FIRST.
            //    Events are invoked only after the whole frame state is final, so a
            //    handler that reads Watch.IsPressed / CrownSteps sees this frame's values.
            int eventCount = ActiveSource.DrainEvents(EventBuffer, EventBufferCapacity);
            for (int i = 0; i < eventCount; i++)
            {
                WI_Event ev = EventBuffer[i];
                switch ((WIEventType)ev.type)
                {
                    case WIEventType.Tap:
                        Watch.Tapped = true;
                        Watch.TapCount++;
                        break;
                    case WIEventType.PressBegin:
                        Watch.PressStarted = true;
                        break;
                    case WIEventType.PressEnd:
                        Watch.PressEnded = true;
                        break;
                    case WIEventType.Swipe:
                        Watch.EnqueueSwipe((WatchSwipe)ev.arg);
                        break;
                }
            }

            // 4. Source of truth for IsPressed: polled every frame (native side includes the watchdog).
            Watch.IsPressed = ActiveSource.IsPressed();

            // 5. Crown accumulation and integer steps. Remainder resets only on direction change.
            float delta = ActiveSource.ConsumeCrownDelta();
            Watch.CrownDelta = delta;
            if (delta != 0f)
            {
                if (_crownRemainder * delta < 0f)
                {
                    _crownRemainder = 0f;
                }

                float total = _crownRemainder + delta;
                int steps = (int)total; // truncation towards zero
                Watch.CrownSteps = steps;
                _crownRemainder = total - steps;
            }

            // 6. Invoke events in arrival order, now that frame state is complete.
            for (int i = 0; i < eventCount; i++)
            {
                WI_Event ev = EventBuffer[i];
                switch ((WIEventType)ev.type)
                {
                    case WIEventType.Tap:        Watch.InvokeTap(); break;
                    case WIEventType.PressBegin: Watch.InvokePressBegan(); break;
                    case WIEventType.PressEnd:   Watch.InvokePressReleased(); break;
                    case WIEventType.Swipe:      Watch.InvokeSwiped((WatchSwipe)ev.arg); break;
                }
            }

            if (delta != 0f)
            {
                Watch.InvokeCrownRotated(delta);
            }
        }
    }
}
