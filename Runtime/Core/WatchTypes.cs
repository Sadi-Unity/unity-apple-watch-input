using System;

namespace WatchInput
{
    public enum WatchSwipe : byte
    {
        Up    = 0,
        Down  = 1,
        Left  = 2,
        Right = 3
    }

    public enum WatchHaptic : int
    {
        Notification  = 0,
        DirectionUp   = 1,
        DirectionDown = 2,
        Success       = 3,
        Failure       = 4,
        Retry         = 5,
        Start         = 6,
        Stop          = 7,
        Click         = 8
    }

    [Flags]
    public enum WatchStatus : uint
    {
        None      = 0,
        Supported = 1u << 0,
        Activated = 1u << 1,
        Paired    = 1u << 2,
        Installed = 1u << 3,
        Reachable = 1u << 4
    }
}
