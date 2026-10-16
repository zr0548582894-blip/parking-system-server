using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ParkingSystem.API.Extensions;
using ParkingSystem.Core.Interfaces.Repositories;
using ParkingSystem.Core.Interfaces.Security;
using ParkingSystem.Core.Interfaces.Services;
using ParkingSystem.Core.Options;
using ParkingSystem.Data.Context;

namespace ParkingSystem.Tests.Api;

// בודקות את רישום ה-DI וה-lifetimes בלי להפעיל שרת ובלי בסיס נתונים (לא נפתח חיבור)
public class DependencyInjectionTests
{
    private static ServiceProvider BuildProvider(Dictionary<string, string?>? overrides = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = "Host=localhost;Database=unused;Username=unused",
            ["Jwt:SecretKey"] = "unit-test-only-key-0123456789-abcdefghij",
            ["Jwt:Issuer"] = "issuer",
            ["Jwt:Audience"] = "audience",
            ["Jwt:ExpiryMinutes"] = "30",
            ["ReservationRules:MaxDaysAhead"] = "30"
        };

        foreach (var (key, value) in overrides ?? new Dictionary<string, string?>())
        {
            settings[key] = value;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPersistence(configuration);
        services.AddApplicationServices(configuration);

        // ValidateOnBuild + ValidateScopes: מזהים בזמן הבנייה שירות שאי אפשר ליצור, או Singleton שמחזיק Scoped
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    [Fact]
    public void EveryServiceAndRepositoryCanBeResolvedInsideARequestScope()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        foreach (var type in new[]
        {
            typeof(IAuthService), typeof(IFeatureService), typeof(IParkingSpotService), typeof(IReservationService),
            typeof(IUserRepository), typeof(IParkingSpotRepository), typeof(IReservationRepository), typeof(IFeatureRepository),
            typeof(IPasswordHasher), typeof(ITokenService)
        })
        {
            Assert.NotNull(scope.ServiceProvider.GetRequiredService(type));
        }
    }

    [Fact]
    public void DbContextAndServices_AreScoped_OneInstancePerRequest()
    {
        using var provider = BuildProvider();

        using var firstScope = provider.CreateScope();
        using var secondScope = provider.CreateScope();

        var first = firstScope.ServiceProvider;
        var second = secondScope.ServiceProvider;

        // אותה בקשה: אותו DbContext, גם דרך ה-repositories השונים
        Assert.Same(first.GetRequiredService<AppDbContext>(), first.GetRequiredService<AppDbContext>());
        // בקשות שונות: DbContext שונה
        Assert.NotSame(first.GetRequiredService<AppDbContext>(), second.GetRequiredService<AppDbContext>());
        Assert.NotSame(first.GetRequiredService<IReservationService>(), second.GetRequiredService<IReservationService>());
    }

    [Fact]
    public void Hasher_TokenServiceAndClock_AreSingletons_SharedAcrossRequests()
    {
        using var provider = BuildProvider();

        using var firstScope = provider.CreateScope();
        using var secondScope = provider.CreateScope();

        Assert.Same(firstScope.ServiceProvider.GetRequiredService<IPasswordHasher>(),
            secondScope.ServiceProvider.GetRequiredService<IPasswordHasher>());
        Assert.Same(firstScope.ServiceProvider.GetRequiredService<ITokenService>(),
            secondScope.ServiceProvider.GetRequiredService<ITokenService>());
        Assert.Same(TimeProvider.System, firstScope.ServiceProvider.GetRequiredService<TimeProvider>());
    }

    [Fact]
    public void AScopedServiceCannotBeResolvedOutsideARequestScope()
    {
        using var provider = BuildProvider();

        // זו בדיוק ההגנה מפני הבאג של "singleton שמחזיק scoped"
        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IReservationService>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("too-short")]
    public void MissingOrShortJwtKey_IsRejectedByOptionsValidation(string key)
    {
        using var provider = BuildProvider(new Dictionary<string, string?> { ["Jwt:SecretKey"] = key });

        var exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<JwtOptions>>().Value);

        Assert.Contains("Jwt:SecretKey", exception.Message);
        Assert.DoesNotContain("too-short", exception.Message);
    }

    [Fact]
    public void ReservationRules_AreReadFromConfiguration()
    {
        using var provider = BuildProvider(new Dictionary<string, string?> { ["ReservationRules:MaxDaysAhead"] = "14" });

        Assert.Equal(14, provider.GetRequiredService<IOptions<ReservationRulesOptions>>().Value.MaxDaysAhead);
    }
}
