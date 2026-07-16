# Ben.Data.Common — Constants

All constants classes are in the `Ben.Data.Common.Constants` namespace.

---

## `RoleNames`

**File:** [`Ben.Data.Common/Constants/RoleNames.cs`](../../../Ben.Data.Common/Constants/RoleNames.cs)

### Summary
Strongly-typed constants for ASP.NET Core Identity role names.  
Referenced everywhere a role name is needed so a future rename requires only a single change.

Available via `global using Ben.Data.Common.Constants;` in `Ben.Data.WebApi` and `Ben.Service.RepositoryService`.

### Constants

| Name | Value | Used in |
|---|---|---|
| `SuperAdmin` | `"SuperAdmin"` | `[Authorize(Roles = RoleNames.SuperAdmin)]` attributes; `UserManager.IsInRoleAsync`; Serilog Properties; `SuperAdminSeeder` |

---

## `AppSources`

**File:** [`Ben.Data.Common/Constants/AppSources.cs`](../../../Ben.Data.Common/Constants/AppSources.cs)

### Summary
Identifies which application tier produced a log or audit entry.  
Written to the `Source` column on `AuditLog` rows and enriched as a Serilog property so every error in the `Logs` table is tagged with its origin.

### Constants

| Name | Value | Description |
|---|---|---|
| `WebApi` | `"WebApi"` | Operations originating from `Ben.Data.WebApi`. |
| `WebApp` | `"WebApp"` | Operations originating from `Ben.Web.WebApp`. |

**Usage example (controller):**
```csharp
await _auditLog.LogCreateAsync(typeof(TEntity).Name, id, entity, userId, AppSources.WebApi);
```

---

## `Actions`

**File:** [`Ben.Data.Common/Constants/Actions.cs`](../../../Ben.Data.Common/Constants/Actions.cs)

### Summary
Hierarchically organised string constants that identify every discrete user-facing or system action that can appear in an activity log or audit trail.  
These are **not** the same as [`OrganizationSecurityAction`](Enums.md#organizationsecurityaction) — they are intended for higher-level event tracking.

### Top-level constants

| Name | Value |
|---|---|
| `Create` | `"CREATE"` |
| `Read` | `"READ"` |
| `Update` | `"UPDATE"` |
| `Delete` | `"DELETE"` |

### `Administrative` nested class

General administrative governance actions.

| Category | Constants |
|---|---|
| Root | `Review`, `Audit`, `Configure` |
| `Verify` | `Identity`, `Address`, `Age`, `Coordinates`, `PaymentMethod`, `Document`, `BackgroundCheck`, `PhoneNumber`, `Email`, `Link`, `SocialMediaAccount`, `Page` |
| `Security` | `GrantPermission`, `RevokePermission`, `CreateRole`, `DeleteRole`, `UpdateRole`, `ModifyRolePermissions` |
| `Organization` | `AssignOrganizationOwnership`, `AssignOrganizationChildOwnership` |
| `General` | `Approve`, `Reject`, `Escalate`, `Deescalate` |
| `HelpTicket` | `Assign`, `Transfer`, `Unassign`, `Reassign`, `Delete`, `AddComment`, `AddOthersToTicket`, `RemoveOthersFromTicket`, `AssignToSelf`, `AssignToDepartment`, `ChangeStatus`, `ChangePriority`, `Close` |
| `User` | `BlockUser`, `UnblockUser`, `ResetPassword`, `UnlockAccount`, `LockAccount` |

### `Communication` nested class

Messaging and notification channel actions.

| Category | Constants |
|---|---|
| Root | `SendEmail`, `SendSMS`, `SendExternalPushNotification`, `ReceiveEmail`, `ReceiveSMS`, `ReceiveExternalPushNotification` |
| `Internal` | `SendPushNotification`, `ReceivePushNotification`, `SendInAppMessage`, `ReceiveInAppMessage`, `ReplyToInAppMessage` |

### `System` nested class

Background process and platform-generated events: `StartProcess`, `EndProcess`, `Error`, `Notification`.

### `Miscellaneous` nested class

Cross-cutting operations: `Export`, `Import`, `Archive`, `Restore`, `Login`, `Logout`, `Proxy`, `Duplicate`, `ErrorUser`, `Upload`.
