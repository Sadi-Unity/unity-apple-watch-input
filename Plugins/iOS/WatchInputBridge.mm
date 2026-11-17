#import <WatchConnectivity/WatchConnectivity.h>
#import <QuartzCore/QuartzCore.h>
#import <UIKit/UIKit.h>
#include <os/lock.h>
#include <stdint.h>
#include <string.h>
#include <atomic>

// ---------------------------------------------------------------------------
// Protocol constants
// ---------------------------------------------------------------------------
static const uint8_t  kProtocolVersion        = 0x02;
static const int      kRingBufferCapacity     = 64;
static const uint64_t kPressWatchdogMs        = 600;
static const uint64_t kHapticMinIntervalMs    = 80;
static const uint8_t  kHapticTypeMax          = 8;
// Device measurement shows worst-case latency during session warm-up is 493 ms,
// whereas genuinely stale background packets arrive with delays of 24+ seconds.
// Raised from 500 ms to 1000 ms to prevent dropping valid packets during warm-up handshakes.
static const double   kMaxInputAgeMs          = 1000.0;
static const double   kClockSkewThresholdMs   = -200.0;

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

// 2 bytes, 1-byte packed — must match C# [StructLayout(Sequential, Pack = 1)]
typedef struct {
    uint8_t  type;      // WIEventType
    uint8_t  arg;       // direction for SWIPE, 1=watchdog, 2=app resigned for synthetic PRESS_END
} WI_Event;

// ---------------------------------------------------------------------------
// Singleton state
// ---------------------------------------------------------------------------
@interface WatchInputBridge : NSObject <WCSessionDelegate>
- (void)appWillResignActive;
- (void)appDidBecomeActive;
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
static std::atomic<int>      sStaleDroppedCount{0};
static std::atomic<bool>     sAppActive{false};

// Crown log throttle (serialized across WCSession delegate callbacks)
static uint64_t sLastCrownLogMs = 0;

// Latency statistics (serialized across WCSession delegate callbacks; reset during WI_Init)
static uint32_t sAcceptedCount = 0;
static double   sMinAgeMs = 1e9;
static double   sMaxAgeMs = -1e9;
static double   sTotalAgeMs = 0.0;

// ---------------------------------------------------------------------------
// Helpers (lock must be held by caller where noted)
// ---------------------------------------------------------------------------

static uint32_t currentMs(void) {
    return (uint32_t)((CACurrentMediaTime() - sInitTime) * 1000.0);
}

// Push event into ring buffer — caller MUST hold sLock.
// Re-checks sAppActive under lock to discard packets arriving while resigning active.
// force == true is used only for the synthetic PRESS_END queued by appWillResignActive.
static void pushEventLocked(uint8_t type, uint8_t arg, bool force = false) {
    if (!force && !sAppActive.load()) {
        return;
    }
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
    sRingBuffer[idx] = ev;
}

// Lazy press watchdog — caller MUST hold sLock.
// If pressed and no PRESS_HELD arrived within 600 ms, synthesize PRESS_END.
static void checkPressWatchdogLocked(void) {
    if (!sPressed) return;
    uint64_t nowMs = (uint64_t)((CACurrentMediaTime() - sInitTime) * 1000.0);
    if ((nowMs - sLastHeldMs) > kPressWatchdogMs) {
        sPressed = false;
        pushEventLocked(WIEventType_PressEnd, 1); // arg=1: watchdog source
    }
}

// ---------------------------------------------------------------------------
// WCSessionDelegate & Lifecycle
// ---------------------------------------------------------------------------
@implementation WatchInputBridge

- (instancetype)init {
    self = [super init];
    if (self) {
        [[NSNotificationCenter defaultCenter] addObserver:self
                                                 selector:@selector(appWillResignActive)
                                                     name:UIApplicationWillResignActiveNotification
                                                   object:nil];
        [[NSNotificationCenter defaultCenter] addObserver:self
                                                 selector:@selector(appDidBecomeActive)
                                                     name:UIApplicationDidBecomeActiveNotification
                                                   object:nil];
    }
    return self;
}

- (void)dealloc {
    [[NSNotificationCenter defaultCenter] removeObserver:self];
}

- (void)sendApplicationContextPhoneActive:(BOOL)active {
    if (![WCSession isSupported]) return;
    WCSession *session = [WCSession defaultSession];
    if (session.activationState != WCSessionActivationStateActivated) return;
    NSError *error = nil;
    [session updateApplicationContext:@{@"phoneActive": @(active)} error:&error];
    if (error && sDebugLog.load()) {
        NSLog(@"[WatchInput] updateApplicationContext error: %@", error);
    }
}

- (void)appWillResignActive {
    sAppActive.store(false);
    [self sendApplicationContextPhoneActive:NO];
    os_unfair_lock_lock(&sLock);
    bool wasPressed = sPressed;
    sHead = 0;
    sCount = 0;
    sCrownAccum = 0.0f;
    sPressed = false;
    sLastHeldMs = 0;
    if (wasPressed) {
        pushEventLocked(WIEventType_PressEnd, 2, true); // arg = 2: "app resigned"
    }
    os_unfair_lock_unlock(&sLock);
    if (sDebugLog.load()) {
        NSLog(@"[WatchInput] App will resign active — buffer cleared, state reset");
    }
}

- (void)appDidBecomeActive {
    sAppActive.store(true);
    [self sendApplicationContextPhoneActive:YES];
    if (sDebugLog.load()) {
        NSLog(@"[WatchInput] App did become active");
    }
}

