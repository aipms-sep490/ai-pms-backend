namespace AIPMS.Application.Abstractions.Security;

public sealed record SignedPreviewClaims(
    string ResourceKind,
    long ResourceId,
    long ActorId,
    long? AssignmentId,
    DateTime ExpiresAtUtc);

public sealed record SignedPreviewTokenDto(
    string Token,
    DateTime ExpiresAtUtc,
    string StreamUrl,
    string SafePreviewUrl,
    string ContentType,
    string FileName,
    string ScanStatus);

public interface ISignedPreviewTokenService
{
    string Issue(SignedPreviewClaims claims);
    SignedPreviewClaims? Validate(string token, string expectedKind, long expectedResourceId, long expectedActorId);
}
