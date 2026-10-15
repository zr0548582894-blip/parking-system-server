using ParkingSystem.Core.Common;
using ParkingSystem.Core.DTOs.Auth;
using ParkingSystem.Core.Entities;
using ParkingSystem.Core.Enums;
using ParkingSystem.Core.Exceptions;
using ParkingSystem.Core.Interfaces.Repositories;
using ParkingSystem.Core.Interfaces.Security;
using ParkingSystem.Core.Interfaces.Services;

namespace ParkingSystem.Service.Services;

public class AuthService : IAuthService
{
    private const string EmailTakenMessage = "כתובת האימייל כבר רשומה במערכת.";
    private const string InvalidCredentialsMessage = "אימייל או סיסמה שגויים.";

    private readonly IUserRepository _users;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenService _tokenService;

    public AuthService(IUserRepository users, IPasswordHasher passwordHasher, ITokenService tokenService)
    {
        _users = users;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
    }

    public async Task<Result<AuthResponse>> RegisterAsync(RegisterRequest request, CancellationToken ct)
    {
        var email = EmailNormalizer.Normalize(request.Email);

        // ולידציה עסקית (תלויה במצב הנתונים): ההודעה הידידותית למקרה הרגיל
        if (await _users.EmailExistsAsync(email, ct))
        {
            return Result<AuthResponse>.Fail(ErrorType.Conflict, EmailTakenMessage);
        }

        var user = new User
        {
            Email = email,
            FullName = request.FullName.Trim(),
            PasswordHash = _passwordHasher.Hash(request.Password),
            // הרשמה עצמית יוצרת תמיד Employee. את התפקיד Admin אי אפשר לקבל דרך ה-API
            Role = UserRole.Employee
        };

        await _users.AddAsync(user, ct);

        try
        {
            await _users.SaveChangesAsync(ct);
        }
        catch (DuplicateEmailException)
        {
            // שתי הרשמות מקבילות עם אותו אימייל עברו יחד את הבדיקה למעלה.
            // האילוץ הייחודי בבסיס הנתונים עצר את השנייה, והתוצאה זהה לבדיקה הרגילה
            return Result<AuthResponse>.Fail(ErrorType.Conflict, EmailTakenMessage);
        }

        return Result<AuthResponse>.Success(ToAuthResponse(user));
    }

    public async Task<Result<AuthResponse>> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        var email = EmailNormalizer.Normalize(request.Email);
        var user = await _users.GetByEmailAsync(email, ct);

        // אותה הודעה גם כשהאימייל לא קיים וגם כשהסיסמה שגויה, כדי לא לחשוף אילו אימיילים רשומים
        if (user is null || !_passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            return Result<AuthResponse>.Fail(ErrorType.Unauthorized, InvalidCredentialsMessage);
        }

        return Result<AuthResponse>.Success(ToAuthResponse(user));
    }

    private AuthResponse ToAuthResponse(User user)
    {
        var token = _tokenService.CreateToken(user);

        return new AuthResponse
        {
            Token = token.Value,
            ExpiresAtUtc = token.ExpiresAtUtc,
            UserId = user.Id,
            FullName = user.FullName,
            Email = user.Email,
            Role = user.Role
        };
    }
}
