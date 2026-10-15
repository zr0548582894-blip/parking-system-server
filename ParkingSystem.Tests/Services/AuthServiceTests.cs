using Moq;
using ParkingSystem.Core.Common;
using ParkingSystem.Core.DTOs.Auth;
using ParkingSystem.Core.Entities;
using ParkingSystem.Core.Enums;
using ParkingSystem.Core.Exceptions;
using ParkingSystem.Core.Interfaces.Repositories;
using ParkingSystem.Core.Interfaces.Security;
using ParkingSystem.Service.Services;

namespace ParkingSystem.Tests.Services;

public class AuthServiceTests
{
    private static readonly DateTime TokenExpiry = new(2030, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IPasswordHasher> _hasher = new();
    private readonly Mock<ITokenService> _tokens = new();
    private readonly AuthService _service;

    public AuthServiceTests()
    {
        _hasher.Setup(h => h.Hash(It.IsAny<string>())).Returns<string>(password => $"hashed:{password}");
        _tokens.Setup(t => t.CreateToken(It.IsAny<User>())).Returns(new GeneratedToken("jwt-token", TokenExpiry));

        _service = new AuthService(_users.Object, _hasher.Object, _tokens.Object);
    }

    private static RegisterRequest NewRegisterRequest(string email = "dana@example.com") => new()
    {
        FullName = "  Dana Levi  ",
        Email = email,
        Password = "Secret123!"
    };

    // ---------- הרשמה ----------

    [Fact]
    public async Task Register_NewEmail_CreatesEmployeeAndReturnsToken()
    {
        using var cts = new CancellationTokenSource();
        User? savedUser = null;
        _users.Setup(u => u.EmailExistsAsync("dana@example.com", cts.Token)).ReturnsAsync(false);
        _users.Setup(u => u.AddAsync(It.IsAny<User>(), cts.Token))
            .Callback<User, CancellationToken>((user, _) => savedUser = user)
            .Returns(Task.CompletedTask);

        var result = await _service.RegisterAsync(NewRegisterRequest(), cts.Token);

        Assert.True(result.IsSuccess);
        Assert.Equal("jwt-token", result.Value!.Token);
        Assert.Equal(TokenExpiry, result.Value.ExpiresAtUtc);
        Assert.Equal("dana@example.com", result.Value.Email);
        Assert.Equal("Dana Levi", result.Value.FullName);
        Assert.Equal(UserRole.Employee, result.Value.Role);

        Assert.NotNull(savedUser);
        Assert.Equal(UserRole.Employee, savedUser!.Role);
        Assert.Equal("hashed:Secret123!", savedUser.PasswordHash);
        Assert.NotEqual("Secret123!", savedUser.PasswordHash);
        _users.Verify(u => u.SaveChangesAsync(cts.Token), Times.Once);
    }

    [Fact]
    public async Task Register_EmailWithDifferentCaseAndSpaces_IsNormalizedBeforeCheckAndSave()
    {
        User? savedUser = null;
        _users.Setup(u => u.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .Callback<User, CancellationToken>((user, _) => savedUser = user)
            .Returns(Task.CompletedTask);

        var result = await _service.RegisterAsync(NewRegisterRequest("  Dana@Example.COM "), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _users.Verify(u => u.EmailExistsAsync("dana@example.com", It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal("dana@example.com", savedUser!.Email);
    }

    [Fact]
    public async Task Register_EmailAlreadyExists_ReturnsConflictAndSavesNothing()
    {
        _users.Setup(u => u.EmailExistsAsync("dana@example.com", It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await _service.RegisterAsync(NewRegisterRequest(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorType.Conflict, result.Error);
        _users.Verify(u => u.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
        _users.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _tokens.Verify(t => t.CreateToken(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public async Task Register_ParallelDuplicate_UniqueConstraintViolationOnSave_ReturnsConflict()
    {
        // שתי הרשמות מקבילות עברו את בדיקת הקיום, ובסיס הנתונים עצר את השנייה בשמירה
        _users.Setup(u => u.EmailExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _users.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DuplicateEmailException("duplicate"));

        var result = await _service.RegisterAsync(NewRegisterRequest(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorType.Conflict, result.Error);
        _tokens.Verify(t => t.CreateToken(It.IsAny<User>()), Times.Never);
    }

    // ---------- התחברות ----------

    [Fact]
    public async Task Login_ValidCredentials_ReturnsToken()
    {
        var user = new User
        {
            Id = 5,
            Email = "dana@example.com",
            FullName = "Dana Levi",
            PasswordHash = "stored-hash",
            Role = UserRole.Admin
        };
        _users.Setup(u => u.GetByEmailAsync("dana@example.com", It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _hasher.Setup(h => h.Verify("Secret123!", "stored-hash")).Returns(true);

        var result = await _service.LoginAsync(
            new LoginRequest { Email = " Dana@Example.com", Password = "Secret123!" }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("jwt-token", result.Value!.Token);
        Assert.Equal(5, result.Value.UserId);
        Assert.Equal(UserRole.Admin, result.Value.Role);
    }

    [Fact]
    public async Task Login_UnknownEmail_ReturnsUnauthorizedWithoutToken()
    {
        _users.Setup(u => u.GetByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);

        var result = await _service.LoginAsync(
            new LoginRequest { Email = "nobody@example.com", Password = "whatever" }, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorType.Unauthorized, result.Error);
        _tokens.Verify(t => t.CreateToken(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public async Task Login_WrongPassword_ReturnsSameUnauthorizedMessageAsUnknownEmail()
    {
        var user = new User { Id = 5, Email = "dana@example.com", PasswordHash = "stored-hash" };
        _users.Setup(u => u.GetByEmailAsync("dana@example.com", It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _users.Setup(u => u.GetByEmailAsync("nobody@example.com", It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);
        _hasher.Setup(h => h.Verify(It.IsAny<string>(), "stored-hash")).Returns(false);

        var wrongPassword = await _service.LoginAsync(
            new LoginRequest { Email = "dana@example.com", Password = "wrong" }, CancellationToken.None);
        var unknownEmail = await _service.LoginAsync(
            new LoginRequest { Email = "nobody@example.com", Password = "wrong" }, CancellationToken.None);

        Assert.Equal(ErrorType.Unauthorized, wrongPassword.Error);
        // אותה הודעה בשני המקרים, כדי שלא ניתן יהיה לגלות אילו אימיילים רשומים
        Assert.Equal(unknownEmail.Message, wrongPassword.Message);
        _tokens.Verify(t => t.CreateToken(It.IsAny<User>()), Times.Never);
    }
}
