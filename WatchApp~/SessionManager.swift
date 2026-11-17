import WatchConnectivity
import WatchKit

// Owns WCSession lifecycle, sends gesture data to phone, receives haptic commands, tracks phone application status.
// All @Published mutations dispatched to main queue for SwiftUI safety.
class SessionManager: NSObject, WCSessionDelegate, ObservableObject {
    @Published var isActivated: Bool = false
    @Published var isReachable: Bool = false
    @Published var phoneActive: Bool = false

    private var session: WCSession?

    override init() {
        super.init()
        guard WCSession.isSupported() else { return }
        let s = WCSession.default
        s.delegate = self
        s.activate()
        session = s

        if let context = s.receivedApplicationContext as? [String: Any],
           let active = context["phoneActive"] as? Bool {
            self.phoneActive = active
        }
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
        DispatchQueue.main.async { [weak self] in
            guard let self = self else { return }
            self.isActivated = (activationState == .activated)
            self.isReachable = session.isReachable
            if let active = session.receivedApplicationContext["phoneActive"] as? Bool {
                self.phoneActive = active
            }
        }
    }

    func sessionReachabilityDidChange(_ session: WCSession) {
        DispatchQueue.main.async { [weak self] in
            self?.isReachable = session.isReachable
        }
    }

    func session(_ session: WCSession, didReceiveApplicationContext applicationContext: [String : Any]) {
        if let active = applicationContext["phoneActive"] as? Bool {
            DispatchQueue.main.async { [weak self] in
                self?.phoneActive = active
            }
        }
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
}
