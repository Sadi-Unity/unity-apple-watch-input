using System.Runtime.InteropServices;

namespace WatchInput.Native
{
    // Discrete event types delivered via the ring buffer.
    // Crown is handled separately via WI_ConsumeCrownDelta.
    internal enum WIEventType : byte
    {
        Tap        = 0x01,
        PressBegin = 0x02,
        PressHeld  = 0x03,
        PressEnd   = 0x04,
        Swipe      = 0x05,
    }

    // 2 bytes, 1-byte packed. Blittable — single allocation, automatic pinning.
    // Must match native WI_Event layout exactly.
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct WI_Event
    {
        public byte type; // WIEventType
        public byte arg;  // direction for SWIPE, 1=watchdog, 2=app resigned for synthetic PRESS_END
    }

    /// <summary>
    /// P/Invoke declarations for the native WatchInputBridge.
    /// On non-iOS or in Editor: safe stub implementations returning defaults.
    /// </summary>
    internal static class WatchInputNative
    {
#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] public static extern void  WI_Init();
        [DllImport("__Internal")] public static extern uint  WI_GetStatus();
        [DllImport("__Internal")] public static extern int   WI_DrainEvents([Out] WI_Event[] buf, int capacity);
        [DllImport("__Internal")] public static extern float WI_ConsumeCrownDelta();
        [DllImport("__Internal")] public static extern int   WI_IsPressed();
        [DllImport("__Internal")] public static extern int   WI_SendHaptic(int type);
        [DllImport("__Internal")] public static extern void  WI_SetDebugLog(int on);
#else
        public static void  WI_Init() { }
        public static uint  WI_GetStatus() => 0;
        public static int   WI_DrainEvents(WI_Event[] buf, int capacity) => 0;
        public static float WI_ConsumeCrownDelta() => 0f;
        public static int   WI_IsPressed() => 0;
        public static int   WI_SendHaptic(int type) => 0;
        public static void  WI_SetDebugLog(int on) { }
#endif
    }
}
