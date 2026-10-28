import Foundation

/// A keyframe's depth map as the scan keeps it: 16-bit floats, then zlib (LiDAR plan, §3).
///
/// ARKit hands over 256 × 192 32-bit metres. Half precision keeps a millimetre to beyond ten
/// metres, which is past what the sensor sees, and halves the file before zlib takes the rest of
/// the empty wall. A point ARKit was not confident of is written as zero — unknown — rather than
/// kept as a guess the texturer would trust.
///
/// The half-float conversion is done by hand: `Float16` does not exist on an Intel Mac, and the
/// package's tests run on whatever Mac they are given.
public enum DepthCodec {

    public enum DepthError: Error { case notADepthMap }

    /// Encodes `width × height` metres, row by row.
    public static func encode(_ meters: [Float]) throws -> Data {
        var raw = Data(capacity: meters.count * 2)
        for value in meters {
            let half = halfBits(value.isFinite && value > 0 ? value : 0)
            raw.append(UInt8(half & 0xFF))
            raw.append(UInt8(half >> 8))
        }
        return try (raw as NSData).compressed(using: .zlib) as Data
    }

    /// The metres back, zero where the depth was unknown.
    public static func decode(_ data: Data) throws -> [Float] {
        let raw = try (data as NSData).decompressed(using: .zlib) as Data
        guard raw.count % 2 == 0 else { throw DepthError.notADepthMap }
        return stride(from: 0, to: raw.count, by: 2).map { offset in
            let bits = UInt16(raw[raw.startIndex + offset]) | UInt16(raw[raw.startIndex + offset + 1]) << 8
            return floatFromHalf(bits)
        }
    }

    /// IEEE 754 half precision, rounded to nearest; past the largest half becomes infinity.
    static func halfBits(_ value: Float) -> UInt16 {
        let bits = value.bitPattern
        let sign = UInt16((bits >> 16) & 0x8000)
        let exponent = Int((bits >> 23) & 0xFF) - 127 + 15
        var mantissa = bits & 0x7F_FFFF

        if exponent <= 0 {
            // Too small for a normal half: a subnormal, or zero.
            guard exponent >= -10 else { return sign }
            mantissa |= 0x80_0000
            let shift = UInt32(14 - exponent)
            let rounded = (mantissa + (1 << (shift - 1))) >> shift
            return sign | UInt16(rounded)
        }
        if exponent >= 31 { return sign | 0x7C00 }

        // Round the 23-bit mantissa to 10 bits; a carry rolls into the exponent as it should.
        let rounded = (UInt32(exponent) << 10 | (mantissa >> 13)) + ((mantissa >> 12) & 1)
        return sign | UInt16(min(rounded, 0x7C00))
    }

    static func floatFromHalf(_ bits: UInt16) -> Float {
        let sign: Float = bits & 0x8000 != 0 ? -1 : 1
        let exponent = Int((bits >> 10) & 0x1F)
        let mantissa = Float(bits & 0x3FF)
        switch exponent {
        case 0: return sign * mantissa / 1024 * pow(2, -14)
        case 31: return mantissa == 0 ? sign * .infinity : .nan
        default: return sign * (1 + mantissa / 1024) * pow(2, Float(exponent - 15))
        }
    }
}