- (void)session:(WCSession *)session
    activationDidCompleteWithState:(WCSessionActivationState)activationState
                             error:(NSError *)error {
    [self updateStatus:session];
    if (activationState == WCSessionActivationStateActivated) {
        [self sendApplicationContextPhoneActive:sAppActive.load()];
    }
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

    // Minimum wire packet size: 10 bytes ([version u8][type u8][sentMs f64 LE])
    if (data.length < 10) return;

    const uint8_t *bytes = (const uint8_t *)data.bytes;
    if (bytes[0] != kProtocolVersion) return;

    // Drop incoming packets if app is not active (e.g. background wakeups)
    if (!sAppActive.load()) {
        sStaleDroppedCount++;
        if (sDebugLog.load()) {
            NSLog(@"[WatchInput] Dropped packet (app not active)");
        }
        return;
    }

    // Timestamp verification
    double sentMs;
    memcpy(&sentMs, bytes + 2, sizeof(double));

    double nowMs = [[NSDate date] timeIntervalSince1970] * 1000.0;
    double ageMs = nowMs - sentMs;

    if (ageMs > kMaxInputAgeMs) {
        sStaleDroppedCount++;
        if (sDebugLog.load()) {
            NSLog(@"[WatchInput] Dropped stale packet: age=%.1f ms > %.0f ms", ageMs, kMaxInputAgeMs);
        }
        return;
    }

    bool debug = sDebugLog.load();

    if (ageMs < kClockSkewThresholdMs) {
        if (debug) {
            NSLog(@"[WatchInput] Warning: clock skew suspected: age=%.1f ms < %.0f ms", ageMs, kClockSkewThresholdMs);
        }
    }

    // Latency statistics (throttled: every 50 accepted packets when debug is active)
    sAcceptedCount++;
    if (ageMs < sMinAgeMs) sMinAgeMs = ageMs;
    if (ageMs > sMaxAgeMs) sMaxAgeMs = ageMs;
    sTotalAgeMs += ageMs;

    if (debug && (sAcceptedCount % 50 == 0)) {
        NSLog(@"[WatchInput] Latency stats (last 50 pkts): min=%.1f ms, avg=%.1f ms, max=%.1f ms (accepted=%u)",
              sMinAgeMs, sTotalAgeMs / 50.0, sMaxAgeMs, sAcceptedCount);
        sMinAgeMs = 1e9;
        sMaxAgeMs = -1e9;
        sTotalAgeMs = 0.0;
    }

    uint8_t type = bytes[1];

    switch (type) {
        case WIEventType_Tap: {
            os_unfair_lock_lock(&sLock);
            pushEventLocked(WIEventType_Tap, 0);
            os_unfair_lock_unlock(&sLock);
            if (debug) NSLog(@"[WatchInput] Event: TAP");
            break;
        }
        case WIEventType_PressBegin: {
            os_unfair_lock_lock(&sLock);
            if (!sAppActive.load()) { os_unfair_lock_unlock(&sLock); break; }
            sPressed = true;
            sLastHeldMs = (uint64_t)((CACurrentMediaTime() - sInitTime) * 1000.0);
            pushEventLocked(WIEventType_PressBegin, 0);
            os_unfair_lock_unlock(&sLock);
            if (debug) NSLog(@"[WatchInput] Event: PRESS_BEGIN");
            break;
        }
        case WIEventType_PressHeld: {
            os_unfair_lock_lock(&sLock);
            sLastHeldMs = (uint64_t)((CACurrentMediaTime() - sInitTime) * 1000.0);
            os_unfair_lock_unlock(&sLock);
            if (debug) NSLog(@"[WatchInput] Event: PRESS_HELD");
            break;
        }
        case WIEventType_PressEnd: {
            os_unfair_lock_lock(&sLock);
            sPressed = false;
            pushEventLocked(WIEventType_PressEnd, 0);
            os_unfair_lock_unlock(&sLock);
            if (debug) NSLog(@"[WatchInput] Event: PRESS_END");
            break;
        }
        case WIEventType_Swipe: {
            if (data.length < 11) break;
            uint8_t dir = bytes[10];
            os_unfair_lock_lock(&sLock);
            pushEventLocked(WIEventType_Swipe, dir);
            os_unfair_lock_unlock(&sLock);
            if (debug) NSLog(@"[WatchInput] Event: SWIPE dir=%d", dir);
            break;
        }
        case WIEventType_Crown: {
            if (data.length < 14) break;
            float delta;
            memcpy(&delta, bytes + 10, sizeof(float));
            os_unfair_lock_lock(&sLock);
            if (!sAppActive.load()) { os_unfair_lock_unlock(&sLock); break; }
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
    sStaleDroppedCount.store(0);
    sAcceptedCount = 0;
    sMinAgeMs = 1e9;
    sMaxAgeMs = -1e9;
    sTotalAgeMs = 0.0;

    if (!sBridge) {
        sBridge = [[WatchInputBridge alloc] init];
    }

    UIApplicationState appState = [UIApplication sharedApplication].applicationState;
    sAppActive.store(appState == UIApplicationStateActive);

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

void WI_SetDebugLog(int on) {
    sDebugLog.store(on != 0);
    NSLog(@"[WatchInput] Debug logging %s", on ? "enabled" : "disabled");
}

} // extern "C"
