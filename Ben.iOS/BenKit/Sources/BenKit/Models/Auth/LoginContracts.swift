import Foundation

/// `POST /login`. The 2FA fields MUST be omitted entirely when nil —
/// Identity treats an empty string as a wrong code and burns a failure
/// (see `WebApiAuthContracts.cs`). `encodeIfPresent` guarantees the omission.
public struct LoginRequest: Sendable, Encodable {
    public var email: String
    public var password: String
    public var twoFactorCode: String?
    public var twoFactorRecoveryCode: String?

    public init(
        email: String,
        password: String,
        twoFactorCode: String? = nil,
        twoFactorRecoveryCode: String? = nil
    ) {
        self.email = email
        self.password = password
        self.twoFactorCode = twoFactorCode
        self.twoFactorRecoveryCode = twoFactorRecoveryCode
    }

    enum CodingKeys: String, CodingKey {
        case email, password, twoFactorCode, twoFactorRecoveryCode
    }

    public func encode(to encoder: Encoder) throws {
        var container = encoder.container(keyedBy: CodingKeys.self)
        try container.encode(email, forKey: .email)
        try container.encode(password, forKey: .password)
        try container.encodeIfPresent(twoFactorCode, forKey: .twoFactorCode)
        try container.encodeIfPresent(twoFactorRecoveryCode, forKey: .twoFactorRecoveryCode)
    }
}

/// The ProblemDetails body Identity returns on 401 — `detail` is how
/// "RequiresTwoFactor" is distinguished from a plain bad password.
public struct ProblemDetailsBody: Sendable, Decodable {
    public var title: String?
    public var detail: String?
    public var status: Int?

    public var requiresTwoFactor: Bool { detail == "RequiresTwoFactor" }
}

/// ValidationProblemDetails (`register`, `forgotPassword`, `resetPassword`):
/// field-keyed error lists worth surfacing per input, not discarding.
public struct ValidationProblemBody: Sendable, Decodable {
    public var title: String?
    public var errors: [String: [String]]?

    public var flattened: [String] {
        (errors ?? [:]).values.flatMap(\.self)
    }
}
