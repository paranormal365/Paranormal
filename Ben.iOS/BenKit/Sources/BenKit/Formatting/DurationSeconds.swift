import Foundation

public extension Duration {
    /// This duration as plain seconds.
    ///
    /// `Duration` holds seconds and attoseconds separately, and every piece of code that measures
    /// elapsed time against a `Date` or a `TimeInterval` has to put them back together. Written
    /// once here so a dropped `/ 1e18` cannot be introduced in one place and not another.
    var inSeconds: Double {
        Double(components.seconds) + Double(components.attoseconds) / 1e18
    }
}
