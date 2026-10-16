using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ParkingSystem.Core.Common;
using ParkingSystem.Core.DTOs.Common;
using ParkingSystem.Core.DTOs.Reservations;
using ParkingSystem.Core.Enums;
using ParkingSystem.Core.Interfaces.Services;

namespace ParkingSystem.API.Controllers;

[Route("api/reservations")]
[Authorize]
public class ReservationsController : ApiControllerBase
{
    private readonly IReservationService _reservationService;

    public ReservationsController(IReservationService reservationService)
    {
        _reservationService = reservationService;
    }

    /// <summary>
    /// הזמנת מקום חניה ליום אחד (עובד בלבד). אם משתמש אחר הקדים לתפוס את המקום, מוחזר 409 עם הודעה ברורה.
    /// זהות המזמין נלקחת מה-token ולא מגוף הבקשה.
    /// </summary>
    [HttpPost]
    [Authorize(Roles = nameof(UserRole.Employee))]
    [ProducesResponseType(typeof(ReservationResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateReservationRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return InvalidModel();
        }

        var result = await _reservationService.CreateAsync(CurrentUserId, request, ct);

        // 201 עם כותרת Location שמצביעה על ההזמנה החדשה
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetById), new { id = result.Value!.Id }, result.Value)
            : Failure(result);
    }

    /// <summary>הזמנה אחת לפי מזהה: הבעלים או מנהל בלבד.</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(ReservationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById([FromRoute] int id, CancellationToken ct) =>
        OkOrFailure(await _reservationService.GetByIdAsync(id, CurrentUserId, CurrentUserRole, ct));

    /// <summary>ההזמנות של המשתמש המחובר, מהחדשה לישנה, בעמודים.</summary>
    [HttpGet("mine")]
    [Authorize(Roles = nameof(UserRole.Employee))]
    [ProducesResponseType(typeof(PagedResult<ReservationResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetMine([FromQuery] PaginationQuery query, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return InvalidModel();
        }

        return OkOrFailure(await _reservationService.GetMineAsync(CurrentUserId, query, ct));
    }

    /// <summary>כל ההזמנות במערכת, בעמודים (מנהל בלבד).</summary>
    [HttpGet]
    [Authorize(Roles = nameof(UserRole.Admin))]
    [ProducesResponseType(typeof(PagedResult<ReservationResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAll([FromQuery] PaginationQuery query, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return InvalidModel();
        }

        return OkOrFailure(await _reservationService.GetAllAsync(query, ct));
    }

    /// <summary>ביטול הזמנה: הבעלים או מנהל בלבד. ההזמנה נשמרת בהיסטוריה והמקום מתפנה.</summary>
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Cancel([FromRoute] int id, CancellationToken ct) =>
        NoContentOrFailure(await _reservationService.CancelAsync(id, CurrentUserId, CurrentUserRole, ct));
}
