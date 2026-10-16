using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ParkingSystem.Core.DTOs.Auth;
using ParkingSystem.Core.Interfaces.Services;

namespace ParkingSystem.API.Controllers;

[Route("api/auth")]
[AllowAnonymous]
public class AuthController : ApiControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    /// <summary>הרשמת משתמש חדש. ההרשמה תמיד יוצרת עובד (Employee) ומחזירה token.</summary>
    [HttpPost("register")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return InvalidModel();
        }

        return OkOrFailure(await _authService.RegisterAsync(request, ct));
    }

    /// <summary>התחברות עם אימייל וסיסמה. מחזירה token לשליחה בכותרת Authorization.</summary>
    [HttpPost("login")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return InvalidModel();
        }

        return OkOrFailure(await _authService.LoginAsync(request, ct));
    }
}
