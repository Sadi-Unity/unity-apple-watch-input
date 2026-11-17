#import <WatchConnectivity/WatchConnectivity.h>
#import <QuartzCore/QuartzCore.h>
#include <os/lock.h>
#include <stdint.h>
#include <string.h>
#include <atomic>

// ---------------------------------------------------------------------------
// Protocol constants
// ---------------------------------------------------------------------------
static const uint8_t  kProtocolVersion      = 0x01;
static const int      kRingBufferCapacity   = 64;
static const uint64_t kPressWatchdogMs      = 600;
static const uint64_t kHapticMinIntervalMs  = 80;
static const uint8_t  kHapticTypeMax        = 8;

// Event types — must match Swift MessageType and C# WIEventType
typedef enum : uint8_t {
    WIEventType_Tap        = 0x01,
    WIEventType_PressBegin = 0x02,
    WIEventType_PressHeld  = 0x03,
    WIEventType_PressEnd   = 0x04,
    WIEventType_Swipe      = 0x05,
    WIEventType_Crown      = 0x06,
} WIEventType;

// Status bitmask — must match C# WIStatus
typedef enum : uint32_t {
    WIStatus_Supported  = 1u << 0,
    WIStatus_Activated  = 1u << 1,
    WIStatus_Paired     = 1u << 2,
    WIStatus_Installed  = 1u << 3,
    WIStatus_Reachable  = 1u << 4,
} WIStatusFlag;

// 12 bytes, 4-byte aligned — must match C# [StructLayout(Sequential, Size=12)]
typedef struct {
    uint8_t  type;      // WIEventType
    uint8_t  arg;       // direction for SWIPE, 1=watchdog for synthetic PRESS_END, 0 otherwise
    uint8_t  _pad[2];
    float    value;     // unused for discrete events (0.0)
    uint32_t timestamp; // milliseconds since WI_Init
} WI_Event;

// ---------------------------------------------------------------------------
// Singleton state
// ---------------------------------------------------------------------------
@interface WatchInputBridge : NSObject <WCSessionDelegate>
@end

static WatchInputBridge* sBridge = nil;

// Lock — protects ring buffer, crown accumulator, press state
static os_unfair_lock sLock = OS_UNFAIR_LOCK_INIT;

// Ring buffer (guarded by sLock)
static WI_Event sRingBuffer[kRingBufferCapacity];
static int sHead = 0;
static int sCount = 0;
static int sDroppedCount = 0;

// Crown accumulator (guarded by sLock)
static float sCrownAccum = 0.0f;

// Press state (guarded by sLock)
static bool sPressed = false;
static uint64_t sLastHeldMs = 0;

// Init time for relative timestamps
static CFTimeInterval sInitTime = 0;

// Atomic state — no lock needed
static std::atomic<uint32_t> sSessionStatus{0};
static std::atomic<bool>     sDebugLog{false};
static std::atomic<uint64_t> sLastHapticMs{0};

// Crown log throttle (delegate queue only, no lock needed)
static uint64_t sLastCrownLogMs = 0;

// ---------------------------------------------------------------------------
// Helpers (lock must be held by caller where noted)
// ---------------------------------------------------------------------------

static uint32_t currentMs(void) {
    return (uint32_t)((CACurrentMediaTime() - sInitTime) * 1000.0);
}

// Push event into ring buffer — caller MUST hold sLock
static void pushEventLocked(uint8_t type, uint8_t arg, float value) {
    if (sCount == kRingBufferCapacity) {
        // Overflow: drop oldest, advance head
        sHead = (sHead + 1) % kRingBufferCapacity;
        sDroppedCount++;
    } else {
        sCount++;
    }
    int idx = (sHead + sCount - 1) % kRingBufferCapacity;
    WI_Event ev;
    ev.type = type;
    ev.arg = arg;
    ev._pad[0] = 0;
    ev._pad[1] = 0;
    ev.value = value;
    ev.timestamp = currentMs();
    sRingBuffer[idx] = ev;
}

// Lazy press watchdog — caller MUST hold sLock.
// If pressed and no PRESS_HELD arrived within 600 ms, synthesize PRESS_END.
static void checkPressWatchdogLocked(void) {
    if (!sPressed) return;
    uint64_t nowMs = (uint64_t)((CACurrentMediaTime() - sInitTime) * 1000.0);
    if ((nowMs - sLastHeldMs) > kPressWatchdogMs) {
        sPressed = false;
        pushEventLocked(WIEventType_PressEnd, 1, 0.0f); // arg=1: watchdog source
    }
}

