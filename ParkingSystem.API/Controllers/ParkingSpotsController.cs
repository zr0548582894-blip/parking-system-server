using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ParkingSystem.Core.Common;
using ParkingSystem.Core.DTOs.ParkingSpots;
using ParkingSystem.Core.Enums;
using ParkingSystem.Core.Interfaces.Services;

namespace ParkingSystem.API.Controllers;

[Route("api/parking-spots")]
[Authorize]
public class ParkingSpotsController : ApiControllerBase
{
    private readonly IParkingSpotService _spotService;

    public ParkingSpotsController(IParkingSpotService spotService)
    {
        _spotService = spotService;
    }

    /// <summary>
    /// רשימת מקומות חניה עם סינון (קומה, פעיל, מאפיין, חיפוש בקוד), מיון ו-pagination.
    /// כשמציינים תאריך, כל מקום מסומן פנוי או תפוס בתאריך הזה.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<ParkingSpotResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetAll([FromQuery] ParkingSpotQuery query, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return InvalidModel();
        }

        return OkOrFailure(await _spotService.GetPagedAsync(query, ct));
    }

    /// <summary>מקום חניה אחד לפי מזהה.</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(ParkingSpotResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById([FromRoute] int id, CancellationToken ct) =>
        OkOrFailure(await _spotService.GetByIdAsync(id, ct));

    /// <summary>יצירת מקום חניה חדש (מנהל בלבד).</summary>
    [HttpPost]
    [Authorize(Roles = nameof(UserRole.Admin))]
    [ProducesResponseType(typeof(ParkingSpotResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateParkingSpotRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return InvalidModel();
        }

        var result = await _spotService.CreateAsync(request, ct);

        // 201 עם כותרת Location שמצביעה על המקום החדש
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetById), new { id = result.Value!.Id }, result.Value)
            : Failure(result);
    }

    /// <summary>עדכון מקום חניה (מנהל בלבד).</summary>
    [HttpPut("{id:int}")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    [ProducesResponseType(typeof(ParkingSpotResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(
        [FromRoute] int id, [FromBody] UpdateParkingSpotRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return InvalidModel();
        }

        return OkOrFailure(await _spotService.UpdateAsync(id, request, ct));
    }

    /// <summary>
    /// הסרת מקום חניה (מנהל בלבד). המקום מסומן כלא פעיל ונשמר בהיסטוריה, ואי אפשר להזמין אותו יותר.
    /// מקום שיש לו הזמנות עתידיות לא יוסר.
    /// </summary>
    [HttpDelete("{id:int}")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete([FromRoute] int id, CancellationToken ct) =>
        NoContentOrFailure(await _spotService.DeleteAsync(id, ct));
}
