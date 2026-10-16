using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using ParkingSystem.API.Extensions;
using ParkingSystem.API.Middleware;
using ParkingSystem.Core.Common;
using ParkingSystem.Core.Enums;

namespace ParkingSystem.API.Controllers;

// בסיס משותף לכל ה-Controllers: מי המשתמש הנוכחי, ותרגום תוצאות של ה-Service לתשובות HTTP אחידות.
// ה-Controllers נשארים דקים: הם לא מכילים לוגיקה עסקית, רק מעבירים לשירות ומתרגמים את התוצאה
[ApiController]
[Produces("application/json")]
public abstract class ApiControllerBase : ControllerBase
{
    // הערכים נלקחים מה-token שהשרת אימת (חתום, ולכן אי אפשר לזייף אותם), ולא מגוף הבקשה
    protected int CurrentUserId =>
        int.Parse(User.FindFirstValue("sub")!, CultureInfo.InvariantCulture);

    protected UserRole CurrentUserRole =>
        Enum.Parse<UserRole>(User.FindFirstValue("role")!);

    // 400 כשה-DTO לא עבר את הוולידציה (Data Annotations). נקרא במפורש בתחילת כל פעולה שמקבלת נתונים
    protected ObjectResult InvalidModel()
    {
        var details = new ValidationProblemDetails(ModelState)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "אחד או יותר מהשדות אינם תקינים.",
            Instance = Request.Path
        };
        details.Extensions["correlationId"] = CorrelationIdMiddleware.Get(HttpContext);

        return new ObjectResult(details)
        {
            StatusCode = StatusCodes.Status400BadRequest,
            ContentTypes = { "application/problem+json" }
        };
    }

    // הצלחה: 200 עם התוצאה. כשל: קוד HTTP לפי סוג הכשל
    protected IActionResult OkOrFailure<T>(Result<T> result) =>
        result.IsSuccess ? Ok(result.Value) : Failure(result);

    // הצלחה: 204 בלי גוף. כשל: קוד HTTP לפי סוג הכשל
    protected IActionResult NoContentOrFailure(Result result) =>
        result.IsSuccess ? NoContent() : Failure(result);

    // כשל עסקי צפוי שה-Service החזיר, בתשובת JSON אחידה
    protected ObjectResult Failure(Result result)
    {
        var status = result.Error switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status500InternalServerError
        };

        return new ObjectResult(HttpContext.CreateProblem(status, result.Message ?? "הבקשה נכשלה."))
        {
            StatusCode = status,
            ContentTypes = { "application/problem+json" }
        };
    }
}
