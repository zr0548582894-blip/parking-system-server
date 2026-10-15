using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using ParkingSystem.Core.Entities;
using ParkingSystem.Core.Interfaces.Security;
using ParkingSystem.Core.Options;

namespace ParkingSystem.Service.Security;

// יוצר JWT חתום עם claims של המשתמש (מזהה, אימייל ו-role). השרת מאמת את החתימה בכל בקשה,
// ולכן אי אפשר לזייף token. מפתח החתימה מגיע מהקונפיגורציה בלבד
public class JwtTokenService : ITokenService
{
    // HMAC-SHA256 דורש מפתח של לפחות 256 ביט
    private const int MinimumKeyBytes = 32;

    private readonly JwtOptions _options;
    private readonly TimeProvider _timeProvider;

    public JwtTokenService(IOptions<JwtOptions> options, TimeProvider timeProvider)
    {
        _options = options.Value;
        _timeProvider = timeProvider;
    }

    public GeneratedToken CreateToken(User user)
    {
        var keyBytes = Encoding.UTF8.GetBytes(_options.SecretKey);
        if (keyBytes.Length < MinimumKeyBytes)
        {
            // תקלת קונפיגורציה, לא טעות של משתמש. ההודעה לא כוללת את המפתח עצמו
            throw new InvalidOperationException(
                $"Jwt:SecretKey is missing or shorter than {MinimumKeyBytes} bytes.");
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var expiresAtUtc = now.AddMinutes(_options.ExpiryMinutes);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            // "role" הוא השם הקצר בתקן JWT, ו-ASP.NET ממפה אותו אוטומטית ל-role בעת האימות
            new Claim("role", user.Role.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var credentials = new SigningCredentials(new SymmetricSecurityKey(keyBytes), SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now,
            expires: expiresAtUtc,
            signingCredentials: credentials);

        return new GeneratedToken(new JwtSecurityTokenHandler().WriteToken(token), expiresAtUtc);
    }
}
