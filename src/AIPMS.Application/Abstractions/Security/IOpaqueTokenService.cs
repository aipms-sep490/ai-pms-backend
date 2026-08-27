namespace AIPMS.Application.Abstractions.Security;

public interface IOpaqueTokenService
{
    OpaqueToken Generate();

    byte[] Hash(string token);
}

public sealed record OpaqueToken(string Value, byte[] Hash);
