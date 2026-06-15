namespace NetAccel.Managed.Api;

/// <summary>
/// Stable error codes from contracts/error-codes.json.
/// Client state logic depends on these, not on display text.
/// </summary>
public static class ManagedErrorCode
{
    public const string ClientAuthSessionRevoked = "client_auth_session_revoked";
    public const string ClientAccountInactive = "client_account_inactive";
    public const string ClientInstanceLimitReached = "client_instance_limit_reached";
    public const string ClientInstanceRebindRequired = "client_instance_rebind_required";
    public const string ClientInstanceRevoked = "client_instance_revoked";
    public const string InvalidCredentials = "invalid_credentials";
    public const string RefreshTokenInvalid = "refresh_token_invalid";
    public const string RefreshTokenRequired = "refresh_token_required";
    public const string TokenIssueFailed = "token_issue_failed";
    public const string InstanceCredentialInvalid = "instance_credential_invalid";
    public const string InstanceCredentialRequired = "instance_credential_required";
    public const string InstanceCredentialScopeDenied = "instance_credential_scope_denied";
    public const string InstanceCredentialInstanceMismatch = "instance_credential_instance_mismatch";
    public const string ManagedIdentityMismatch = "managed_identity_mismatch";
    public const string ManagedAuthIdentityMismatch = "managed_auth_identity_mismatch";
    public const string ManagedScopeDenied = "managed_scope_denied";
    public const string ManagedConfigNotModified = "managed_config_not_modified";
    public const string ManagedConfigRevisionStale = "managed_config_revision_stale";
    public const string ManagedConfigAckInvalid = "managed_config_ack_invalid";
    public const string ManagedSelectionInvalid = "managed_selection_invalid";
    public const string ManagedSelectionRevisionConflict = "managed_selection_revision_conflict";
    public const string ManagedSelectionPlanNotAssigned = "managed_selection_plan_not_assigned";
    public const string ManagedSelectionPlanUnavailable = "managed_selection_plan_unavailable";
    public const string ManagedSessionPlanNotEffective = "managed_session_plan_not_effective";
    public const string ManagedKeyRevoked = "managed_key_revoked";
    public const string ManagedEmergencyStop = "managed_emergency_stop";
    public const string MandatoryUpdateRequired = "mandatory_update_required";
    public const string ManagedRevisionConflict = "managed_revision_conflict";
    public const string ManagedProfileNotAssigned = "managed_profile_not_assigned";
    public const string ManagedProfileMaintenance = "managed_profile_maintenance";
    public const string ManagedProfileCapabilityIncompatible = "managed_profile_capability_incompatible";
}
