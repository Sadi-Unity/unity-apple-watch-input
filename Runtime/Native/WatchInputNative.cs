using System;
using System.Runtime.InteropServices;

namespace WatchInput.Native
{
    // Discrete event types delivered via the ring buffer.
    // Crown is handled separately via WI_ConsumeCrownDelta.
    public enum WIEventType : byte
    {
        Tap        = 0x01,
        PressBegin = 0x02,
        PressHeld  = 0x03,
        PressEnd   = 0x04,
        Swipe      = 0x05,
    }

    public enum WISwipeDirection : byte
    {
        Up    = 0,
        Down  = 1,
        Left  = 2,
        Right = 3,
    }

    // Explicit enum matching WKHapticType values via switch on the watch side.
    public enum WIHapticType : int
    {
        Notification  = 0,
        DirectionUp   = 1,
        DirectionDown = 2,
        Success       = 3,
        Failure       = 4,
        Retry         = 5,
        Start         = 6,
        Stop          = 7,
        Click         = 8,
    }

    // Session status bitmask — mirrors native WIStatusFlag.
    [Flags]
    public enum WIStatus : uint
    {
        None      = 0,
        Supported = 1u << 0,
        Activated = 1u << 1,
        Paired    = 1u << 2,
        Installed = 1u << 3,
        Reachable = 1u << 4,
    }

    // 12 bytes, 4-byte aligned. Blittable — single allocation, automatic pinning.
    // Must match native WI_Event layout exactly.
    [StructLayout(LayoutKind.Sequential, Size = 12)]
    public struct WI_Event
    {
        public byte  type;      // WIEventType
        public byte  arg;       // direction for SWIPE, 1=watchdog for synthetic PRESS_END
        private byte _pad0;
        private byte _pad1;
        public float value;     // unused for discrete events (0.0)
        public uint  timestamp; // milliseconds since WI_Init
    }

    /// <summary>
    /// P/Invoke declarations for the native WatchInputBridge.
    /// On non-iOS or in Editor: safe stub implementations returning defaults.
    /// </summary>
    public static class WatchInputNative
    {
#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] public static extern void  WI_Init();
        [DllImport("__Internal")] public static extern uint  WI_GetStatus();
        [DllImport("__Internal")] public static extern int   WI_DrainEvents([Out] WI_Event[] buf, int capacity);
        [DllImport("__Internal")] public static extern float WI_ConsumeCrownDelta();
        [DllImport("__Internal")] public static extern int   WI_IsPressed();
        [DllImport("__Internal")] public static extern int   WI_SendHaptic(int type);
        [DllImport("__Internal")] public static extern int   WI_GetDroppedCount();
        [DllImport("__Internal")] public static extern int   WI_GetStaleDroppedCount();
        [DllImport("__Internal")] public static extern void  WI_SetDebugLog(int on);
#else
        public static void  WI_Init() { }
        public static uint  WI_GetStatus() => 0;
        public static int   WI_DrainEvents(WI_Event[] buf, int capacity) => 0;
        public static float WI_ConsumeCrownDelta() => 0f;
        public static int   WI_IsPressed() => 0;
        public static int   WI_SendHaptic(int type) => 0;
        public static int   WI_GetDroppedCount() => 0;
        public static int   WI_GetStaleDroppedCount() => 0;
        public static void  WI_SetDebugLog(int on) { }
#endif
    }
}
