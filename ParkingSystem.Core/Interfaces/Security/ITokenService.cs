using ParkingSystem.Core.Entities;

namespace ParkingSystem.Core.Interfaces.Security;

public interface ITokenService
{
    GeneratedToken CreateToken(User user);
}

// ה-token עצמו ומועד תפוגתו, כדי שה-Service לא יצטרך לחשב אותו שוב
public record GeneratedToken(string Value, DateTime ExpiresAtUtc);
