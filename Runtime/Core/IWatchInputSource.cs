using WatchInput.Native;

namespace WatchInput.Core
{
    internal interface IWatchInputSource
    {
        void Initialize();
        WatchStatus GetStatus();
        int DrainEvents(WI_Event[] buffer, int capacity);
        float ConsumeCrownDelta();
        bool IsPressed();
        bool PlayHaptic(WatchHaptic type);
        void SetDebugLogging(bool enabled);
    }
}