// ---------------------------------------------------------------------------
// WCSessionDelegate
// ---------------------------------------------------------------------------
@implementation WatchInputBridge

- (void)session:(WCSession *)session
    activationDidCompleteWithState:(WCSessionActivationState)activationState
                             error:(NSError *)error {
    [self updateStatus:session];
    if (sDebugLog.load()) {
        NSLog(@"[WatchInput] Session activation: state=%ld error=%@",
              (long)activationState, error);
    }
}

- (void)sessionDidBecomeInactive:(WCSession *)session {
    [self updateStatus:session];
    if (sDebugLog.load()) {
        NSLog(@"[WatchInput] Session became inactive");
    }
}

// Reactivate after watch switch (iOS only)
- (void)sessionDidDeactivate:(WCSession *)session {
    [self updateStatus:session];
    if (sDebugLog.load()) {
        NSLog(@"[WatchInput] Session deactivated, reactivating...");
    }
    [session activateSession];
}

- (void)sessionReachabilityDidChange:(WCSession *)session {
    [self updateStatus:session];
    if (sDebugLog.load()) {
        NSLog(@"[WatchInput] Reachability changed: %d", session.isReachable);
    }
}

// Watch sends gesture events WITHOUT replyHandler
- (void)session:(WCSession *)session didReceiveMessageData:(NSData *)messageData {
    [self handleMessageData:messageData replyHandler:nil];
}

// Watch may also send WITH replyHandler (robustness)
- (void)session:(WCSession *)session
    didReceiveMessageData:(NSData *)messageData
             replyHandler:(void (^)(NSData * _Nonnull))replyHandler {
    [self handleMessageData:messageData replyHandler:replyHandler];
}

- (void)handleMessageData:(NSData *)data
             replyHandler:(void (^ _Nullable)(NSData *))replyHandler {
    // Always reply to prevent sender timeout
    if (replyHandler) replyHandler([NSData data]);

    if (data.length < 2) return;

    const uint8_t *bytes = (const uint8_t *)data.bytes;
    if (bytes[0] != kProtocolVersion) return;

    bool debug = sDebugLog.load();
    uint8_t type = bytes[1];

    switch (type) {
        case WIEventType_Tap: {
            os_unfair_lock_lock(&sLock);
            pushEventLocked(WIEventType_Tap, 0, 0.0f);
            os_unfair_lock_unlock(&sLock);
            if (debug) NSLog(@"[WatchInput] Event: TAP");
            break;
        }
        case WIEventType_PressBegin: {
            os_unfair_lock_lock(&sLock);
            sPressed = true;
            sLastHeldMs = (uint64_t)((CACurrentMediaTime() - sInitTime) * 1000.0);
            pushEventLocked(WIEventType_PressBegin, 0, 0.0f);
            os_unfair_lock_unlock(&sLock);
            if (debug) NSLog(@"[WatchInput] Event: PRESS_BEGIN");
            break;
        }
        case WIEventType_PressHeld: {
            os_unfair_lock_lock(&sLock);
            sLastHeldMs = (uint64_t)((CACurrentMediaTime() - sInitTime) * 1000.0);
            pushEventLocked(WIEventType_PressHeld, 0, 0.0f);
            os_unfair_lock_unlock(&sLock);
            if (debug) NSLog(@"[WatchInput] Event: PRESS_HELD");
            break;
        }
        case WIEventType_PressEnd: {
            os_unfair_lock_lock(&sLock);
            sPressed = false;
            pushEventLocked(WIEventType_PressEnd, 0, 0.0f);
            os_unfair_lock_unlock(&sLock);
            if (debug) NSLog(@"[WatchInput] Event: PRESS_END");
            break;
        }
        case WIEventType_Swipe: {
            if (data.length < 3) break;
            uint8_t dir = bytes[2];
            os_unfair_lock_lock(&sLock);
            pushEventLocked(WIEventType_Swipe, dir, 0.0f);
            os_unfair_lock_unlock(&sLock);
            if (debug) NSLog(@"[WatchInput] Event: SWIPE dir=%d", dir);
            break;
        }
        case WIEventType_Crown: {
            if (data.length < 6) break;
            float delta;
            memcpy(&delta, bytes + 2, sizeof(float));
            os_unfair_lock_lock(&sLock);
            sCrownAccum += delta;
            os_unfair_lock_unlock(&sLock);
            // Crown log throttled: at most ~3 lines/sec
            if (debug) {
                uint64_t nowMs = (uint64_t)((CACurrentMediaTime() - sInitTime) * 1000.0);
                if ((nowMs - sLastCrownLogMs) > 300) {
                    NSLog(@"[WatchInput] Crown delta=%.3f accum=%.3f", delta, sCrownAccum);
                    sLastCrownLogMs = nowMs;
                }
            }
            break;
        }
        default:
            if (debug) NSLog(@"[WatchInput] Unknown message type: 0x%02x", type);
            break;
    }
}

