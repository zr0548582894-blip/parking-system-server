using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace ParkingSystem.API.Extensions;

// הגבלת קצב בקשות להרשמה והתחברות, נגד ניחוש סיסמאות. המונה נפרד לכל כתובת IP
// (אחרי UseForwardedHeaders, ולכן זו כתובת הלקוח האמיתי ולא כתובת ה-proxy של Render).
// המונים נשמרים בזיכרון של מופע השרת, וזה מספיק למופע יחיד כמו ב-Render החינמי
public static class RateLimitingExtensions
{
    public const string AuthPolicy = "auth";

    public static IServiceCollection AddAuthRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // ההגדרות נקראות מה-IConfiguration של השרת הרץ (לא בזמן הרישום), כדי שהבדיקות יוכלו לשנות אותן
            options.AddPolicy(AuthPolicy, httpContext =>
            {
                var configuration = httpContext.RequestServices.GetRequiredService<IConfiguration>();

                return RateLimitPartition.GetFixedWindowLimiter(
                    httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = configuration.GetValue("RateLimiting:AuthPermitLimit", 10),
                        Window = TimeSpan.FromSeconds(configuration.GetValue("RateLimiting:AuthWindowSeconds", 60)),
                        QueueLimit = 0
                    });
            });

            // תשובה אחידה (ProblemDetails עם מזהה בקשה), ואזהרה בלוג בלי פרטים רגישים
            options.OnRejected = async (context, token) =>
            {
                var http = context.HttpContext;
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    http.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();
                }

                http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("RateLimiting")
                    .LogWarning("Rate limit exceeded for {Method} {Path}.", http.Request.Method, http.Request.Path);

                await http.WriteProblemAsync(
                    StatusCodes.Status429TooManyRequests, "יותר מדי ניסיונות. נסי שוב בעוד כמה רגעים.");
            };
        });

        return services;
    }
}
