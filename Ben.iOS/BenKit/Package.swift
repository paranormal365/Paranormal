// swift-tools-version: 6.0
import PackageDescription

let package = Package(
    name: "BenKit",
    platforms: [
        .iOS(.v18),
        // macOS so `swift test` runs on the Mac host without a simulator.
        .macOS(.v14),
    ],
    products: [
        .library(name: "BenKit", targets: ["BenKit"]),
    ],
    targets: [
        .target(name: "BenKit"),
        .target(name: "BenKitTestSupport", dependencies: ["BenKit"]),
        .testTarget(
            name: "BenKitTests",
            dependencies: ["BenKit", "BenKitTestSupport"],
            resources: [.copy("Fixtures")]
        ),
    ]
)
