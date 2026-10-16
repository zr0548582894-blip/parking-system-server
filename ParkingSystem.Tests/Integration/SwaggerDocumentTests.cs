using System.Net;
using System.Text.Json;

namespace ParkingSystem.Tests.Integration;

// בודקות את מסמך ה-Swagger שהשרת מפיק בפועל: שכל הפעולות מתועדות, שהנעילה והרשאות נכונות,
// שלכל קוד תגובה יש תיאור, ושאין בו סודות
public class SwaggerDocumentTests : IClassFixture<ApiTestHost>
{
    private static readonly string[] ExpectedOperations =
    {
        "POST /api/auth/register", "POST /api/auth/login",
        "GET /api/features",
        "GET /api/parking-spots", "POST /api/parking-spots",
        "GET /api/parking-spots/{id}", "PUT /api/parking-spots/{id}", "DELETE /api/parking-spots/{id}",
        "POST /api/reservations", "GET /api/reservations",
        "GET /api/reservations/{id}", "DELETE /api/reservations/{id}", "GET /api/reservations/mine"
    };

    private static readonly string[] OpenOperations = { "POST /api/auth/register", "POST /api/auth/login" };

    private readonly ApiTestHost _host;

    public SwaggerDocumentTests(ApiTestHost host)
    {
        _host = host;
    }

    private async Task<(string Raw, JsonElement Doc)> LoadAsync()
    {
        var response = await _host.CreateClient().GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync();
        return (raw, JsonDocument.Parse(raw).RootElement.Clone());
    }

    private static IEnumerable<(string Key, JsonElement Operation)> Operations(JsonElement doc) =>
        doc.GetProperty("paths").EnumerateObject().SelectMany(path =>
            path.Value.EnumerateObject().Select(op => ($"{op.Name.ToUpperInvariant()} {path.Name}", op.Value)));

    private static JsonElement Operation(JsonElement doc, string key) =>
        Operations(doc).Single(o => o.Key == key).Operation;

    private static bool HasCode(JsonElement operation, string code) =>
        operation.GetProperty("responses").TryGetProperty(code, out _);

    [PostgresFact]
    public async Task TheDocumentDescribesTheApi_AndTheUiPageIsServed()
    {
        var (_, doc) = await LoadAsync();

        Assert.Equal("ParkingSystem API", doc.GetProperty("info").GetProperty("title").GetString());
        Assert.False(string.IsNullOrWhiteSpace(doc.GetProperty("info").GetProperty("description").GetString()));
        Assert.Equal(HttpStatusCode.OK, (await _host.CreateClient().GetAsync("/swagger/index.html")).StatusCode);
    }

    [PostgresFact]
    public async Task EveryEndpointIsDocumented_AndNothingExtraIsExposed()
    {
        var (_, doc) = await LoadAsync();

        var documented = Operations(doc).Select(o => o.Key).OrderBy(k => k).ToList();

        Assert.Equal(ExpectedOperations.OrderBy(k => k).ToList(), documented);
    }

    [PostgresFact]
    public async Task TheBearerSchemeIsDefined_SoTheAuthorizeButtonAppears()
    {
        var (_, doc) = await LoadAsync();

        var scheme = doc.GetProperty("components").GetProperty("securitySchemes").GetProperty("Bearer");
        Assert.Equal("http", scheme.GetProperty("type").GetString());
        Assert.Equal("bearer", scheme.GetProperty("scheme").GetString());
        Assert.Equal("JWT", scheme.GetProperty("bearerFormat").GetString());
    }

    [PostgresFact]
    public async Task OnlyTheProtectedOperationsRequireTheBearerToken()
    {
        var (_, doc) = await LoadAsync();

        foreach (var (key, operation) in Operations(doc))
        {
            var locked = operation.TryGetProperty("security", out var security)
                         && security.EnumerateArray().Any(requirement => requirement.TryGetProperty("Bearer", out _));

            Assert.Equal(!OpenOperations.Contains(key), locked);
        }
    }

    [PostgresFact]
    public async Task EveryOperationHasASummary_AndEveryResponseCodeHasADescription()
    {
        var (_, doc) = await LoadAsync();

        foreach (var (key, operation) in Operations(doc))
        {
            Assert.False(string.IsNullOrWhiteSpace(operation.GetProperty("summary").GetString()), $"{key} has no summary");

            foreach (var response in operation.GetProperty("responses").EnumerateObject())
            {
                var description = response.Value.GetProperty("description").GetString();
                Assert.False(string.IsNullOrWhiteSpace(description) || description is "OK" or "Success",
                    $"{key} response {response.Name} has no real description");
            }
        }
    }

    [PostgresFact]
    public async Task TheMainStatusCodesAreDocumentedWhereTheyApply()
    {
        var (_, doc) = await LoadAsync();

        // כל פעולה מוגנת יכולה להחזיר 401
        foreach (var (key, operation) in Operations(doc).Where(o => !OpenOperations.Contains(o.Key)))
        {
            Assert.True(HasCode(operation, "401"), $"{key} should document 401");
        }

        var reserve = Operation(doc, "POST /api/reservations");
        foreach (var code in new[] { "201", "400", "401", "403", "404", "409" })
        {
            Assert.True(HasCode(reserve, code), $"POST /api/reservations should document {code}");
        }

        Assert.Contains("Location", reserve.GetProperty("responses").GetProperty("201").GetProperty("description").GetString());

        Assert.True(HasCode(Operation(doc, "DELETE /api/reservations/{id}"), "204"));
        Assert.True(HasCode(Operation(doc, "DELETE /api/parking-spots/{id}"), "409"));
        Assert.True(HasCode(Operation(doc, "POST /api/auth/register"), "409"));

        // פעולות שמותרות לתפקיד מסוים בלבד מתעדות 403
        foreach (var key in new[] { "POST /api/parking-spots", "PUT /api/parking-spots/{id}", "DELETE /api/parking-spots/{id}",
                                    "POST /api/reservations", "GET /api/reservations", "GET /api/reservations/mine" })
        {
            Assert.True(HasCode(Operation(doc, key), "403"), $"{key} should document 403");
        }
    }

    [PostgresFact]
    public async Task EnumsAreShownAsReadableText_AndDtoRulesAreVisible()
    {
        var (_, doc) = await LoadAsync();
        var schemas = doc.GetProperty("components").GetProperty("schemas");

        var role = schemas.GetProperty("UserRole");
        Assert.Equal("string", role.GetProperty("type").GetString());
        Assert.Equal(new[] { "Employee", "Admin" }, role.GetProperty("enum").EnumerateArray().Select(e => e.GetString()).ToArray());

        var password = schemas.GetProperty("RegisterRequest").GetProperty("properties").GetProperty("password");
        Assert.Equal(8, password.GetProperty("minLength").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(schemas.GetProperty("CreateReservationRequest")
            .GetProperty("properties").GetProperty("date").GetProperty("description").GetString()));
    }

    [PostgresFact]
    public async Task TheDocumentContainsNoSecretsOrTokens()
    {
        var (raw, _) = await LoadAsync();

        Assert.DoesNotContain(ApiTestHost.AdminPassword, raw);
        Assert.DoesNotContain(ApiTestHost.EmployeePassword, raw);
        Assert.DoesNotContain("end-to-end-test-only-key", raw);
        Assert.DoesNotMatch(@"eyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\.", raw);
    }
}
