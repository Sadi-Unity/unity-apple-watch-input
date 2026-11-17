using UnityEngine;
using WatchInput.Core;
using WatchInput.Native;

namespace WatchInput.Sources
{
    internal sealed class SimulatedWatchSource : IWatchInputSource
    {
        private const int PendingCapacity = 16;
        private readonly WI_Event[] _pendingEvents = new WI_Event[PendingCapacity];
        private int _pendingCount;

        private float _accumulatedCrownDelta;
        private bool _isMouseDown;
        private float _mouseDownTime;
        private bool _pressBeganSent;
        private bool _isPressed;
        private bool _debugLogging;

        private static readonly string[] HapticLogMessages = new string[]
        {
            "[WatchInput Sim] Haptic: Notification",
            "[WatchInput Sim] Haptic: DirectionUp",
            "[WatchInput Sim] Haptic: DirectionDown",
            "[WatchInput Sim] Haptic: Success",
            "[WatchInput Sim] Haptic: Failure",
            "[WatchInput Sim] Haptic: Retry",
            "[WatchInput Sim] Haptic: Start",
            "[WatchInput Sim] Haptic: Stop",
            "[WatchInput Sim] Haptic: Click"
        };

        public void Initialize()
        {
            _pendingCount = 0;
            _accumulatedCrownDelta = 0f;
            _isMouseDown = false;
            _mouseDownTime = 0f;
            _pressBeganSent = false;
            _isPressed = false;
        }

        public WatchStatus GetStatus()
        {
            return WatchStatus.Supported |
                   WatchStatus.Activated |
                   WatchStatus.Paired |
                   WatchStatus.Installed |
                   WatchStatus.Reachable;
        }

        public int DrainEvents(WI_Event[] buffer, int capacity)
        {
            PollInput();

            int toDrain = _pendingCount < capacity ? _pendingCount : capacity;
            for (int i = 0; i < toDrain; i++)
            {
                buffer[i] = _pendingEvents[i];
            }

            if (toDrain < _pendingCount)
            {
                int remaining = _pendingCount - toDrain;
                for (int i = 0; i < remaining; i++)
                {
                    _pendingEvents[i] = _pendingEvents[toDrain + i];
                }
                _pendingCount = remaining;
            }
            else
            {
                _pendingCount = 0;
            }

            return toDrain;
        }

        public float ConsumeCrownDelta()
        {
            float delta = _accumulatedCrownDelta;
            _accumulatedCrownDelta = 0f;
            return delta;
        }

        public bool IsPressed()
        {
            return _isPressed;
        }

        public bool PlayHaptic(WatchHaptic type)
        {
            int idx = (int)type;
            if (idx >= 0 && idx < HapticLogMessages.Length)
            {
                Debug.Log(HapticLogMessages[idx]);
            }
            return true;
        }

        public void SetDebugLogging(bool enabled)
        {
            _debugLogging = enabled;
            if (_debugLogging)
            {
                Debug.Log("[WatchInput Sim] Debug logging enabled.");
            }
        }

        private void EnqueueEvent(WIEventType type, byte arg)
        {
            if (_pendingCount < PendingCapacity)
            {
                _pendingEvents[_pendingCount].type = (byte)type;
                _pendingEvents[_pendingCount].arg = arg;
                _pendingEvents[_pendingCount].value = 0f;
                _pendingEvents[_pendingCount].timestamp = 0;
                _pendingCount++;
            }
        }

        private void PollInput()
        {
            bool mouseDownThisFrame = false;
            bool mouseUpThisFrame = false;
            bool mouseHeld = false;
            float scrollDeltaY = 0f;

#if ENABLE_INPUT_SYSTEM && WATCH_INPUT_SYSTEM_AVAILABLE
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse != null)
            {
                mouseDownThisFrame = mouse.leftButton.wasPressedThisFrame;
                mouseUpThisFrame = mouse.leftButton.wasReleasedThisFrame;
                mouseHeld = mouse.leftButton.isPressed;
                Vector2 scroll = mouse.scroll.ReadValue();
                scrollDeltaY = scroll.y;
            }

            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null)
            {
                if (kb.upArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame)
                    EnqueueEvent(WIEventType.Swipe, (byte)WatchSwipe.Up);
                if (kb.downArrowKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame)
                    EnqueueEvent(WIEventType.Swipe, (byte)WatchSwipe.Down);
                if (kb.leftArrowKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame)
                    EnqueueEvent(WIEventType.Swipe, (byte)WatchSwipe.Left);
                if (kb.rightArrowKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame)
                    EnqueueEvent(WIEventType.Swipe, (byte)WatchSwipe.Right);
            }
#elif ENABLE_LEGACY_INPUT_MANAGER
            mouseDownThisFrame = Input.GetMouseButtonDown(0);
            mouseUpThisFrame = Input.GetMouseButtonUp(0);
            mouseHeld = Input.GetMouseButton(0);
            scrollDeltaY = Input.mouseScrollDelta.y;

            if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W))
                EnqueueEvent(WIEventType.Swipe, (byte)WatchSwipe.Up);
            if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S))
                EnqueueEvent(WIEventType.Swipe, (byte)WatchSwipe.Down);
            if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A))
                EnqueueEvent(WIEventType.Swipe, (byte)WatchSwipe.Left);
            if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D))
                EnqueueEvent(WIEventType.Swipe, (byte)WatchSwipe.Right);
#endif

            // Crown accumulation: 1 notch = 1.0 tick
            if (scrollDeltaY > 0.001f)
            {
                _accumulatedCrownDelta += 1f;
            }
            else if (scrollDeltaY < -0.001f)
            {
                _accumulatedCrownDelta -= 1f;
            }

            // Mouse button timing: < 0.3s = tap; >= 0.3s held = press
            if (mouseDownThisFrame)
            {
                _isMouseDown = true;
                _mouseDownTime = Time.realtimeSinceStartup;
                _pressBeganSent = false;
            }

            if (_isMouseDown && !_pressBeganSent && mouseHeld)
            {
                if (Time.realtimeSinceStartup - _mouseDownTime >= 0.3f)
                {
                    _pressBeganSent = true;
                    _isPressed = true;
                    EnqueueEvent(WIEventType.PressBegin, 0);
                }
            }

            if (mouseUpThisFrame && _isMouseDown)
            {
                _isMouseDown = false;
                if (_pressBeganSent)
                {
                    _pressBeganSent = false;
                    _isPressed = false;
                    EnqueueEvent(WIEventType.PressEnd, 0);
                }
                else
                {
                    EnqueueEvent(WIEventType.Tap, 0);
                }
            }
        }
    }
}
