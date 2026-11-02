using Nexa.Xsr;

namespace Nexa.Services.Accounts;

/// <summary>
/// Stable error contracts of the account capability. Codes are semantic identifiers and never
/// change meaning.
/// </summary>
public static class AccountErrors
{
    public static readonly XsrSemanticId InvalidProfileCode = XsrSemanticId.Parse("accounts.invalid_profile");
    public static readonly XsrSemanticId ProfileNotFoundCode = XsrSemanticId.Parse("accounts.profile_not_found");
    public static readonly XsrSemanticId PersistFailedCode = XsrSemanticId.Parse("accounts.persist_failed");
    public static readonly XsrSemanticId LaunchNotSupportedCode = XsrSemanticId.Parse("accounts.launch_not_supported");

    public static XsrError InvalidProfile(string reason) =>
        new(XsrErrorKind.Rejected, InvalidProfileCode, $"The launch profile was rejected: {reason}");

    public static XsrError ProfileNotFound(int index) =>
        new(XsrErrorKind.NotFound, ProfileNotFoundCode, $"No launch profile exists at index {index}.");

    public static XsrError PersistFailed(string reason) =>
        new(XsrErrorKind.Unavailable, PersistFailedCode, $"The launch profile store could not be written: {reason}");
    public static XsrError LaunchNotSupported(LaunchProfileKind kind, string reason) =>
        new(XsrErrorKind.Rejected, LaunchNotSupportedCode, $"The {kind} account cannot launch yet: {reason}");
}
