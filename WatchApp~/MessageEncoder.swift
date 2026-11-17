import Foundation

// Binary message serialization for the wire protocol.
// Each message: [version u8][type u8][payload...], little-endian.
struct MessageEncoder {
    static func encodeTap() -> Data {
        Data([InputConstants.protocolVersion, MessageType.tap.rawValue])
    }

    static func encodePressBegin() -> Data {
        Data([InputConstants.protocolVersion, MessageType.pressBegin.rawValue])
    }

    static func encodePressHeld() -> Data {
        Data([InputConstants.protocolVersion, MessageType.pressHeld.rawValue])
    }

    static func encodePressEnd() -> Data {
        Data([InputConstants.protocolVersion, MessageType.pressEnd.rawValue])
    }

    static func encodeSwipe(_ direction: SwipeDirection) -> Data {
        Data([InputConstants.protocolVersion, MessageType.swipe.rawValue, direction.rawValue])
    }

    static func encodeCrown(_ deltaTicks: Float) -> Data {
        var ticks = deltaTicks
        var data = Data([InputConstants.protocolVersion, MessageType.crown.rawValue])
        Swift.withUnsafeBytes(of: &ticks) { data.append(contentsOf: $0) }
        return data
    }
}
