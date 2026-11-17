using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using WatchInput;
using WatchInput.Core;
using WatchInput.Native;
using WatchInput.Sources;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace WatchInput.Tests.Editor
{
    [TestFixture]
    public class WatchInputPumpTests
    {
        private FakeWatchInputSource _fakeSource;

        private class FakeWatchInputSource : IWatchInputSource
        {
            public readonly List<WI_Event> EventsToDrain = new List<WI_Event>();
            public float NextCrownDelta;
            public bool PressedState;
            public WatchStatus Status = WatchStatus.Supported | WatchStatus.Activated | WatchStatus.Paired | WatchStatus.Installed | WatchStatus.Reachable;

            public void Initialize()
            {
                EventsToDrain.Clear();
                NextCrownDelta = 0f;
                PressedState = false;
            }

            public WatchStatus GetStatus() => Status;

            public int DrainEvents(WI_Event[] buffer, int capacity)
            {
                int count = Math.Min(EventsToDrain.Count, capacity);
                for (int i = 0; i < count; i++)
                {
                    buffer[i] = EventsToDrain[i];
                }
                EventsToDrain.RemoveRange(0, count);
                return count;
            }

            public float ConsumeCrownDelta()
            {
                float delta = NextCrownDelta;
                NextCrownDelta = 0f;
                return delta;
            }

            public bool IsPressed() => PressedState;

            public bool PlayHaptic(WatchHaptic type) => true;

            public void SetDebugLogging(bool enabled) { }
        }

        [SetUp]
        public void SetUp()
        {
            _fakeSource = new FakeWatchInputSource();
            _fakeSource.Initialize();
            WatchInputPump.SetSourceForTesting(_fakeSource);
        }

        [TearDown]
        public void TearDown()
        {
            WatchInputPump.ResetForTesting();
        }

        [Test]
        public void Pump_AccumulatesCrownStepsAndRetainsFractionalRemainder()
        {
            // Frame 1: delta = 0.4 -> steps = 0, remainder = 0.4
            _fakeSource.NextCrownDelta = 0.4f;
            WatchInputPump.TickForTesting();
            Assert.AreEqual(0, Watch.CrownSteps, "Frame 1 steps should be 0");
            Assert.AreEqual(0.4f, Watch.CrownDelta, 0.001f);

            // Frame 2: delta = 0.4 -> total = 0.8 -> steps = 0, remainder = 0.8
            _fakeSource.NextCrownDelta = 0.4f;
            WatchInputPump.TickForTesting();
            Assert.AreEqual(0, Watch.CrownSteps, "Frame 2 steps should be 0");

            // Frame 3: delta = 0.4 -> total = 1.2 -> steps = 1, remainder = 0.2
            _fakeSource.NextCrownDelta = 0.4f;
            WatchInputPump.TickForTesting();
            Assert.AreEqual(1, Watch.CrownSteps, "Frame 3 steps should be 1");
        }

        [Test]
        public void Pump_ResetsCrownRemainder_WhenRotationDirectionInverts()
        {
            // Frame 1: delta = 0.8 -> steps = 0, remainder = 0.8
            _fakeSource.NextCrownDelta = 0.8f;
            WatchInputPump.TickForTesting();
            Assert.AreEqual(0, Watch.CrownSteps);

            // Frame 2: direction inverts (negative delta = -0.5)
            // Remainder should reset to 0 before accumulating: total = -0.5 -> steps = 0, remainder = -0.5
            _fakeSource.NextCrownDelta = -0.5f;
            WatchInputPump.TickForTesting();
            Assert.AreEqual(0, Watch.CrownSteps);

            // Frame 3: delta = -0.6 -> total = -1.1 -> steps = -1
            // If remainder hadn't reset, 0.8 - 0.5 - 0.6 = -0.3 and steps would still be 0!
            _fakeSource.NextCrownDelta = -0.6f;
            WatchInputPump.TickForTesting();
            Assert.AreEqual(-1, Watch.CrownSteps, "Inverted rotation should accumulate in new direction");
        }

        [Test]
        public void Pump_TwoPassUpdate_IsPressedTrueDuringPressBeganCallback()
        {
            _fakeSource.EventsToDrain.Add(new WI_Event { type = (byte)WIEventType.PressBegin });
            _fakeSource.PressedState = true;

            bool isPressedObservedInCallback = false;
            Watch.PressBegan += () =>
            {
                isPressedObservedInCallback = Watch.IsPressed;
            };

            WatchInputPump.TickForTesting();

            Assert.IsTrue(isPressedObservedInCallback, "Watch.IsPressed must be true inside PressBegan callback due to two-pass pump update");
        }

        [Test]
        public void Pump_SubscriberException_DoesNotHaltInputProcessing()
        {
            LogAssert.Expect(LogType.Exception, new Regex(".*Deliberate test subscriber exception.*"));

            bool secondSubscriberExecuted = false;
            Watch.Tap += () => throw new InvalidOperationException("Deliberate test subscriber exception");
            Watch.Tap += () => { secondSubscriberExecuted = true; };

            _fakeSource.EventsToDrain.Add(new WI_Event { type = (byte)WIEventType.Tap });

            WatchInputPump.TickForTesting();

            Assert.IsTrue(Watch.Tapped, "Watch.Tapped state flag must be set despite subscriber exception");
            Assert.IsTrue(secondSubscriberExecuted, "Second subscriber must execute even if first throws exception");
        }

        [Test]
        public void Pump_SubscriberSelfUnsubscribe_SucceedsWithoutErrorAndDoesNotRunInNextFrame()
        {
            int callCount = 0;
            Action selfRemovingHandler = null;
            selfRemovingHandler = () =>
            {
                callCount++;
                Watch.Tap -= selfRemovingHandler;
            };

            Watch.Tap += selfRemovingHandler;

            // Frame 1: Tap event arrives
            _fakeSource.EventsToDrain.Add(new WI_Event { type = (byte)WIEventType.Tap });
            WatchInputPump.TickForTesting();
            Assert.AreEqual(1, callCount, "Handler should be called in first frame");

            // Frame 2: Another Tap event arrives
            _fakeSource.EventsToDrain.Add(new WI_Event { type = (byte)WIEventType.Tap });
            WatchInputPump.TickForTesting();
            Assert.AreEqual(1, callCount, "Handler should not be called again after self-unsubscribing");
        }

        [Test]
        public void Pump_SwipeQueue_MaintainsFIFOOrder_AndRespectsCapacity8()
        {
            // Enqueue 10 swipe events: Up, Down, Left, Right, Up, Down, Left, Right, Up, Down
            WatchSwipe[] testSwipes = new[]
            {
                WatchSwipe.Up, WatchSwipe.Down, WatchSwipe.Left, WatchSwipe.Right,
                WatchSwipe.Up, WatchSwipe.Down, WatchSwipe.Left, WatchSwipe.Right,
                WatchSwipe.Up, WatchSwipe.Down
            };

            for (int i = 0; i < testSwipes.Length; i++)
            {
                _fakeSource.EventsToDrain.Add(new WI_Event
                {
                    type = (byte)WIEventType.Swipe,
                    arg = (byte)testSwipes[i]
                });
            }

            WatchInputPump.TickForTesting();

            // Dequeue first 8 items: should match the first 8 swipes exactly in FIFO order
            for (int i = 0; i < 8; i++)
            {
                bool dequeued = Watch.TryGetSwipe(out WatchSwipe direction);
                Assert.IsTrue(dequeued, $"Swipe item at index {i} should be dequeued");
                Assert.AreEqual(testSwipes[i], direction, $"Swipe item at index {i} must maintain FIFO order");
            }

            // 9th dequeue attempt should fail because capacity is capped at 8
            bool extraDequeued = Watch.TryGetSwipe(out WatchSwipe extraDir);
            Assert.IsFalse(extraDequeued, "Swipe queue capacity must strictly enforce maximum of 8 entries");
        }

        [Test]
        public void Pump_PerFrameFlags_ResetOnSubsequentUpdate()
        {
            _fakeSource.EventsToDrain.Add(new WI_Event { type = (byte)WIEventType.Tap });
            _fakeSource.EventsToDrain.Add(new WI_Event { type = (byte)WIEventType.PressBegin });
            _fakeSource.NextCrownDelta = 0.5f;

            WatchInputPump.TickForTesting();
            Assert.IsTrue(Watch.Tapped);
            Assert.AreEqual(1, Watch.TapCount);
            Assert.IsTrue(Watch.PressStarted);
            Assert.AreEqual(0.5f, Watch.CrownDelta, 0.001f);

            // Next tick without new events
            WatchInputPump.TickForTesting();
            Assert.IsFalse(Watch.Tapped, "Tapped flag must reset on next frame");
            Assert.AreEqual(0, Watch.TapCount, "TapCount must reset on next frame");
            Assert.IsFalse(Watch.PressStarted, "PressStarted must reset on next frame");
            Assert.AreEqual(0f, Watch.CrownDelta, 0.001f, "CrownDelta must reset on next frame");
        }

        [Test]
        public void Pump_WhenPressActive_AndSourceGivesIsPressedFalseWithPressEndEvent_TriggersPressReleasedAndSetsPressEnded()
        {
            // Frame 1: Establish pressed state
            _fakeSource.EventsToDrain.Add(new WI_Event { type = (byte)WIEventType.PressBegin });
            _fakeSource.PressedState = true;
            WatchInputPump.TickForTesting();
            Assert.IsTrue(Watch.IsPressed, "Watch should be pressed after frame 1");

            // Frame 2: Source transitions to unpressed with synthetic/regular PressEnd event (e.g. arg = 2 from app resignation)
            _fakeSource.EventsToDrain.Add(new WI_Event { type = (byte)WIEventType.PressEnd, arg = 2 });
            _fakeSource.PressedState = false;

            bool pressReleasedFired = false;
            Watch.PressReleased += () => pressReleasedFired = true;

            WatchInputPump.TickForTesting();

            Assert.IsFalse(Watch.IsPressed, "Watch.IsPressed must be false");
            Assert.IsTrue(Watch.PressEnded, "Watch.PressEnded must be true");
            Assert.IsTrue(pressReleasedFired, "Watch.PressReleased event must be fired");
        }

        [Test]
        public void Pump_WhenPressActive_AndSourceGivesIsPressedFalseWithoutPressEndEvent_SynthesizesPressEndedAndPressReleased()
        {
            // Frame 1: Establish pressed state
            _fakeSource.EventsToDrain.Add(new WI_Event { type = (byte)WIEventType.PressBegin });
            _fakeSource.PressedState = true;
            WatchInputPump.TickForTesting();
            Assert.IsTrue(Watch.IsPressed, "Watch should be pressed after frame 1");

            // Frame 2: Source transitions to unpressed WITHOUT PressEnd event (event loss defense via falling edge)
            _fakeSource.PressedState = false;

            bool pressReleasedFired = false;
            Watch.PressReleased += () => pressReleasedFired = true;

            WatchInputPump.TickForTesting();

            Assert.IsFalse(Watch.IsPressed, "Watch.IsPressed must be false");
            Assert.IsTrue(Watch.PressEnded, "Watch.PressEnded must be synthesized true on falling edge");
            Assert.IsTrue(pressReleasedFired, "Watch.PressReleased event must be triggered on falling edge");
        }

        [Test]
        public void Pump_TickForTesting_AllocatesZeroGCMemory_WithoutEvents()
        {
            // Warm-up call
            WatchInputPump.TickForTesting();

            // Assert zero GC memory allocated on per-frame hot path without events
            Assert.That(() => WatchInputPump.TickForTesting(), Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void Pump_TickForTesting_AllocatesZeroGCMemory_WithEvents()
        {
            // Prepare events for warm-up
            _fakeSource.EventsToDrain.Add(new WI_Event { type = (byte)WIEventType.Tap });
            _fakeSource.NextCrownDelta = 1.0f;

            // Warm-up call
            WatchInputPump.TickForTesting();

            // Prepare identical events for measured run
            _fakeSource.EventsToDrain.Add(new WI_Event { type = (byte)WIEventType.Tap });
            _fakeSource.NextCrownDelta = 1.0f;

            // Assert zero GC memory allocated on per-frame hot path with events
            Assert.That(() => WatchInputPump.TickForTesting(), Is.Not.AllocatingGCMemory());
        }
    }
}
