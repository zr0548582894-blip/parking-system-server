using AutoMapper;
using Microsoft.Extensions.Logging;
using ParkingSystem.Core.Common;
using ParkingSystem.Core.DTOs.Auth;
using ParkingSystem.Core.Entities;
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
    private readonly IMapper _mapper;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        IUserRepository users,
        IPasswordHasher passwordHasher,
        ITokenService tokenService,
        IMapper mapper,
        ILogger<AuthService> logger)
    {
        _users = users;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
        _mapper = mapper;
        _logger = logger;
    }

    public async Task<Result<AuthResponse>> RegisterAsync(RegisterRequest request, CancellationToken ct)
    {
        // AutoMapper: מנרמל את האימייל, מנקה את השם וקובע תפקיד Employee (הרשמה עצמית לא יוצרת Admin).
        // הסיסמה לא ממופה, והיא נשמרת רק כ-hash בשורה למטה
        var user = _mapper.Map<User>(request);

        // ולידציה עסקית (תלויה במצב הנתונים): ההודעה הידידותית למקרה הרגיל
        if (await _users.EmailExistsAsync(user.Email, ct))
        {
            return Result<AuthResponse>.Fail(ErrorType.Conflict, EmailTakenMessage);
        }

        user.PasswordHash = _passwordHasher.Hash(request.Password);

        await _users.AddAsync(user, ct);

        try
        {
            await _users.SaveChangesAsync(ct);
        }
        catch (DuplicateEmailException exception)
        {
            // שתי הרשמות מקבילות עם אותו אימייל עברו יחד את הבדיקה למעלה.
            // האילוץ הייחודי בבסיס הנתונים עצר את השנייה, והתוצאה זהה לבדיקה הרגילה.
            // האימייל עצמו לא נכתב ללוג (מידע אישי)
            _logger.LogWarning(exception, "Registration conflict: an email was registered by a parallel request.");
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
