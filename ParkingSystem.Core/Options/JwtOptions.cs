namespace ParkingSystem.Core.Options;

public class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;

    // סוד: לא נשמר ב-repo. בפיתוח מגיע מ-User Secrets
    public string SecretKey { get; set; } = string.Empty;

    public int ExpiryMinutes { get; set; } = 60;
}
