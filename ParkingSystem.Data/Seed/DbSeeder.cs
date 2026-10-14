using Microsoft.EntityFrameworkCore;
using ParkingSystem.Core.Entities;
using ParkingSystem.Data.Context;

namespace ParkingSystem.Data.Seed;

// מזין נתוני התחלה, כדי שאפשר יהיה להפעיל את המערכת מיד אחרי הקמת בסיס הנתונים.
// הפעולה בטוחה להרצה חוזרת: כל חלק נכנס רק אם הטבלה שלו ריקה
public static class DbSeeder
{
    private const string Accessible = "נגיש";
    private const string Covered = "מקורה";
    private const string EvCharging = "טעינה לרכב חשמלי";

    public static async Task SeedAsync(AppDbContext context, CancellationToken ct)
    {
        await SeedFeaturesAsync(context, ct);
        await SeedParkingSpotsAsync(context, ct);
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
