import WatchConnectivity
import WatchKit

// Owns WCSession lifecycle, sends gesture data to phone, receives haptic commands.
// All @Published mutations dispatched to main queue for SwiftUI safety.
class SessionManager: NSObject, WCSessionDelegate, ObservableObject {
    @Published var statusText: String = "Initializing..."

    private var session: WCSession?

    override init() {
        super.init()
        guard WCSession.isSupported() else {
            updateStatus("WCSession not supported")
            return
        }
        let s = WCSession.default
        s.delegate = self
        s.activate()
        session = s
    }

    // Fire-and-forget: drop if counterpart is unreachable
    func send(_ data: Data) {
        guard let s = session, s.isReachable else { return }
        s.sendMessageData(data, replyHandler: nil, errorHandler: nil)
    }

    // MARK: - WCSessionDelegate

    func session(_ session: WCSession,
                 activationDidCompleteWith activationState: WCSessionActivationState,
                 error: Error?) {
        refreshStatus(session)
    }

    func sessionReachabilityDidChange(_ session: WCSession) {
        refreshStatus(session)
    }

    // Haptic command from phone (sent with replyHandler) — must always reply to prevent timeout
    func session(_ session: WCSession,
                 didReceiveMessageData messageData: Data,
                 replyHandler: @escaping (Data) -> Void) {
        replyHandler(Data())
        handleIncoming(messageData)
    }

    // Fallback for messages sent without replyHandler
    func session(_ session: WCSession, didReceiveMessageData messageData: Data) {
        handleIncoming(messageData)
    }

    // MARK: - Private

    private func handleIncoming(_ data: Data) {
        guard data.count >= 3,
              data[0] == InputConstants.protocolVersion,
              data[1] == MessageType.haptic.rawValue,
              let haptic = HapticType(rawValue: data[2]) else { return }

        let wkType = haptic.toWKHapticType()
        DispatchQueue.main.async {
            WKInterfaceDevice.current().play(wkType)
        }
    }

    private func refreshStatus(_ session: WCSession) {
        let text: String
        if session.activationState == .activated && session.isReachable {
            text = "Connected"
        } else if session.activationState == .activated {
            text = "Open the iPhone app"
        } else {
            text = "Connecting..."
        }
        updateStatus(text)
    }

    private func updateStatus(_ text: String) {
        DispatchQueue.main.async { [weak self] in
            self?.statusText = text
        }
    }
}
