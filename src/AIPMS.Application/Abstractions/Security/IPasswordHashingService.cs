namespace AIPMS.Application.Abstractions.Security;

public interface IPasswordHashingService
{
    string Hash(string password);

    bool Verify(string passwordHash, string providedPassword);
}
