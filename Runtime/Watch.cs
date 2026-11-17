using System;
using UnityEngine;
using WatchInput.Core;

namespace WatchInput
{
    /// <summary>
    /// Unified Apple Watch input API for Unity.
    /// Provides zero-allocation per-frame polling, swipe queue consumption, haptic triggering, and event callbacks.
    /// Automatically driven via PlayerLoop PreUpdate without requiring scene objects.
    /// </summary>
    public static class Watch
    {
        // --- Per-frame state flags ---
        public static bool Tapped { get; internal set; }
        public static int TapCount { get; internal set; }
        public static bool PressStarted { get; internal set; }
        public static bool IsPressed { get; internal set; }
        public static bool PressEnded { get; internal set; }
        public static float CrownDelta { get; internal set; }
        public static int CrownSteps { get; internal set; }

        // --- Status ---
        public static bool IsReachable => (Status & WatchStatus.Reachable) != 0;
        public static WatchStatus Status { get; internal set; }

        public static bool DebugLogging
        {
            get => _debugLogging;
            set
            {
                _debugLogging = value;
                WatchInputPump.ActiveSource?.SetDebugLogging(value);
            }
        }
        private static bool _debugLogging;

        // --- Events ---
        public static event Action Tap;
        public static event Action<WatchSwipe> Swiped;
        public static event Action PressBegan;
        public static event Action PressReleased;
        public static event Action<float> CrownRotated;

        // --- Swipe FIFO Queue (Fixed Capacity, Zero GC) ---
        private const int SwipeQueueCapacity = 8;
        private static readonly WatchSwipe[] SwipeQueue = new WatchSwipe[SwipeQueueCapacity];
        private static int _swipeHead;
        private static int _swipeCount;

        /// <summary>
        /// Attempts to dequeue the next swipe direction received during this frame.
        /// Returns true if a swipe was retrieved; false if queue is empty.
        /// </summary>
        public static bool TryGetSwipe(out WatchSwipe direction)
        {
            if (_swipeCount > 0)
            {
                direction = SwipeQueue[_swipeHead];
                _swipeHead = (_swipeHead + 1) % SwipeQueueCapacity;
                _swipeCount--;
                return true;
            }

            direction = default;
            return false;
        }

        /// <summary>
        /// Sends a haptic feedback vibration to the Apple Watch.
        /// Subject to native 80 ms rate-limiting and reachability checks.
        /// </summary>
        public static bool PlayHaptic(WatchHaptic type)
        {
            var source = WatchInputPump.ActiveSource;
            return source != null && source.PlayHaptic(type);
        }

        // --- Internal state management (Called by WatchInputPump) ---

        internal static void ResetFrameState()
        {
            Tapped = false;
            TapCount = 0;
            PressStarted = false;
            PressEnded = false;
            CrownDelta = 0f;
            CrownSteps = 0;
            _swipeHead = 0;
            _swipeCount = 0;
        }

        internal static void ResetAllState()
        {
            ResetFrameState();
            IsPressed = false;
            Status = WatchStatus.None;
            _debugLogging = false;
        }

        internal static void EnqueueSwipe(WatchSwipe direction)
        {
            if (_swipeCount < SwipeQueueCapacity)
            {
                int index = (_swipeHead + _swipeCount) % SwipeQueueCapacity;
                SwipeQueue[index] = direction;
                _swipeCount++;
            }
        }

        internal static void InvokeTap()
        {
            try
            {
                Tap?.Invoke();
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        internal static void InvokeSwiped(WatchSwipe direction)
        {
            try
            {
                Swiped?.Invoke(direction);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        internal static void InvokePressBegan()
        {
            try
            {
                PressBegan?.Invoke();
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        internal static void InvokePressReleased()
        {
            try
            {
                PressReleased?.Invoke();
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        internal static void InvokeCrownRotated(float delta)
        {
            try
            {
                CrownRotated?.Invoke(delta);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }
    }
}
