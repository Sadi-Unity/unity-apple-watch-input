import SwiftUI

// Digital Crown continuous rotation tracking with wrap correction and rate-limited sending.
// Unit: ticks (crown range × ticksPerRange). One notch = 1 tick.
class CrownState: ObservableObject {
    @Published var crownValue: Double = 0.0

    var onCrownDelta: ((Float) -> Void)?

    private var prevCrown: Double = 0.0
    private var accumDelta: Double = 0.0
    private var lastSendTime: TimeInterval = 0.0
    private var pendingFlushTask: Task<Void, Never>?

    private let wrapThreshold = InputConstants.crownRange / 2.0
    private let minInterval = 1.0 / InputConstants.crownSendMaxHz

    deinit {
        pendingFlushTask?.cancel()
    }

    func onCrownChanged() {
        let rawDelta = crownValue - prevCrown
        var corrected = rawDelta

        // Wrap correction: if the knob jumped past half-range, it wrapped around
        if corrected > wrapThreshold {
            corrected -= InputConstants.crownRange
        } else if corrected < -wrapThreshold {
            corrected += InputConstants.crownRange
        }

        prevCrown = crownValue
        accumDelta += corrected

        let now = ProcessInfo.processInfo.systemUptime
        let elapsed = now - lastSendTime

        if elapsed >= minInterval {
            pendingFlushTask?.cancel()
            pendingFlushTask = nil
            flushAccumulatedDelta(at: now)
        } else if pendingFlushTask == nil {
            let remainingDelay = minInterval - elapsed
            pendingFlushTask = Task { [weak self] in
                try? await Task.sleep(nanoseconds: UInt64(remainingDelay * 1_000_000_000))
                guard !Task.isCancelled else { return }
                await MainActor.run {
                    guard let self = self else { return }
                    self.pendingFlushTask = nil
                    let flushNow = ProcessInfo.processInfo.systemUptime
                    self.flushAccumulatedDelta(at: flushNow)
                }
            }
        }
    }

    private func flushAccumulatedDelta(at time: TimeInterval) {
        guard accumDelta != 0 else { return }
        let deltaTicks = Float(accumDelta * InputConstants.ticksPerRange)
        onCrownDelta?(deltaTicks)
        accumDelta = 0.0
        lastSendTime = time
    }
}
