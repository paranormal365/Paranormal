namespace Ben.Data.Common.Enums;

/// <summary>
/// Which of Apple's two push services a device token belongs to (item 252).
/// </summary>
/// <remarks>
/// A token is minted by one and refused by the other: a build run from Xcode registers with the
/// sandbox, TestFlight and the App Store with production. The app says which when it registers,
/// because only the build knows.
/// </remarks>
public enum PushEnvironment
{
    Production = 0,
    Sandbox = 1,
}
