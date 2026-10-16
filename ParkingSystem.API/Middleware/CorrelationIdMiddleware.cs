namespace ParkingSystem.API.Middleware;

// נותן לכל בקשה מזהה (CorrelationId), מחזיר אותו ללקוח בכותרת, ומוסיף אותו לכל שורת לוג של הבקשה.
// כך אפשר לשחזר בקשה שלמה מתוך לוג משותף
public class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-Id";

    private const string ItemKey = "CorrelationId";
    private const int MaxIdLength = 64;

    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;

    public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    // המזהה של הבקשה הנוכחית. נקרא ממקומות אחרים, כמו מנגנון הטיפול בשגיאות
    public static string Get(HttpContext context) =>
        context.Items[ItemKey] as string ?? context.TraceIdentifier;

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = ResolveCorrelationId(context);

        context.Items[ItemKey] = correlationId;
        context.Response.Headers[HeaderName] = correlationId;

        // כל שורת לוג שנכתבת בזמן הבקשה תכלול את ה-CorrelationId (NLog קורא אותו מה-scope)
        using (_logger.BeginScope(new Dictionary<string, object> { [ItemKey] = correlationId }))
        {
            await _next(context);
        }
    }

    // לקוח יכול לשלוח מזהה משלו, אבל רק אם הוא קצר ובטוח (אותיות, ספרות, - ו-_).
    // ערך אחר יכול לשבש את הלוג (למשל שורה חדשה), ולכן מחליפים אותו במזהה חדש
    private static string ResolveCorrelationId(HttpContext context)
    {
        var incoming = context.Request.Headers[HeaderName].FirstOrDefault();

        return incoming is { Length: > 0 and <= MaxIdLength } && incoming.All(IsSafeCharacter)
            ? incoming
            : Guid.NewGuid().ToString("N");
    }

    private static bool IsSafeCharacter(char c) => char.IsAsciiLetterOrDigit(c) || c is '-' or '_';
}
