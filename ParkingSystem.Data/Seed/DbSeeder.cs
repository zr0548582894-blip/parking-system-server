using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ParkingSystem.Core.Common;
using ParkingSystem.Core.Entities;
using ParkingSystem.Core.Enums;
using ParkingSystem.Core.Interfaces.Security;
using ParkingSystem.Core.Options;
using ParkingSystem.Data.Context;

namespace ParkingSystem.Data.Seed;

// מזין נתוני התחלה, כדי שאפשר יהיה להפעיל את המערכת מיד אחרי הקמת בסיס הנתונים.
// הפעולה בטוחה להרצה חוזרת: כל חלק נכנס רק אם הנתון שלו עדיין לא קיים
public static class DbSeeder
{
    private const string Accessible = "נגיש";
    private const string Covered = "מקורה";
    private const string EvCharging = "טעינה לרכב חשמלי";

    // IPasswordHasher הוא ממשק מ-Core: Data לא מכיר את Service, וה-API מחבר את המימוש
    public static async Task SeedAsync(
        AppDbContext context,
        IPasswordHasher passwordHasher,
        SeedUsersOptions usersOptions,
        ILogger logger,
        CancellationToken ct)
    {
        await SeedFeaturesAsync(context, ct);
        await SeedParkingSpotsAsync(context, ct);
        await SeedUsersAsync(context, passwordHasher, usersOptions, logger, ct);
    }

    private static async Task SeedUsersAsync(
        AppDbContext context,
        IPasswordHasher passwordHasher,
        SeedUsersOptions options,
        ILogger logger,
        CancellationToken ct)
    {
        await SeedUserAsync(context, passwordHasher, logger, "Admin user", "Admin",
            options.AdminEmail, options.AdminPassword, UserRole.Admin, ct);
        await SeedUserAsync(context, passwordHasher, logger, "Employee user", "Employee",
            options.EmployeeEmail, options.EmployeePassword, UserRole.Employee, ct);
    }

    private static async Task SeedUserAsync(
        AppDbContext context,
        IPasswordHasher passwordHasher,
        ILogger logger,
        string description,
        string fullName,
        string email,
        string password,
        UserRole role,
        CancellationToken ct)
    {
        // הסיסמה מגיעה רק מהקונפיגורציה. בלעדיה לא יוצרים את המשתמש (ולא כותבים אותה בלוג)
        if (string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning("{Description} was not seeded: no password is configured under {Section}.",
                description, SeedUsersOptions.SectionName);
            return;
        }

        var normalizedEmail = EmailNormalizer.Normalize(email);
        if (await context.Users.AnyAsync(u => u.Email == normalizedEmail, ct))
        {
            return;
        }

        context.Users.Add(new User
        {
            Email = normalizedEmail,
            FullName = fullName,
            PasswordHash = passwordHasher.Hash(password),
            Role = role
        });

        await context.SaveChangesAsync(ct);
    }

    private static async Task SeedFeaturesAsync(AppDbContext context, CancellationToken ct)
    {
        if (await context.Features.AnyAsync(ct))
        {
            return;
        }

        context.Features.AddRange(
            new Feature { Name = Accessible },
            new Feature { Name = Covered },
            new Feature { Name = EvCharging });

        await context.SaveChangesAsync(ct);
    }

    private static async Task SeedParkingSpotsAsync(AppDbContext context, CancellationToken ct)
    {
        if (await context.ParkingSpots.AnyAsync(ct))
        {
            return;
        }

        var features = await context.Features.ToDictionaryAsync(f => f.Name, ct);

        // ארבע קומות, חמישה מקומות בכל אחת: מרתף (-1), קרקע (0), קומה 1 וקומה 2
        var spots = new List<ParkingSpot>();
        AddFloor(spots, features, prefix: "B", floor: -1, covered: true);
        AddFloor(spots, features, prefix: "G", floor: 0, covered: false);
        AddFloor(spots, features, prefix: "L1", floor: 1, covered: true);
        AddFloor(spots, features, prefix: "L2", floor: 2, covered: false);

        // שני מקומות נגישים בקומת הקרקע, ושלושה עם טעינה לרכב חשמלי
        AddFeature(spots, features, Accessible, "G-01", "G-02");
        AddFeature(spots, features, EvCharging, "L1-01", "L1-02", "L2-01");

        context.ParkingSpots.AddRange(spots);
        await context.SaveChangesAsync(ct);
    }

    private static void AddFloor(
        List<ParkingSpot> spots, Dictionary<string, Feature> features, string prefix, int floor, bool covered)
    {
        for (var number = 1; number <= 5; number++)
        {
            var spot = new ParkingSpot { Code = $"{prefix}-{number:00}", Floor = floor, IsActive = true };

            if (covered)
            {
                spot.Features.Add(features[Covered]);
            }

            spots.Add(spot);
        }
    }

    private static void AddFeature(
        List<ParkingSpot> spots, Dictionary<string, Feature> features, string featureName, params string[] codes)
    {
        foreach (var spot in spots.Where(s => codes.Contains(s.Code)))
        {
            spot.Features.Add(features[featureName]);
        }
    }
}
