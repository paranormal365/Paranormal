import Testing
@testable import BenKit

/// Readings never print "-0" (10/17/2026: the live meter showed "-0 mG" on an iPad).
@Suite struct ReadingTextTests {
    @Test(arguments: [-0.4, -0.0, 0.0, 0.3, -0.49, 0.49])
    func anything_that_rounds_to_zero_reads_zero_with_no_sign(_ value: Double) {
        #expect(ReadingText.signedWhole(value) == "0")
        #expect(ReadingText.signedWhole(value, unit: "mG") == "0 mG")
        #expect(ReadingText.fromBase(value, unit: "mG") == "at base")
    }

    @Test func positive_and_negative_readings_keep_their_sign() {
        #expect(ReadingText.signedWhole(20) == "+20")
        #expect(ReadingText.signedWhole(0.6) == "+1")
        #expect(ReadingText.signedWhole(-0.6) == "-1")
        #expect(ReadingText.signedWhole(-12.4, unit: "mG") == "-12 mG")
    }

    @Test(arguments: [-0.4, 0.3])
    func a_sound_level_near_zero_reads_zero(_ value: Double) {
        #expect(ReadingText.whole(value) == "0")
    }

    @Test func a_sound_level_keeps_its_minus() {
        #expect(ReadingText.whole(-51.2) == "-51")
    }

    @Test func from_base_says_which_way_in_words() {
        #expect(ReadingText.fromBase(3.2, unit: "mG") == "3 mG above base")
        #expect(ReadingText.fromBase(-3.2, unit: "mG") == "3 mG below base")
    }

    /// The bug itself, so this file keeps saying why it exists.
    @Test func the_old_format_does_print_minus_zero() {
        #expect(String(format: "%+.0f", -0.4) == "-0")
    }
}
