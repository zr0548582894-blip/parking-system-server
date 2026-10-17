using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace ParkingSystem.Tests.Integration;

// מוכיחות שהשרת מוכן לרוץ בענן (Render) ולשרת לקוח React מדומיין אחר
public class DeploymentReadinessTests : IClassFixture<ApiTestHost>
{
    private const string AllowedOrigin = "http://localhost:5173";
    private const string AllowedOriginWithTrailingSlashInConfig = "https://parking-client.example.com";
    private const string ProductionEnvironment = "Production";

    private readonly ApiTestHost _host;

    public DeploymentReadinessTests(ApiTestHost host)
    {
        _host = host;
    }

    private static HttpRequestMessage Preflight(string origin) =>
        new(HttpMethod.Options, "/api/reservations")
        {
            Headers =
            {
                { "Origin", origin },
                { "Access-Control-Request-Method", "POST" },
                { "Access-Control-Request-Headers", "authorization,content-type" }
            }
        };

    private static string? AllowOriginHeader(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Access-Control-Allow-Origin", out var values) ? values.Single() : null;

    // ---------- בדיקת חיים ----------

    [PostgresFact]
    public async Task Healthz_IsOpenAndReportsOk_WithoutAppearingInSwagger()
    {
        var client = _host.CreateClient();

        var response = await client.GetAsync("/healthz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("ok", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());

        var swagger = await client.GetStringAsync("/swagger/v1/swagger.json");
        Assert.DoesNotContain("healthz", swagger);
    }

    // ---------- CORS ----------

    [PostgresFact]
    public async Task APreflightFromTheConfiguredClient_IsAllowed_WithTheMethodsAndHeadersItAsksFor()
    {
        var response = await _host.CreateClient().SendAsync(Preflight(AllowedOrigin));

        Assert.True(response.IsSuccessStatusCode);
        Assert.Equal(AllowedOrigin, AllowOriginHeader(response));
        Assert.Contains("POST", string.Join(",", response.Headers.GetValues("Access-Control-Allow-Methods")));
        Assert.Contains("authorization", string.Join(",", response.Headers.GetValues("Access-Control-Allow-Headers")).ToLowerInvariant());
    }

    [PostgresFact]
    public async Task ATrailingSlashInTheConfiguredOrigin_DoesNotBreakTheMatch()
    {
        var response = await _host.CreateClient().SendAsync(Preflight(AllowedOriginWithTrailingSlashInConfig));

        Assert.Equal(AllowedOriginWithTrailingSlashInConfig, AllowOriginHeader(response));
    }

    [PostgresFact]
    public async Task AnUnknownOrigin_GetsNoCorsPermission()
    {
        var response = await _host.CreateClient().SendAsync(Preflight("https://evil.example.com"));

        Assert.Null(AllowOriginHeader(response));
    }

    [PostgresFact]
    public async Task WithNoConfiguredOrigins_NoWebsiteIsAllowed()
    {
        var client = _host.CreateClient(ProductionEnvironment, new Dictionary<string, string?> { ["Cors:AllowedOrigins"] = "" });

        var response = await client.SendAsync(Preflight(AllowedOrigin));

        Assert.Null(AllowOriginHeader(response));
    }

    [PostgresFact]
    public async Task TheBrowserIsAllowedToReadTheLocationAndCorrelationIdHeaders()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/features") { Headers = { { "Origin", AllowedOrigin } } };

        var response = await _host.CreateClient().SendAsync(request);

        Assert.Equal(AllowedOrigin, AllowOriginHeader(response));
        var exposed = string.Join(",", response.Headers.GetValues("Access-Control-Expose-Headers"));
        Assert.Contains("Location", exposed);
        Assert.Contains("X-Correlation-Id", exposed);
    }

    // ---------- מאחורי proxy (Render) ----------

    [PostgresFact]
    public async Task BehindAProxy_TheLocationHeaderUsesTheForwardedHttpsHostAndScheme()
    {
        var client = _host.CreateClient();
        var register = await client.PostAsJsonAsync("/api/users", new
        {
            fullName = "Proxy Tester",
            email = $"proxy-{Guid.NewGuid():N}@test.local",
            password = "Passw0rd!Test"
        });
        var token = (await register.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/reservations")
        {
            Content = JsonContent.Create(new { parkingSpotId = await SpotIdAsync(client, token!, "L2-05"), date = DateTime.UtcNow.AddDays(11).ToString("yyyy-MM-dd") }),
            Headers =
            {
                { "Authorization", $"Bearer {token}" },
                { "X-Forwarded-Proto", "https" },
                { "X-Forwarded-Host", "parking-api.onrender.com" }
            }
        };

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.StartsWith("https://parking-api.onrender.com/api/reservations/", response.Headers.Location!.ToString());
    }

    private static async Task<int> SpotIdAsync(HttpClient client, string token, string code)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/parking-spots?search={code}&pageSize=5")
        {
            Headers = { { "Authorization", $"Bearer {token}" } }
        };
        var page = await (await client.SendAsync(request)).Content.ReadFromJsonAsync<JsonElement>();
        return page.GetProperty("items").EnumerateArray().First(item => item.GetProperty("code").GetString() == code)
            .GetProperty("id").GetInt32();
    }

    // ---------- Production ----------

    [PostgresFact]
    public async Task InProduction_TheServerDoesNotRedirectToHttps_BecauseTheProxyHandlesIt()
    {
        var response = await _host.CreateClient(ProductionEnvironment).GetAsync("/healthz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [PostgresFact]
    public async Task InProduction_SwaggerIsAvailableByDefault()
    {
        var client = _host.CreateClient(ProductionEnvironment);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/swagger/v1/swagger.json")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/swagger/index.html")).StatusCode);
    }

    [PostgresFact]
    public async Task SwaggerCanBeTurnedOff_WithTheConfigurationFlag()
    {
        var client = _host.CreateClient(ProductionEnvironment, new Dictionary<string, string?> { ["Swagger:Enabled"] = "false" });

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/swagger/v1/swagger.json")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/healthz")).StatusCode);
    }

    [PostgresFact]
    public async Task InProduction_TheProtectedApiStillRequiresALogin()
    {
        var client = _host.CreateClient(ProductionEnvironment);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/features")).StatusCode);
    }
}
