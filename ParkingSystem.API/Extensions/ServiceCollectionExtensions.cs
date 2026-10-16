using Microsoft.EntityFrameworkCore;
using ParkingSystem.Core.Interfaces.Repositories;
using ParkingSystem.Core.Interfaces.Security;
using ParkingSystem.Core.Interfaces.Services;
using ParkingSystem.Core.Options;
using ParkingSystem.Data.Context;
using ParkingSystem.Data.Repositories;
using ParkingSystem.Service.Mapping;
using ParkingSystem.Service.Security;
using ParkingSystem.Service.Services;

namespace ParkingSystem.API.Extensions;

// כל רישום ה-Dependency Injection במקום אחד, עם ה-lifetime של כל שירות.
// ה-API הוא השכבה היחידה שמכירה גם את Data וגם את Service, ולכן הרישום יושב כאן
public static class ServiceCollectionExtensions
{
    // Scoped = instance אחד לכל בקשת HTTP. נדרש לכל מה שתלוי ב-DbContext
    public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        // DbContext: Scoped. אינו thread-safe ומחזיק מעקב אחרי ישויות, ולכן instance נפרד לכל בקשה
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("Default")));

        // Repositories: Scoped, כי הם תלויים ב-DbContext. Singleton שמחזיק DbContext היה באג:
        // ה-DbContext היה נתפס לכל חיי האפליקציה ומשותף בין בקשות שונות
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IParkingSpotRepository, ParkingSpotRepository>();
        services.AddScoped<IReservationRepository, ReservationRepository>();
        services.AddScoped<IFeatureRepository, FeatureRepository>();

        return services;
    }

    public static IServiceCollection AddApplicationServices(this IServiceCollection services, IConfiguration configuration)
    {
        // הגדרות: נקשרות מהקונפיגורציה. מפתח ה-JWT מגיע מ-User Secrets או ממשתנה סביבה, לא מקובץ.
        // ValidateOnStart: אם הגדרה חסרה, השרת נכשל בעלייה עם הודעה ברורה ולא ב-500 מסתורי בהמשך
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .Validate(options => options.SecretKey.Length >= 32,
                "Jwt:SecretKey must be configured (User Secrets or environment variable) with at least 32 characters.")
            .Validate(options => options.ExpiryMinutes > 0, "Jwt:ExpiryMinutes must be positive.")
            .ValidateOnStart();

        services.AddOptions<ReservationRulesOptions>()
            .Bind(configuration.GetSection(ReservationRulesOptions.SectionName))
            .Validate(options => options.MaxDaysAhead > 0, "ReservationRules:MaxDaysAhead must be positive.")
            .ValidateOnStart();

        services.AddOptions<SeedUsersOptions>()
            .Bind(configuration.GetSection(SeedUsersOptions.SectionName));

        // Singleton: שירותים חסרי מצב, בטוחים לשיתוף בין בקשות. אף אחד מהם לא תלוי ב-DbContext
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IPasswordHasher, BcryptPasswordHasher>();
        services.AddSingleton<ITokenService, JwtTokenService>();

        // AutoMapper: ההגדרות (MapperConfiguration) נטענות פעם אחת, וה-IMapper הוא Scoped
        services.AddAutoMapper(typeof(MappingProfile));

        // שירותי הלוגיקה: Scoped, כי הם תלויים ב-repositories שהם Scoped
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IFeatureService, FeatureService>();
        services.AddScoped<IParkingSpotService, ParkingSpotService>();
        services.AddScoped<IReservationService, ReservationService>();

        return services;
    }
}
