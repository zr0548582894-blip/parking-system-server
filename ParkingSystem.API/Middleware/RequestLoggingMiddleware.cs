using System.Diagnostics;

namespace ParkingSystem.API.Middleware;

// שורת לוג אחת לכל בקשה שנכנסת: מה הבקשה, איזה קוד חזר, וכמה זמן זה לקח.
// רץ אחרי CorrelationIdMiddleware, ולכן כל שורה כוללת את מזהה הבקשה
public class RequestLoggingMiddleware
{
    // הקוד שנהוג לרשום כשהלקוח סגר את החיבור לפני שקיבל תשובה
    private const int ClientClosedRequest = 499;

    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        int? statusOverride = null;

        try
        {
            await _next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            statusOverride = ClientClosedRequest;
            throw;
        }
        catch
        {
            // החריגה תטופל ב-ExceptionHandlingMiddleware (שרץ מבחוץ) ותהפוך ל-500
            statusOverride = StatusCodes.Status500InternalServerError;
            throw;
        }
        finally
        {
            stopwatch.Stop();

            // נרשמים רק: שיטה, כתובת (בלי query string) וקוד. בלי כותרות ובלי גוף הבקשה,
            // כי שם יכולים להיות סיסמאות ו-tokens. הכתובת נכתבת מקודדת, כדי שתו מיוחד
            // (כמו שורה חדשה) לא ישתול שורות מזויפות בלוג
            _logger.LogInformation(
                "HTTP {Method} {Path} -> {StatusCode} in {ElapsedMs} ms",
                context.Request.Method,
                context.Request.Path.ToUriComponent(),
                statusOverride ?? context.Response.StatusCode,
                stopwatch.ElapsedMilliseconds);
        }
    }
}
