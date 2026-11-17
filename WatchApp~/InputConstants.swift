import Foundation
import WatchKit

// Centralized gesture recognition thresholds and protocol constants.
// Collected here for future migration to a user-facing settings asset.
enum InputConstants {
    static let tapMaxDuration: TimeInterval = 0.3
    static let tapMaxDistance: CGFloat = 10.0
    static let swipeMinDistance: CGFloat = 20.0
    static let pressHeldInterval: TimeInterval = 0.25

    static let crownRange: Double = 1.0
    static let crownSendMaxHz: Double = 30.0

    static let protocolVersion: UInt8 = 0x02
}

// Wire protocol message types
enum MessageType: UInt8 {
    case tap        = 0x01
    case pressBegin = 0x02
    case pressHeld  = 0x03
    case pressEnd   = 0x04
    case swipe      = 0x05
    case crown      = 0x06
    case haptic     = 0x10 // phone -> watch
}

enum SwipeDirection: UInt8 {
    case up    = 0
    case down  = 1
    case left  = 2
    case right = 3
}

// Own enum with explicit switch mapping — do not rely on WKHapticType.rawValue
enum HapticType: UInt8 {
    case notification  = 0
    case directionUp   = 1
    case directionDown = 2
    case success       = 3
    case failure       = 4
    case retry         = 5
    case start         = 6
    case stop          = 7
    case click         = 8

    func toWKHapticType() -> WKHapticType {
        switch self {
        case .notification:  return .notification
        case .directionUp:   return .directionUp
        case .directionDown: return .directionDown
        case .success:       return .success
        case .failure:       return .failure
        case .retry:         return .retry
        case .start:         return .start
        case .stop:          return .stop
        case .click:         return .click
        }
    }
}
