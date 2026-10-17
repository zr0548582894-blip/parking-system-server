using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace ParkingSystem.Tests.Integration;

// מפעיל את ה-API כולו (אותו Program.cs, אותו pipeline) בתוך תהליך הבדיקה, מול בסיס נתונים נפרד שנמחק בסוף.
// הסיסמאות והמפתח כאן הם ערכי דמה לבדיקות בלבד, ולא קשורים לסודות של המערכת
public sealed class ApiTestHost : IAsyncLifetime
{
    public const string AdminEmail = "admin@parking.local";
    public const string AdminPassword = "Admin-Test-Pass-1!";
    public const string EmployeeEmail = "employee@parking.local";
    public const string EmployeePassword = "Employee-Test-Pass-1!";

    private readonly PostgresDatabaseFixture _database = new();
    private WebApplicationFactory<Program>? _factory;

    public HttpClient CreateClient() => _factory!.CreateClient();

    // שרת נוסף מאותה בסיס נתונים, עם סביבה או הגדרות אחרות (למשל Production, או Swagger כבוי).
    // לא עוקב אחרי הפניות, כדי שאפשר יהיה לבדוק שאין הפניה מיותרת ל-HTTPS
    public HttpClient CreateClient(string environment, IDictionary<string, string?>? settings = null) =>
        _factory!.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            if (settings is not null)
            {
                builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings));
            }
        }).CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    public async Task InitializeAsync()
    {
        if (!PostgresAvailability.IsAvailable)
        {
            return;
        }

        await _database.InitializeAsync();

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Default"] = _database.ConnectionString,
                    ["Jwt:SecretKey"] = "end-to-end-test-only-key-0123456789-abcdefgh",
                    ["Jwt:Issuer"] = "e2e-issuer",
                    ["Jwt:Audience"] = "e2e-audience",
                    ["Jwt:ExpiryMinutes"] = "30",
                    ["ReservationRules:MaxDaysAhead"] = "30",
                    ["Cors:AllowedOrigins"] = "http://localhost:5173, https://parking-client.example.com/",
                    ["SeedUsers:AdminPassword"] = AdminPassword,
                    ["SeedUsers:EmployeePassword"] = EmployeePassword
                })));

        // מפעיל את השרת עכשיו: מחיל migrations ומזין נתוני התחלה (מקומות, מאפיינים, משתמשי דמו)
        _factory.CreateClient().Dispose();
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        await _database.DisposeAsync();
    }
}
