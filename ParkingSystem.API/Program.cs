using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NLog;
using NLog.Web;
using ParkingSystem.API.Extensions;
using ParkingSystem.API.Middleware;
using ParkingSystem.API.Swagger;
using ParkingSystem.Core.Interfaces.Security;
using ParkingSystem.Core.Options;
using ParkingSystem.Data.Context;
using ParkingSystem.Data.Seed;

var builder = WebApplication.CreateBuilder(args);

// ב-Render (ובכל פלטפורמת ענן) הפורט שהשרת צריך להאזין לו מגיע במשתנה PORT, ועל כל הכתובות (0.0.0.0).
// מקומית, כשהמשתנה לא קיים, נשארות הכתובות מ-launchSettings.json
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(port))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

// NLog מחליף את מנגנון הלוגים המובנה, אבל הקוד ממשיך לכתוב דרך ILogger הרגיל.
// ההגדרות (יעדים, פורמט, רמות) נמצאות ב-nlog.config.
// מנקים את ספקי הלוגים המובנים, כדי שכל הלוגים יעברו דרך NLog בלבד ובפורמט אחד
builder.Logging.ClearProviders();
builder.Host.UseNLog();

// בסביבת פיתוח כותבים גם הודעות Debug. בייצור נשארים ב-Information
var nlogConfiguration = LogManager.Configuration;
if (builder.Environment.IsDevelopment() && nlogConfiguration is not null)
{
    nlogConfiguration.Variables["minLevel"] = "Debug";
    LogManager.ReconfigExistingLoggers();
}

// Add services to the container.

// כל רישום ה-DI (עם ה-lifetimes וההסברים) נמצא ב-Extensions/ServiceCollectionExtensions.cs
builder.Services.AddPersistence(builder.Configuration);
builder.Services.AddApplicationServices(builder.Configuration);
builder.Services.AddJwtAuthentication();
builder.Services.AddClientCors();    // מתיר ללקוח ה-React (Cors:AllowedOrigins) לקרוא ל-API מהדפדפן
builder.Services.AddProxyHeaders();  // השרת רץ מאחורי proxy (Render), ולכן קורא X-Forwarded-*

builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(options =>
    {
        // בדיקת ה-ModelState נעשית במפורש בתחילת כל פעולה (ApiControllerBase.InvalidModel),
        // כדי שתשובת ה-400 תהיה אחידה (ProblemDetails עם מזהה בקשה) ולא תלויה בפילטר האוטומטי
        options.SuppressModelStateInvalidFilter = true;
    })
    // enums (תפקיד, סטטוס, מיון) נשלחים ומתקבלים כטקסט קריא ("Admin") ולא כמספר
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// תיעוד ה-API (Swagger) עם כפתור Authorize ל-JWT. ההגדרה ב-Swagger/SwaggerServiceCollectionExtensions.cs
builder.Services.AddSwaggerDocumentation();

var app = builder.Build();

// בעליית השרת: מחילים migrations שעוד לא הוחלו ומזינים נתוני התחלה,
// כדי שהמערכת תרוץ בלי שום פעולה ידנית בבסיס הנתונים
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await context.Database.MigrateAsync(app.Lifetime.ApplicationStopping);
    await DbSeeder.SeedAsync(
        context,
        scope.ServiceProvider.GetRequiredService<IPasswordHasher>(),
        scope.ServiceProvider.GetRequiredService<IOptions<SeedUsersOptions>>().Value,
        app.Logger,
        app.Lifetime.ApplicationStopping);

    app.Logger.LogDebug("Database migrated and seeded.");
}

app.Logger.LogInformation("ParkingSystem API is starting in the {Environment} environment.", app.Environment.EnvironmentName);

// Configure the HTTP request pipeline.

// קודם כל קוראים את כותרות ה-proxy, כדי שהסכמה (https) והדומיין יהיו נכונים לכל מה שאחריהן
app.UseForwardedHeaders();

// הסדר חשוב: טיפול בשגיאות ראשון, כדי שיתפוס חריגה מכל מה שמגיע אחריו.
// אחריו מזהה הבקשה, ורק אז לוג הבקשות (כדי שכל שורת לוג תכלול את המזהה)
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();

// Swagger פעיל בכל סביבה (גם ב-Render), כי הוא חלק מהדרישות. אפשר לכבות עם Swagger:Enabled=false
if (app.Configuration.GetValue("Swagger:Enabled", true))
{
    app.UseSwagger();
    app.UseSwaggerUI(options => options.DocumentTitle = "ParkingSystem API");
}

// הפניה ל-HTTPS רק בפיתוח המקומי. בענן ה-HTTPS נעצר ב-load balancer (Render כבר מפנה מ-HTTP ל-HTTPS)
if (app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

// CORS לפני האימות, כדי שבקשות preflight (OPTIONS) של הדפדפן יענו בלי token
app.UseCors(ParkingSystem.API.Extensions.ServiceCollectionExtensions.ClientCorsPolicy);

// הסדר חשוב: קודם מזהים מי המשתמש (Authentication), ורק אחר כך בודקים מה מותר לו (Authorization)
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// בדיקת חיים לפלטפורמת הענן (Render). פתוחה, לא מכילה מידע, ולא מופיעה ב-Swagger
app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }))
    .AllowAnonymous()
    .ExcludeFromDescription();

app.Run();

// מאפשר לבדיקות להפעיל את השרת כולו בתוך תהליך הבדיקה (WebApplicationFactory)
public partial class Program;
