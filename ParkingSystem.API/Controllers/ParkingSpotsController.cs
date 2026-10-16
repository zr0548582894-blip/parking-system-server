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
    /// <response code="200">עמוד של מקומות חניה, עם הסך הכול ומספר העמודים.</response>
    /// <response code="400">פרמטרים לא תקינים, למשל page=0, או available=true בלי date.</response>
    /// <response code="401">לא מחובר: חסר token או שהוא אינו תקף.</response>
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
    /// <response code="200">פרטי המקום.</response>
    /// <response code="401">לא מחובר.</response>
    /// <response code="404">מקום החניה לא נמצא.</response>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(ParkingSpotResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById([FromRoute] int id, CancellationToken ct) =>
        OkOrFailure(await _spotService.GetByIdAsync(id, ct));

    /// <summary>יצירת מקום חניה חדש (מנהל בלבד).</summary>
    /// <response code="201">המקום נוצר. כותרת Location מצביעה על GET /api/parking-spots/{id}.</response>
    /// <response code="400">שדות לא תקינים, או מאפיין שלא קיים.</response>
    /// <response code="401">לא מחובר.</response>
    /// <response code="403">הפעולה מותרת למנהל (Admin) בלבד.</response>
    /// <response code="409">קוד המקום כבר קיים.</response>
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
    /// <response code="200">המקום עודכן, ומוחזר במצבו החדש.</response>
    /// <response code="400">שדות לא תקינים, או מאפיין שלא קיים.</response>
    /// <response code="401">לא מחובר.</response>
    /// <response code="403">הפעולה מותרת למנהל (Admin) בלבד.</response>
    /// <response code="404">מקום החניה לא נמצא.</response>
    /// <response code="409">הקוד תפוס אצל מקום אחר, או שמישהו עדכן את המקום באותו רגע.</response>
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
    /// <response code="204">המקום הוסר מהשימוש ונשמר בהיסטוריה.</response>
    /// <response code="401">לא מחובר.</response>
    /// <response code="403">הפעולה מותרת למנהל (Admin) בלבד.</response>
    /// <response code="404">מקום החניה לא נמצא.</response>
    /// <response code="409">יש למקום הזמנות עתידיות, ולכן אי אפשר להסיר אותו.</response>
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
