using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ParkingSystem.API.Middleware;
using ParkingSystem.Core.DTOs.Auth;
using ParkingSystem.Core.Enums;

namespace ParkingSystem.Tests.Integration;

// הדרישות מהשרת מקצה לקצה, דרך HTTP אמיתי: אימות, הרשאות, ולידציה, הזמנה ותחרות על המקום
public class ApiEndToEndTests : IClassFixture<ApiTestHost>
{
    private const int ParallelUsers = 8;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly ApiTestHost _host;

    public ApiEndToEndTests(ApiTestHost host)
    {
        _host = host;
    }

    // ---------- עזרים ----------

    private static string DayFromToday(int days) => DateTime.UtcNow.AddDays(days).ToString("yyyy-MM-dd");

    private HttpClient WithToken(string token)
    {
        var client = _host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<(HttpClient Client, AuthResponse Auth)> LoginAsync(string email, string password)
    {
        var response = await _host.CreateClient().PostAsJsonAsync("/api/auth/login", new { email, password });
        response.EnsureSuccessStatusCode();
        var auth = (await response.Content.ReadFromJsonAsync<AuthResponse>(Json))!;
        return (WithToken(auth.Token), auth);
    }

    private Task<(HttpClient Client, AuthResponse Auth)> AdminAsync() =>
        LoginAsync(ApiTestHost.AdminEmail, ApiTestHost.AdminPassword);

    private async Task<(HttpClient Client, AuthResponse Auth)> NewEmployeeAsync()
    {
        var response = await _host.CreateClient().PostAsJsonAsync("/api/auth/register", new
        {
            fullName = "E2E Employee",
            email = $"e2e-{Guid.NewGuid():N}@test.local",
            password = "Passw0rd!Test"
        });
        response.EnsureSuccessStatusCode();
        var auth = (await response.Content.ReadFromJsonAsync<AuthResponse>(Json))!;
        return (WithToken(auth.Token), auth);
    }

    private static async Task<int> SpotIdAsync(HttpClient client, string code)
    {
        var page = await client.GetFromJsonAsync<JsonElement>($"/api/parking-spots?search={code}&pageSize=5", Json);
        return page.GetProperty("items").EnumerateArray().First(item => item.GetProperty("code").GetString() == code)
            .GetProperty("id").GetInt32();
    }

    private static Task<HttpResponseMessage> ReserveAsync(HttpClient client, int spotId, string date) =>
        client.PostAsJsonAsync("/api/reservations", new { parkingSpotId = spotId, date });

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(Json);

    // ---------- אימות (401) ----------

    [PostgresFact]
    public async Task WithoutAToken_ProtectedEndpointsReturn401AsUniformJson()
    {
        var response = await _host.CreateClient().GetAsync("/api/features");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await ReadJsonAsync(response);
        Assert.Equal(401, body.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("correlationId").GetString()));
    }

