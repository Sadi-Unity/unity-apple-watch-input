import SwiftUI

// DragGesture(minimumDistance: 0) state machine for tap / press / swipe recognition.
// States: IDLE → TOUCHING → PRESSING or CONSUMED → IDLE.
// Every Task is cancelled on every exit path.
class GestureState: ObservableObject {
    enum Phase {
        case idle
        case touching
        case pressing
        case consumed
    }

    var onEvent: ((Data) -> Void)?

    private var phase: Phase = .idle
    private var touchStart: Date = .distantPast
    private var pressTask: Task<Void, Never>?
    private var heldTask: Task<Void, Never>?

    deinit {
        pressTask?.cancel()
        heldTask?.cancel()
    }

    func onDragChanged(_ value: DragGesture.Value) {
        let dist = hypot(value.translation.width, value.translation.height)

        switch phase {
        case .idle:
            phase = .touching
            touchStart = Date()
            startPressTimer()

        case .touching:
            if dist >= InputConstants.swipeMinDistance {
                cancelPressTimer()
                let dir = dominantDirection(value.translation)
                onEvent?(MessageEncoder.encodeSwipe(dir))
                phase = .consumed
            } else if dist >= InputConstants.tapMaxDistance {
                // Too far for tap or press, but not far enough for swipe yet
                cancelPressTimer()
            }

        case .pressing:
            // Movement ignored during press — only onEnded exits
            break

        case .consumed:
            // Everything ignored until finger lifts
            break
        }
    }

    func onDragEnded(_ value: DragGesture.Value) {
        let dist = hypot(value.translation.width, value.translation.height)

        switch phase {
        case .touching:
            cancelPressTimer()
            let elapsed = Date().timeIntervalSince(touchStart)
            if elapsed < InputConstants.tapMaxDuration && dist < InputConstants.tapMaxDistance {
                onEvent?(MessageEncoder.encodeTap())
            }
            // Otherwise: gesture rejected, no event

        case .pressing:
            cancelHeldTimer()
            onEvent?(MessageEncoder.encodePressEnd())

        case .consumed:
            break

        case .idle:
            break
        }
        phase = .idle
    }

    // MARK: - Timers (Task + Task.sleep, cancelled on every exit path)

    private func startPressTimer() {
        pressTask = Task { [weak self] in
            try? await Task.sleep(nanoseconds: UInt64(InputConstants.tapMaxDuration * 1_000_000_000))
            guard !Task.isCancelled else { return }
            await MainActor.run {
                guard let self = self, self.phase == .touching else { return }
                self.phase = .pressing
                self.onEvent?(MessageEncoder.encodePressBegin())
                self.startHeldTimer()
            }
        }
    }

    private func cancelPressTimer() {
        pressTask?.cancel()
        pressTask = nil
    }

    private func startHeldTimer() {
        heldTask = Task { [weak self] in
            while !Task.isCancelled {
                try? await Task.sleep(nanoseconds: UInt64(InputConstants.pressHeldInterval * 1_000_000_000))
                guard !Task.isCancelled else { return }
                await MainActor.run {
                    guard let self = self, self.phase == .pressing else { return }
                    self.onEvent?(MessageEncoder.encodePressHeld())
                }
            }
        }
    }

    private func cancelHeldTimer() {
        heldTask?.cancel()
        heldTask = nil
    }

    // MARK: - Geometry

    private func dominantDirection(_ translation: CGSize) -> SwipeDirection {
        if abs(translation.width) > abs(translation.height) {
            return translation.width > 0 ? .right : .left
        } else {
            return translation.height > 0 ? .down : .up
        }
    }
}
