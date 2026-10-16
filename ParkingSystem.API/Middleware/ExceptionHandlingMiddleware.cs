using Microsoft.AspNetCore.Mvc;

namespace ParkingSystem.API.Middleware;

// הרשת האחרונה: תופס כל חריגה שלא טופלה בשום מקום, כותב אותה ללוג, ומחזיר ללקוח תשובת JSON אחידה
// (ProblemDetails) בלי לחשוף פרטים פנימיים. כשלים עסקיים צפויים (למשל "מקום תפוס") לא עוברים כאן:
// הם חוזרים מה-Service כ-Result וה-Controller הופך אותם לקוד HTTP
public class ExceptionHandlingMiddleware
{
    private const string UnexpectedErrorTitle = "אירעה שגיאה בלתי צפויה בשרת.";
    private const string BadRequestTitle = "הבקשה אינה תקינה.";

    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // הלקוח ניתק באמצע הבקשה: אין למי לענות ואין כאן תקלה
            _logger.LogDebug("The client cancelled the request {Method} {Path}.",
                context.Request.Method, context.Request.Path.ToUriComponent());
        }
        catch (Exception exception)
        {
            await HandleAsync(context, exception);
        }
    }

    private async Task HandleAsync(HttpContext context, Exception exception)
    {
        var correlationId = CorrelationIdMiddleware.Get(context);

        // בקשה פגומה (למשל גוף גדול מדי) היא טעות של הלקוח; כל השאר הוא תקלה בשרת
        var status = exception is BadHttpRequestException badRequest
            ? badRequest.StatusCode
            : StatusCodes.Status500InternalServerError;

        using (_logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            if (status >= StatusCodes.Status500InternalServerError)
            {
                _logger.LogError(exception, "Unhandled exception while processing {Method} {Path}.",
                    context.Request.Method, context.Request.Path.ToUriComponent());
            }
            else
            {
                _logger.LogWarning(exception, "Bad request {Method} {Path}.",
                    context.Request.Method, context.Request.Path.ToUriComponent());
            }

            if (context.Response.HasStarted)
            {
                // כבר התחלנו לשלוח תשובה, ואי אפשר להחליף אותה: מנתקים את החיבור
                _logger.LogWarning("The response had already started, so the error body could not be written.");
                context.Abort();
                return;
            }
        }

        var problem = new ProblemDetails
        {
            Status = status,
            Title = status >= StatusCodes.Status500InternalServerError ? UnexpectedErrorTitle : BadRequestTitle,
            Instance = context.Request.Path
        };
        problem.Extensions["correlationId"] = correlationId;

        context.Response.Headers.Clear();
        context.Response.StatusCode = status;
        context.Response.Headers[CorrelationIdMiddleware.HeaderName] = correlationId;

        await context.Response.WriteAsJsonAsync(
            problem, options: null, contentType: "application/problem+json", context.RequestAborted);
    }
}
