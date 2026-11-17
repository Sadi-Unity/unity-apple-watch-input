import SwiftUI

// Main watch UI: touch surface with gesture recognition, crown binding, and connection status.
struct ContentView: View {
    @StateObject private var session = SessionManager()
    @StateObject private var gesture = GestureState()
    @StateObject private var crown = CrownState()
    @FocusState private var isCrownFocused: Bool
    @Environment(\.isLuminanceReduced) private var isLuminanceReduced

    private var statusText: String {
        if isLuminanceReduced {
            return WatchStatusText.wristDown
        } else if session.isActivated && session.isReachable && session.phoneActive {
            return WatchStatusText.connected
        } else {
            return WatchStatusText.openPhoneGame
        }
    }

    var body: some View {
        VStack(spacing: 4) {
            Text(statusText)
                .font(.caption2)
                .foregroundColor(.secondary)

            RoundedRectangle(cornerRadius: 12)
                .fill(Color.blue.opacity(0.15))
                .overlay(
                    Text("Touch Here")
                        .font(.headline)
                        .foregroundColor(.blue)
                )
                .gesture(
                    DragGesture(minimumDistance: 0)
                        .onChanged { gesture.onDragChanged($0) }
                        .onEnded { gesture.onDragEnded($0) }
                )
        }
        .focusable()
        .focused($isCrownFocused)
        .digitalCrownRotation(
            $crown.crownValue,
            from: 0,
            through: InputConstants.crownRange,
            by: 1.0 / WatchInputBuildConfig.ticksPerRange,
            sensitivity: .medium,
            isContinuous: true,
            isHapticFeedbackEnabled: WatchInputBuildConfig.crownHapticsEnabled
        )
        .onChange(of: crown.crownValue) { _ in
            crown.onCrownChanged()
        }
        .onAppear {
            isCrownFocused = true

            gesture.onEvent = { data in
                session.send(data)
            }

            crown.onCrownDelta = { deltaTicks in
                session.send(MessageEncoder.encodeCrown(deltaTicks))
            }
        }
    }
}
