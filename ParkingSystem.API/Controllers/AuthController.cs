using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ParkingSystem.API.Extensions;
using ParkingSystem.Core.DTOs.Auth;
using ParkingSystem.Core.Interfaces.Services;

namespace ParkingSystem.API.Controllers;

// הרשמה והתחברות כמשאבים (REST): יצירת משתמש היא POST /api/users, ויצירת token היא POST /api/tokens.
// השמות הם שמות עצם ברבים, והפעולה נקבעת לפי ה-HTTP method ולא לפי שם פעולה בכתובת
[Route("api")]
[AllowAnonymous]
[EnableRateLimiting(RateLimitingExtensions.AuthPolicy)]
public class AuthController : ApiControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    /// <summary>הרשמת משתמש חדש. ההרשמה תמיד יוצרת עובד (Employee) ומחזירה token.</summary>
    /// <response code="200">ההרשמה הצליחה, ומוחזר token לשליחה בבקשות הבאות.</response>
    /// <response code="400">אחד או יותר מהשדות אינם תקינים. מוחזרת רשימת שגיאות לכל שדה.</response>
    /// <response code="409">כתובת האימייל כבר רשומה במערכת.</response>
    /// <response code="429">יותר מדי ניסיונות מאותה כתובת. יש לחכות (כותרת Retry-After).</response>
    [HttpPost("users")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return InvalidModel();
        }

        return OkOrFailure(await _authService.RegisterAsync(request, ct));
    }

    /// <summary>התחברות עם אימייל וסיסמה. מחזירה token לשליחה בכותרת Authorization.</summary>
    /// <response code="200">ההתחברות הצליחה, ומוחזר token.</response>
    /// <response code="400">אחד או יותר מהשדות אינם תקינים.</response>
    /// <response code="401">האימייל או הסיסמה שגויים.</response>
    /// <response code="429">יותר מדי ניסיונות מאותה כתובת. יש לחכות (כותרת Retry-After).</response>
    [HttpPost("tokens")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return InvalidModel();
        }

        return OkOrFailure(await _authService.LoginAsync(request, ct));
    }
}
