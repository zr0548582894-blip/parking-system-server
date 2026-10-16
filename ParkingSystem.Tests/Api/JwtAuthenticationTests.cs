using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using ParkingSystem.API.Extensions;
using ParkingSystem.Core.Entities;
using ParkingSystem.Core.Enums;
using ParkingSystem.Core.Interfaces.Security;
using ParkingSystem.Core.Options;
using ParkingSystem.Service.Security;
using ParkingSystem.Tests.Helpers;

namespace ParkingSystem.Tests.Api;

// מוכיחות שהשרת מקבל רק token אמיתי שחתם בעצמו, ודוחה כל זיוף, פקיעה או ערך שגוי
public class JwtAuthenticationTests
{
    private const string ServerKey = "unit-test-only-key-0123456789-abcdefghij";
    private const string Issuer = "test-issuer";
    private const string Audience = "test-audience";

    private static readonly User Admin = new() { Id = 7, Email = "dana@example.com", Role = UserRole.Admin };
    private static readonly User Employee = new() { Id = 8, Email = "noa@example.com", Role = UserRole.Employee };

    // בונה את אותה הגדרה שה-API משתמש בה, עם הגדרות בדיקה בזיכרון
    private static (JwtBearerOptions Options, ITokenService Tokens) Build()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = "Host=localhost;Database=unused;Username=unused",
            ["Jwt:SecretKey"] = ServerKey,
            ["Jwt:Issuer"] = Issuer,
            ["Jwt:Audience"] = Audience,
            ["Jwt:ExpiryMinutes"] = "30",
            ["ReservationRules:MaxDaysAhead"] = "30"
        }).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPersistence(configuration);
        services.AddApplicationServices(configuration);
        services.AddJwtAuthentication();

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        return (options, provider.GetRequiredService<ITokenService>());
    }

    private static Task<TokenValidationResult> ValidateAsync(JwtBearerOptions options, string token) =>
        options.TokenHandlers.First().ValidateTokenAsync(token, options.TokenValidationParameters);

    private static string TokenFrom(JwtOptions settings, User user, TimeProvider? clock = null) =>
        new JwtTokenService(Options.Create(settings), clock ?? TimeProvider.System).CreateToken(user).Value;

    private static JwtOptions Settings(string key = ServerKey, string issuer = Issuer, string audience = Audience) =>
        new() { SecretKey = key, Issuer = issuer, Audience = audience, ExpiryMinutes = 30 };

    // ---------- token תקין ----------

    [Fact]
    public async Task ATokenFromTheServerItself_IsAccepted_AndCarriesTheUserIdAndRole()
    {
        var (options, tokens) = Build();

        var result = await ValidateAsync(options, tokens.CreateToken(Admin).Value);

        Assert.True(result.IsValid);
        var principal = new ClaimsPrincipal(result.ClaimsIdentity);
        Assert.Equal("7", principal.FindFirstValue("sub"));
        Assert.True(principal.IsInRole("Admin"));
        Assert.False(principal.IsInRole("Employee"));
    }

    [Fact]
    public async Task AnEmployeeToken_IsAcceptedButIsNotAnAdmin()
    {
        var (options, tokens) = Build();

        var result = await ValidateAsync(options, tokens.CreateToken(Employee).Value);

        var principal = new ClaimsPrincipal(result.ClaimsIdentity);
        Assert.True(result.IsValid);
        Assert.True(principal.IsInRole("Employee"));
        Assert.False(principal.IsInRole("Admin"));
    }

    // ---------- זיופים וטעויות: כולם נדחים ----------

    [Fact]
    public async Task ATokenSignedWithAnotherKey_IsRejected()
    {
        var (options, _) = Build();

        var result = await ValidateAsync(options, TokenFrom(Settings(key: "some-other-key-0123456789-abcdefghijklm"), Admin));

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task AnExpiredToken_IsRejected()
    {
        var (options, _) = Build();
        var longAgo = new FixedTimeProvider(new DateTimeOffset(2020, 1, 1, 10, 0, 0, TimeSpan.Zero));

        var result = await ValidateAsync(options, TokenFrom(Settings(), Admin, longAgo));

        Assert.False(result.IsValid);
        Assert.IsType<SecurityTokenExpiredException>(result.Exception);
    }

    [Theory]
    [InlineData("another-issuer", Audience)]
    [InlineData(Issuer, "another-audience")]
    public async Task AWrongIssuerOrAudience_IsRejected(string issuer, string audience)
    {
        var (options, _) = Build();

        var result = await ValidateAsync(options, TokenFrom(Settings(issuer: issuer, audience: audience), Admin));

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task ChangingTheRoleInsideAValidToken_BreaksTheSignature_AndIsRejected()
    {
        var (options, tokens) = Build();
        var parts = tokens.CreateToken(Employee).Value.Split('.');

        // המשתמש מנסה להפוך את עצמו למנהל: משנה את ה-payload ומשאיר את החתימה המקורית
        var payload = Base64UrlEncoder.Decode(parts[1]).Replace("Employee", "Admin");
        var tampered = $"{parts[0]}.{Base64UrlEncoder.Encode(payload)}.{parts[2]}";

        var result = await ValidateAsync(options, tampered);

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task ATokenWithoutASignature_AlgNone_IsRejected()
    {
        var (options, _) = Build();
        var header = Base64UrlEncoder.Encode(JsonSerializer.Serialize(new { alg = "none", typ = "JWT" }));
        var payload = Base64UrlEncoder.Encode(JsonSerializer.Serialize(new
        {
            sub = "7", role = "Admin", iss = Issuer, aud = Audience, exp = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()
        }));

        var result = await ValidateAsync(options, $"{header}.{payload}.");

        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-token")]
    [InlineData("a.b.c")]
    public async Task GarbageInsteadOfAToken_IsRejected(string garbage)
    {
        var (options, _) = Build();

        var result = await ValidateAsync(options, garbage);

        Assert.False(result.IsValid);
    }

    // ---------- תשובות 401 ו-403 ----------

    private static DefaultHttpContext NewContext()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.Request.Path = "/api/reservations";
        return context;
    }

    private static async Task<JsonElement> BodyAsync(HttpContext context)
    {
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        return document.RootElement.Clone();
    }

    [Fact]
    public async Task WhenNoValidTokenIsSent_TheChallengeReturns401AsUniformJson()
    {
        var (options, _) = Build();
        var context = NewContext();
        var scheme = new AuthenticationScheme(JwtBearerDefaults.AuthenticationScheme, null, typeof(JwtBearerHandler));

        await options.Events.Challenge(new JwtBearerChallengeContext(context, scheme, options, new AuthenticationProperties()));

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.StartsWith("application/problem+json", context.Response.ContentType);
        var body = await BodyAsync(context);
        Assert.Equal(401, body.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("correlationId").GetString()));
    }

    [Fact]
    public async Task WhenTheRoleIsNotAllowed_TheForbiddenResponseIs403AsUniformJson()
    {
        var (options, _) = Build();
        var context = NewContext();
        var scheme = new AuthenticationScheme(JwtBearerDefaults.AuthenticationScheme, null, typeof(JwtBearerHandler));

        await options.Events.Forbidden(new ForbiddenContext(context, scheme, options));

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        Assert.StartsWith("application/problem+json", context.Response.ContentType);
        Assert.Equal(403, (await BodyAsync(context)).GetProperty("status").GetInt32());
    }
}
