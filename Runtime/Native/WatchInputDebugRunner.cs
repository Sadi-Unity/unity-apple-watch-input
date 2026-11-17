// TEMPORARY — removed in Tur 3
using UnityEngine;

namespace WatchInput.Native
{
    /// <summary>
    /// TEMPORARY debug runner for Tur 2 device testing. Will be removed in Tur 3.
    /// Initializes native bridge, drains events each frame, logs them,
    /// and sends haptic feedback on TAP (click) and SWIPE (success) for round-trip testing.
    /// GC allocation rules are waived for this temporary class.
    /// </summary>
    public class WatchInputDebugRunner : MonoBehaviour
    {
        private WI_Event[] _eventBuffer;
        private uint _lastStatus;
        private const int EventBufferCapacity = 16;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoCreate()
        {
            var go = new GameObject("[WatchInput DebugRunner — TEMPORARY]");
            DontDestroyOnLoad(go);
            go.AddComponent<WatchInputDebugRunner>();
        }

        void Awake()
        {
            _eventBuffer = new WI_Event[EventBufferCapacity];
            WatchInputNative.WI_Init();
            WatchInputNative.WI_SetDebugLog(1);
            Debug.Log("[WatchInput] DebugRunner initialized (TEMPORARY).");
        }

        void Update()
        {
            // Log status changes
            uint status = WatchInputNative.WI_GetStatus();
            if (status != _lastStatus)
            {
                _lastStatus = status;
                Debug.Log($"[WatchInput] Status changed: {(WIStatus)status}");
            }

            // Drain discrete events from ring buffer
            int count = WatchInputNative.WI_DrainEvents(_eventBuffer, EventBufferCapacity);
            for (int i = 0; i < count; i++)
            {
                WI_Event ev = _eventBuffer[i];
                Debug.Log($"[WatchInput] Event: type={(WIEventType)ev.type} arg={ev.arg} value={ev.value:F3} ts={ev.timestamp}");

                // Round-trip haptic test: feel the event on the watch
                switch ((WIEventType)ev.type)
                {
                    case WIEventType.Tap:
                        WatchInputNative.WI_SendHaptic((int)WIHapticType.Click);
                        break;
                    case WIEventType.Swipe:
                        WatchInputNative.WI_SendHaptic((int)WIHapticType.Success);
                        break;
                }
            }

            // Consume crown delta (non-zero only)
            float crownDelta = WatchInputNative.WI_ConsumeCrownDelta();
            if (crownDelta != 0f)
            {
                Debug.Log($"[WatchInput] Crown delta: {crownDelta:F3} ticks");
            }
        }
    }
}
