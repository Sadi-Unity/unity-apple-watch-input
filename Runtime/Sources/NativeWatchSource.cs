using WatchInput.Core;
using WatchInput.Native;

namespace WatchInput.Sources
{
    internal sealed class NativeWatchSource : IWatchInputSource
    {
        public void Initialize()
        {
            WatchInputNative.WI_Init();
        }

        public WatchStatus GetStatus()
        {
            return (WatchStatus)WatchInputNative.WI_GetStatus();
        }

        public int DrainEvents(WI_Event[] buffer, int capacity)
        {
            return WatchInputNative.WI_DrainEvents(buffer, capacity);
        }

        public float ConsumeCrownDelta()
        {
            return WatchInputNative.WI_ConsumeCrownDelta();
        }

        public bool IsPressed()
        {
            return WatchInputNative.WI_IsPressed() != 0;
        }

        public bool PlayHaptic(WatchHaptic type)
        {
            return WatchInputNative.WI_SendHaptic((int)type) != 0;
        }

        public void SetDebugLogging(bool enabled)
        {
            WatchInputNative.WI_SetDebugLog(enabled ? 1 : 0);
        }
    }
}