    [PostgresFact]
    public async Task AGarbageToken_Returns401()
    {
        var response = await WithToken("garbage.token.value").GetAsync("/api/features");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [PostgresFact]
    public async Task LoginWithAWrongPassword_Returns401()
    {
        var response = await _host.CreateClient().PostAsJsonAsync("/api/auth/login",
            new { email = ApiTestHost.AdminEmail, password = "wrong-password" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [PostgresFact]
    public async Task SeededUsers_LoginWithTheirOwnRoles()
    {
        var (_, admin) = await AdminAsync();
        var (_, employee) = await LoginAsync(ApiTestHost.EmployeeEmail, ApiTestHost.EmployeePassword);

        Assert.Equal(UserRole.Admin, admin.Role);
        Assert.Equal(UserRole.Employee, employee.Role);
    }

    // ---------- ולידציה (400) ----------

    [PostgresFact]
    public async Task InvalidRegistration_Returns400WithFieldErrorsAndACorrelationId()
    {
        var response = await _host.CreateClient().PostAsJsonAsync("/api/auth/register",
            new { fullName = "X", email = "not-an-email", password = "short" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.True(body.GetProperty("errors").TryGetProperty("Email", out _));
        Assert.True(body.GetProperty("errors").TryGetProperty("Password", out _));
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("correlationId").GetString()));
    }

    [PostgresFact]
    public async Task DuplicateRegistration_Returns409()
    {
        var email = $"dup-{Guid.NewGuid():N}@test.local";
        var request = new { fullName = "Dup User", email, password = "Passw0rd!Test" };
        var client = _host.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/register", request)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/auth/register", request)).StatusCode);
    }

    [PostgresFact]
    public async Task InvalidQueryParameters_Return400()
    {
        var (client, _) = await NewEmployeeAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/parking-spots?page=0")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/parking-spots?available=true")).StatusCode);
    }

    // ---------- הרשאות לפי role (403) ----------

    [PostgresFact]
    public async Task RolesAreEnforcedOnTheServer()
    {
        var (employee, _) = await NewEmployeeAsync();
        var (admin, _) = await AdminAsync();
        var spotId = await SpotIdAsync(employee, "B-05");

        var employeeCreatesSpot = await employee.PostAsJsonAsync("/api/parking-spots", new { code = "NOPE-1", floor = 1, featureIds = Array.Empty<int>() });
        var adminReserves = await ReserveAsync(admin, spotId, DayFromToday(25));
        var employeeListsAll = await employee.GetAsync("/api/reservations");

        Assert.Equal(HttpStatusCode.Forbidden, employeeCreatesSpot.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, adminReserves.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, employeeListsAll.StatusCode);
        Assert.Equal("application/problem+json", employeeListsAll.Content.Headers.ContentType?.MediaType);
    }

    // ---------- הזמנה ותחרות ----------

    [PostgresFact]
    public async Task Reservation_Returns201WithALocationThatLeadsToIt_AndOnlyTheOwnerOrAnAdminMayRead()
    {
        var (owner, _) = await NewEmployeeAsync();
        var (other, _) = await NewEmployeeAsync();
        var (admin, _) = await AdminAsync();
        var spotId = await SpotIdAsync(owner, "B-01");

        var created = await ReserveAsync(owner, spotId, DayFromToday(3));

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var location = created.Headers.Location!;
        Assert.EndsWith($"/api/reservations/{(await ReadJsonAsync(created)).GetProperty("id").GetInt32()}", location.ToString());
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync(location)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync(location)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.GetAsync(location)).StatusCode);
    }

    [PostgresFact]
    public async Task SecondUserReservingTheSameSpotAndDay_Gets409WithAClearMessage()
    {
        var (first, _) = await NewEmployeeAsync();
        var (second, _) = await NewEmployeeAsync();
        var spotId = await SpotIdAsync(first, "B-02");
        var date = DayFromToday(4);

        Assert.Equal(HttpStatusCode.Created, (await ReserveAsync(first, spotId, date)).StatusCode);
        var loser = await ReserveAsync(second, spotId, date);

        Assert.Equal(HttpStatusCode.Conflict, loser.StatusCode);
        Assert.Equal(409, (await ReadJsonAsync(loser)).GetProperty("status").GetInt32());
    }

    [PostgresFact]
    public async Task BusinessRules_AreRejectedWithTheRightStatus()
    {
        var (employee, _) = await NewEmployeeAsync();
        var spotA = await SpotIdAsync(employee, "B-03");
        var spotB = await SpotIdAsync(employee, "B-04");
        var date = DayFromToday(5);

        Assert.Equal(HttpStatusCode.Created, (await ReserveAsync(employee, spotA, date)).StatusCode);

        Assert.Equal(HttpStatusCode.Conflict, (await ReserveAsync(employee, spotB, date)).StatusCode);           // אותו יום, מקום אחר
        Assert.Equal(HttpStatusCode.BadRequest, (await ReserveAsync(employee, spotB, DayFromToday(-1))).StatusCode);  // תאריך שעבר
        Assert.Equal(HttpStatusCode.BadRequest, (await ReserveAsync(employee, spotB, DayFromToday(40))).StatusCode); // מעבר לחלון
        Assert.Equal(HttpStatusCode.NotFound, (await ReserveAsync(employee, 999999, DayFromToday(6))).StatusCode);   // מקום לא קיים
    }

    [PostgresFact]
    public async Task CancellingFreesTheSpot_AndOnlyTheOwnerOrAnAdminMayCancel()
    {
        var (owner, _) = await NewEmployeeAsync();
        var (other, _) = await NewEmployeeAsync();
        var spotId = await SpotIdAsync(owner, "G-01");
        var date = DayFromToday(7);

        var created = await ReserveAsync(owner, spotId, date);
        var location = created.Headers.Location!;

        Assert.Equal(HttpStatusCode.Forbidden, (await other.DeleteAsync(location)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync(location)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await ReserveAsync(other, spotId, date)).StatusCode);
    }

    [PostgresFact]
    public async Task AReservedSpot_DisappearsFromTheAvailableList()
    {
        var (employee, _) = await NewEmployeeAsync();
        var spotId = await SpotIdAsync(employee, "G-02");
        var date = DayFromToday(8);

        async Task<bool> IsListedAsync()
        {
            var page = await employee.GetFromJsonAsync<JsonElement>($"/api/parking-spots?date={date}&available=true&pageSize=50", Json);
            return page.GetProperty("items").EnumerateArray().Any(item => item.GetProperty("id").GetInt32() == spotId);
        }

        Assert.True(await IsListedAsync());
        await ReserveAsync(employee, spotId, date);
        Assert.False(await IsListedAsync());
    }

    [PostgresFact]
    public async Task ParallelReservationsOverHttp_ExactlyOneWins_TheRestGet409()
    {
        var users = new List<HttpClient>();
        for (var i = 0; i < ParallelUsers; i++)
        {
            users.Add((await NewEmployeeAsync()).Client);
        }

        var spotId = await SpotIdAsync(users[0], "G-03");
        var date = DayFromToday(9);
        var startGate = new TaskCompletionSource();

        var tasks = users.Select(client => Task.Run(async () =>
        {
            await startGate.Task;
            return (await ReserveAsync(client, spotId, date)).StatusCode;
        })).ToList();

        startGate.SetResult();
        var statuses = await Task.WhenAll(tasks);

        Assert.Equal(1, statuses.Count(s => s == HttpStatusCode.Created));
        Assert.Equal(ParallelUsers - 1, statuses.Count(s => s == HttpStatusCode.Conflict));
    }

    // ---------- ניהול מקומות (Admin) ----------

    [PostgresFact]
    public async Task AdminSpotLifecycle_CreateUpdateRemove_AndARemovedSpotCannotBeReserved()
    {
        var (admin, _) = await AdminAsync();
        var (employee, _) = await NewEmployeeAsync();
        var code = $"T{Guid.NewGuid():N}"[..10];

        var created = await admin.PostAsJsonAsync("/api/parking-spots", new { code, floor = 4, featureIds = new[] { 1 } });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var location = created.Headers.Location!;

        var duplicate = await admin.PostAsJsonAsync("/api/parking-spots", new { code, floor = 4, featureIds = Array.Empty<int>() });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        var updated = await admin.PutAsJsonAsync(location, new { code, floor = 5, isActive = true, featureIds = Array.Empty<int>() });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal(5, (await ReadJsonAsync(updated)).GetProperty("floor").GetInt32());

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync(location)).StatusCode);

        var spotId = await SpotIdAsync(employee, code);
        Assert.Equal(HttpStatusCode.BadRequest, (await ReserveAsync(employee, spotId, DayFromToday(10))).StatusCode);
    }

    [PostgresFact]
    public async Task ParallelCreationsOfTheSameSpotCode_ExactlyOneSucceeds_TheRestGet409()
    {
        var (admin, _) = await AdminAsync();
        var code = $"P{Guid.NewGuid():N}"[..10];
        var startGate = new TaskCompletionSource();

        var tasks = Enumerable.Range(0, ParallelUsers).Select(_ => Task.Run(async () =>
        {
            await startGate.Task;
            return (await admin.PostAsJsonAsync("/api/parking-spots", new { code, floor = 6, featureIds = Array.Empty<int>() })).StatusCode;
        })).ToList();

        startGate.SetResult();
        var statuses = await Task.WhenAll(tasks);

        Assert.Equal(1, statuses.Count(s => s == HttpStatusCode.Created));
        Assert.Equal(ParallelUsers - 1, statuses.Count(s => s == HttpStatusCode.Conflict));
    }

    // ---------- מזהה בקשה ----------

    [PostgresFact]
    public async Task ACorrelationIdFromTheClient_IsEchoedBack()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/features");
        request.Headers.Add(CorrelationIdMiddleware.HeaderName, "e2e-trace-001");

        var response = await _host.CreateClient().SendAsync(request);

        Assert.Equal("e2e-trace-001", response.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single());
    }
}
