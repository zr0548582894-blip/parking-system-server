using Microsoft.AspNetCore.Mvc;
using ParkingSystem.API.Middleware;

namespace ParkingSystem.API.Extensions;

public static class HttpContextExtensions
{
    // כותב תשובת שגיאה אחידה (ProblemDetails) עם מזהה הבקשה. משמש את טיפול השגיאות הגלובלי
    // ואת תשובות האימות (401 ו-403), כדי שכל שגיאה בשרת תיראה אותו דבר
    public static async Task WriteProblemAsync(this HttpContext context, int status, string title)
    {
        var problem = context.CreateProblem(status, title);

        context.Response.StatusCode = status;
        context.Response.Headers[CorrelationIdMiddleware.HeaderName] = CorrelationIdMiddleware.Get(context);

        await context.Response.WriteAsJsonAsync(
            problem, options: null, contentType: "application/problem+json", context.RequestAborted);
    }

    // בונה את אובייקט השגיאה האחיד: קוד, הודעה, הכתובת, ומזהה הבקשה.
    // משמש גם את ה-Controllers, שמחזירים אותו כתוצאה של פעולה
    public static ProblemDetails CreateProblem(this HttpContext context, int status, string title)
    {
        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Instance = context.Request.Path
        };
        problem.Extensions["correlationId"] = CorrelationIdMiddleware.Get(context);

        return problem;
    }
}
