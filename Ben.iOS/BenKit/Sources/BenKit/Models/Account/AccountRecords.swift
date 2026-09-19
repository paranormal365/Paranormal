import Foundation

// Ports of Ben.Data.WebApi/Controllers/AccountRegistrationController.cs,
// MyTwoFactorController.cs and MyPasswordController.cs — everything a person needs to get
// an account and look after it.

/// A sign-up. The handle is normalised and re-checked server-side whatever the client did.
public struct RegisterRequest: Encodable, Sendable {
    public var email: String
    public var password: String
    public var displayName: String
    public var handle: String
    public var firstName: String?
    public var lastName: String?

    public init(email: String, password: String, displayName: String, handle: String,
                firstName: String? = nil, lastName: String? = nil) {
        self.email = email
        self.password = password
        self.displayName = displayName
        self.handle = handle
        self.firstName = firstName
        self.lastName = lastName
    }
}

/// The result of a sign-up. `field` names the input to point at, or nil for a general message.
public struct RegisterResponse: Decodable, Sendable, Equatable {
    public var succeeded: Bool
    public var message: String
    public var field: String?
}

/// Whether a handle can be taken, and why not when it can't.
public struct HandleAvailability: Decodable, Sendable, Equatable {
    public var handle: String
    public var available: Bool
    public var reason: String?
}

public struct ConfirmEmailResponse: Decodable, Sendable, Equatable {
    public var succeeded: Bool
    public var message: String
}

/// Where two-step sign-in stands for this account.
public struct TwoFactorStatus: Decodable, Sendable, Equatable {
    public var enabled: Bool
    public var hasAuthenticatorKey: Bool
    public var recoveryCodesRemaining: Int
}

/// What an authenticator app needs: the key to type, or the URI to scan.
public struct TwoFactorSetup: Decodable, Sendable, Equatable {
    public var sharedKey: String
    public var authenticatorUri: String
}

/// The recovery codes handed over ONCE when two-step is switched on.
public struct TwoFactorEnabled: Decodable, Sendable, Equatable {
    public var recoveryCodes: [String]
}