- (void)updateStatus:(WCSession *)session {
    uint32_t status = 0;
    if ([WCSession isSupported])                                   status |= WIStatus_Supported;
    if (session.activationState == WCSessionActivationStateActivated) status |= WIStatus_Activated;
    if (session.isPaired)                                          status |= WIStatus_Paired;
    if (session.isWatchAppInstalled)                               status |= WIStatus_Installed;
    if (session.isReachable)                                       status |= WIStatus_Reachable;
    sSessionStatus.store(status);
}

@end

// ---------------------------------------------------------------------------
// C ABI — called from Unity main thread via P/Invoke
// ---------------------------------------------------------------------------
extern "C" {

void WI_Init(void) {
    if (![WCSession isSupported]) {
        NSLog(@"[WatchInput] WCSession is not supported on this device.");
        return;
    }

    sInitTime = CACurrentMediaTime();

    os_unfair_lock_lock(&sLock);
    sHead = 0;
    sCount = 0;
    sDroppedCount = 0;
    sCrownAccum = 0.0f;
    sPressed = false;
    sLastHeldMs = 0;
    os_unfair_lock_unlock(&sLock);

    sSessionStatus.store(WIStatus_Supported);
    sLastHapticMs.store(0);

    if (!sBridge) {
        sBridge = [[WatchInputBridge alloc] init];
    }

    WCSession *session = [WCSession defaultSession];
    session.delegate = sBridge;
    [session activateSession];

    NSLog(@"[WatchInput] Initialized.");
}

uint32_t WI_GetStatus(void) {
    return sSessionStatus.load();
}

int WI_DrainEvents(WI_Event* buf, int capacity) {
    if (!buf || capacity <= 0) return 0;

    os_unfair_lock_lock(&sLock);
    checkPressWatchdogLocked();

    int drained = 0;
    while (sCount > 0 && drained < capacity) {
        buf[drained] = sRingBuffer[sHead];
        sHead = (sHead + 1) % kRingBufferCapacity;
        sCount--;
        drained++;
    }
    os_unfair_lock_unlock(&sLock);
    return drained;
}

float WI_ConsumeCrownDelta(void) {
    os_unfair_lock_lock(&sLock);
    float delta = sCrownAccum;
    sCrownAccum = 0.0f;
    os_unfair_lock_unlock(&sLock);
    return delta;
}

int WI_IsPressed(void) {
    os_unfair_lock_lock(&sLock);
    checkPressWatchdogLocked();
    int result = sPressed ? 1 : 0;
    os_unfair_lock_unlock(&sLock);
    return result;
}

int WI_SendHaptic(int type) {
    if (type < 0 || type > kHapticTypeMax) return 0;

    WCSession *session = [WCSession defaultSession];
    if (!session.isReachable) return 0;

    // Rate limit: 80 ms minimum interval between haptic commands
    uint64_t nowMs = (uint64_t)((CACurrentMediaTime() - sInitTime) * 1000.0);
    uint64_t lastMs = sLastHapticMs.load();
    if ((nowMs - lastMs) < kHapticMinIntervalMs) return 0;
    sLastHapticMs.store(nowMs);

    uint8_t payload[3] = { kProtocolVersion, 0x10, (uint8_t)type };
    NSData *data = [NSData dataWithBytes:payload length:sizeof(payload)];

    [session sendMessageData:data
                replyHandler:^(NSData * _Nonnull replyMessageData) {
                    // Reply received — haptic acknowledged by watch
                }
                errorHandler:^(NSError * _Nonnull error) {
                    if (sDebugLog.load()) {
                        NSLog(@"[WatchInput] Haptic send error: %@", error.localizedDescription);
                    }
                }];

    if (sDebugLog.load()) {
        NSLog(@"[WatchInput] Sent haptic type=%d", type);
    }
    return 1;
}

int WI_GetDroppedCount(void) {
    os_unfair_lock_lock(&sLock);
    int count = sDroppedCount;
    os_unfair_lock_unlock(&sLock);
    return count;
}

void WI_SetDebugLog(int on) {
    sDebugLog.store(on != 0);
    NSLog(@"[WatchInput] Debug logging %s", on ? "enabled" : "disabled");
}

} // extern "C"
