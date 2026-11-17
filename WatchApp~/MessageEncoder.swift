import Foundation

// Binary message serialization for the wire protocol (v2).
// Each input message: [version u8][type u8][sentMs f64 LE][payload...], little-endian.
// sentMs = Date().timeIntervalSince1970 * 1000.0
struct MessageEncoder {
    private static func createHeader(type: MessageType) -> Data {
        var data = Data(capacity: 16)
        data.append(InputConstants.protocolVersion)
        data.append(type.rawValue)
        var sentMs = Date().timeIntervalSince1970 * 1000.0
        Swift.withUnsafeBytes(of: &sentMs) { data.append(contentsOf: $0) }
        return data
    }

    static func encodeTap() -> Data {
        createHeader(type: .tap)
    }

    static func encodePressBegin() -> Data {
        createHeader(type: .pressBegin)
    }

    static func encodePressHeld() -> Data {
        createHeader(type: .pressHeld)
    }

    static func encodePressEnd() -> Data {
        createHeader(type: .pressEnd)
    }

    static func encodeSwipe(_ direction: SwipeDirection) -> Data {
        var data = createHeader(type: .swipe)
        data.append(direction.rawValue)
        return data
    }

    static func encodeCrown(_ deltaTicks: Float) -> Data {
        var data = createHeader(type: .crown)
        var ticks = deltaTicks
        Swift.withUnsafeBytes(of: &ticks) { data.append(contentsOf: $0) }
        return data
    }
}
