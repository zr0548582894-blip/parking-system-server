using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using ParkingSystem.Core.Entities;
using ParkingSystem.Core.Enums;
using ParkingSystem.Core.Options;
using ParkingSystem.Service.Security;
using ParkingSystem.Tests.Helpers;

namespace ParkingSystem.Tests.Security;

public class JwtTokenServiceTests
{
    // מפתח דמה לבדיקות בלבד: אינו המפתח של המערכת, שמגיע מ-User Secrets
    private const string TestKey = "unit-test-only-key-0123456789-abcdefghij";
    private const string Issuer = "test-issuer";
    private const string Audience = "test-audience";

    private static readonly User TestUser = new()
    {
        Id = 7,
        Email = "dana@example.com",
        FullName = "Dana Levi",
        Role = UserRole.Admin
    };

    private static JwtTokenService CreateService(string secretKey = TestKey, int expiryMinutes = 30, TimeProvider? clock = null) =>
        new(Options.Create(new JwtOptions
        {
            Issuer = Issuer,
            Audience = Audience,
            SecretKey = secretKey,
            ExpiryMinutes = expiryMinutes
        }), clock ?? TimeProvider.System);

    private static TokenValidationParameters ValidationParameters(string key) => new()
    {
        ValidateIssuer = true,
        ValidIssuer = Issuer,
        ValidateAudience = true,
        ValidAudience = Audience,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero
    };

    [Fact]
    public void CreateToken_ContainsUserIdEmailAndRoleClaims()
    {
        var generated = CreateService().CreateToken(TestUser);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(generated.Value);

        Assert.Equal("7", jwt.Claims.First(c => c.Type == JwtRegisteredClaimNames.Sub).Value);
        Assert.Equal("dana@example.com", jwt.Claims.First(c => c.Type == JwtRegisteredClaimNames.Email).Value);
        Assert.Equal("Admin", jwt.Claims.First(c => c.Type == "role").Value);
        Assert.Equal(Issuer, jwt.Issuer);
        Assert.Contains(Audience, jwt.Audiences);
    }

    [Fact]
    public void CreateToken_ExpiryIsClockPlusConfiguredMinutes()
    {
        var now = new DateTimeOffset(2030, 1, 1, 10, 0, 0, TimeSpan.Zero);

        var generated = CreateService(expiryMinutes: 30, clock: new FixedTimeProvider(now)).CreateToken(TestUser);

        var expected = new DateTime(2030, 1, 1, 10, 30, 0, DateTimeKind.Utc);
        Assert.Equal(expected, generated.ExpiresAtUtc);
        Assert.Equal(DateTimeKind.Utc, generated.ExpiresAtUtc.Kind);
        Assert.Equal(expected, new JwtSecurityTokenHandler().ReadJwtToken(generated.Value).ValidTo);
    }

    [Fact]
    public void CreateToken_IsAcceptedByTheConfiguredKey()
    {
        var generated = CreateService().CreateToken(TestUser);

        var principal = new JwtSecurityTokenHandler().ValidateToken(
            generated.Value, ValidationParameters(TestKey), out var validatedToken);

        Assert.NotNull(validatedToken);
        Assert.NotNull(principal);
    }

    [Fact]
    public void CreateToken_IsRejectedWhenValidatedWithADifferentKey()
    {
        var generated = CreateService().CreateToken(TestUser);

        Assert.ThrowsAny<SecurityTokenException>(() => new JwtSecurityTokenHandler().ValidateToken(
            generated.Value, ValidationParameters("another-test-key-0123456789-abcdefghij"), out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("tiny-key")]
    public void CreateToken_MissingOrTooShortKey_ThrowsWithoutLeakingTheKey(string weakKey)
    {
        var service = CreateService(secretKey: weakKey);

        var exception = Assert.Throws<InvalidOperationException>(() => service.CreateToken(TestUser));

        Assert.DoesNotContain("tiny-key", exception.Message);
    }
}
