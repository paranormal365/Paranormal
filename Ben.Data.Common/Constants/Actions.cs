namespace Ben.Data.Common.Constants;

/// <summary>
/// Hierarchically organised string constants that identify every discrete
/// user-facing or system action that can be logged or audited in the application.
/// </summary>
/// <remarks>
/// Constants are grouped into nested static classes that mirror the domain:
/// <see cref="Administrative"/>, <see cref="Communication"/>,
/// <see cref="System"/>, and <see cref="Miscellaneous"/>.
/// These values are intended for use in activity logs, audit trails, and future
/// permission/role systems — they are <em>not</em> the same as the
/// database-level <see cref="Ben.Data.Common.Enums.OrganizationSecurityAction"/> enum.
/// </remarks>
public static class Actions
{
    /// <summary>A record was created.</summary>
    public const string Create = "CREATE";
    /// <summary>A record was read or listed.</summary>
    public const string Read = "READ";
    /// <summary>A record was modified.</summary>
    public const string Update = "UPDATE";
    /// <summary>A record was deleted.</summary>
    public const string Delete = "DELETE";

    /// <summary>Administrative and governance actions performed by privileged users.</summary>
    public static class Administrative
    {
        /// <summary>A record or submission was reviewed.</summary>
        public const string Review = "REVIEW";
        /// <summary>An audit of records or activity was performed.</summary>
        public const string Audit = "AUDIT";
        /// <summary>System or application settings were changed.</summary>
        public const string Configure = "CONFIGURE";

        /// <summary>Identity and credential verification actions.</summary>
        public static class Verify
        {
            public const string Identity = "VERIFY_IDENTITY";
            public const string Address = "VERIFY_ADDRESS";
            public const string Age = "VERIFY_AGE";
            public const string Coordinates = "VERIFY_COORDINATES";
            public const string PaymentMethod = "VERIFY_PAYMENT_METHOD";
            public const string Document = "VERIFY_DOCUMENT";
            public const string BackgroundCheck = "VERIFY_BACKGROUND_CHECK";
            public const string PhoneNumber = "VERIFY_PHONE_NUMBER";
            public const string Email = "VERIFY_EMAIL";
            public const string Link = "VERIFY_LINK";
            public const string SocialMediaAccount = "VERIFY_SOCIAL_MEDIA_ACCOUNT";
            public const string Page = "VERIFY_PAGE_CONTENT";
        }

        /// <summary>Permission and role management actions.</summary>
        public static class Security
        {
            public const string GrantPermission = "GRANT_INDIVIDUAL_PERMISSION";
            public const string RevokePermission = "REVOKE_INDIVIDUAL_PERMISSION";
            public const string CreateRole = "CREATE_ROLE";
            public const string DeleteRole = "DELETE_ROLE";
            public const string UpdateRole = "UPDATE_ROLE";
            public const string ModifyRolePermissions = "MODIFY_ROLE_PERMISSIONS";
        }

        /// <summary>Organisation ownership assignment actions.</summary>
        public static class Organization
        {
            public const string AssignOrganizationOwnership = "ASSIGN_PRIMARY_OWNERSHIP";
            public const string AssignOrganizationChildOwnership = "ASSIGN_CHILD_OWNERSHIP";
        }

        /// <summary>General approval workflow actions.</summary>
        public static class General
        {
            public const string Approve = "APPROVE";
            public const string Reject = "REJECT";
            public const string Escalate = "ESCALATE";
            public const string Deescalate = "DEESCALATE";
        }

        /// <summary>Help-ticket lifecycle actions.</summary>
        public static class HelpTicket
        {
            public const string Assign = "ASSIGN_HELP_TICKET";
            public const string Transfer = "TRANSFER_HELP_TICKET";
            public const string Unassign = "UNASSIGN_HELP_TICKET";
            public const string Reassign = "REASSIGN_HELP_TICKET";
            public const string Delete = "DELETE_HELP_TICKET";
            public const string AddComment = "ADD_COMMENT_TO_HELP_TICKET";
            public const string AddOthersToTicket = "ADD_ADDITIONAconst L_PEOPLE_TO_HELP_TICKET";
            public const string RemoveOthersFromTicket = "REMOVE_ADDITIONAL_PEOPLE_FROM_HELP_TICKET";
            public const string AssignToSelf = "ASSIGN_HELP_TICKET_TO_SELF";
            public const string AssignToDepartment = "ASSIGN_HELP_TICKET_TO_DEPARTMENT";
            public const string ChangeStatus = "CHANGE_HELP_TICKET_STATUS";
            public const string ChangePriority = "CHANGE_HELP_TICKET_PRIORITY";
            public const string Close = "CLOSE_HELP_TICKET";
        }

        /// <summary>User account management actions performed by an administrator.</summary>
        public static class User
        {
            public const string BlockUser = "BLOCK_USER";
            public const string UnblockUser = "UNBLOCK_USER";
            public const string ResetPassword = "ADMIN_RESET_PASSWORD";
            public const string UnlockAccount = "UNLOCK_ACCOUNT";
            public const string LockAccount = "LOCK_ACCOUNT";
        }
    }

    /// <summary>Messaging and notification channel actions.</summary>
    public static class Communication
    {
        public const string SendEmail = "EMAIL_SEND";
        public const string SendSMS = "SMS_SEND";
        public const string SendExternalPushNotification = "EXTERNAL_PUSH_NOTIFICATION_SEND";
        public const string ReceiveEmail = "EMAIL_RECEIVE";
        public const string ReceiveSMS = "SMS_RECEIVE";
        public const string ReceiveExternalPushNotification = "EXTERNAL_PUSH_NOTIFICATION_RECEIVE_REQUEST";

        /// <summary>In-application messaging channel actions.</summary>
        public static class Internal
        {
            public const string SendPushNotification = "INTERNAL_PUSH_NOTIFICATION_SEND";
            public const string ReceivePushNotification = "INTERNAL_PUSH_NOTIFICATION_RECEIVE";
            public const string SendInAppMessage = "IN_APP_MESSAGE_SEND";
            public const string ReceiveInAppMessage = "IN_APP_MESSAGE_RECEIVE";
            public const string ReplyToInAppMessage = "IN_APP_MESSAGE_REPLY";
        }
    }

    /// <summary>Background process and platform-generated events.</summary>
    public static class System
    {
        public const string StartProcess = "PROCESS_START";
        public const string EndProcess = "PROCESS_END";
        public const string Error = "ERROR_BY_SYSTEM";
        public const string Notification = "NOTIFICATION_BY_SYSTEM";
    }

    /// <summary>Cross-cutting operations that do not belong to a specific domain.</summary>
    public static class Miscellaneous
    {
        public const string Export = "EXPORT";
        public const string Import = "IMPORT";
        public const string Archive = "ARCHIVE";
        public const string Restore = "RESTORE";
        public const string Login = "LOGIN";
        public const string Logout = "LOGOUT";
        public const string Proxy = "PROXY";
        public const string Duplicate = "DUPLICATE";
        public const string ErrorUser = "ERROR_BY_USER";
        public const string Upload = "UPLOAD";
    }
}
