using Microsoft.EntityFrameworkCore;
using ParkingSystem.Core.Common;
using ParkingSystem.Core.DTOs.ParkingSpots;
using ParkingSystem.Core.Entities;
using ParkingSystem.Core.Enums;
using ParkingSystem.Core.Interfaces.Repositories;
using ParkingSystem.Data.Context;

namespace ParkingSystem.Data.Repositories;

public class ParkingSpotRepository : Repository, IParkingSpotRepository
{
    public ParkingSpotRepository(AppDbContext context) : base(context)
    {
    }

    // עם מעקב: מיועד למצב שבו מעדכנים את המקום
    public Task<ParkingSpot?> GetByIdAsync(int id, CancellationToken ct) =>
        Context.ParkingSpots
            .Include(p => p.Features)
            .FirstOrDefaultAsync(p => p.Id == id, ct);

    // ללא מעקב: קריאה בלבד
    public Task<ParkingSpot?> GetDetailsAsync(int id, CancellationToken ct) =>
        Context.ParkingSpots
            .AsNoTracking()
            .Include(p => p.Features)
            .FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<PagedResult<ParkingSpot>> GetPagedAsync(ParkingSpotQuery query, CancellationToken ct)
    {
        // IQueryable: השאילתה עוד לא רצה. כל Where מוסיף תנאי, וה-SQL נבנה ורץ רק ב-CountAsync / ToListAsync
        IQueryable<ParkingSpot> spots = Context.ParkingSpots.AsNoTracking();

        if (query.Floor.HasValue)
        {
            spots = spots.Where(p => p.Floor == query.Floor.Value);
        }

        if (query.IsActive.HasValue)
        {
            spots = spots.Where(p => p.IsActive == query.IsActive.Value);
        }

        if (query.FeatureId.HasValue)
        {
            var featureId = query.FeatureId.Value;
            spots = spots.Where(p => p.Features.Any(f => f.Id == featureId));
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // ILike = חיפוש חלקי בלי רגישות לאותיות גדולות/קטנות. מנטרלים את התווים המיוחדים של LIKE
            var escaped = query.Search.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
            spots = spots.Where(p => EF.Functions.ILike(p.Code, $"%{escaped}%"));
        }

        if (query.Available && query.Date.HasValue)
        {
            // רק מקומות פעילים שאין עליהם הזמנה פעילה באותו תאריך
            var date = query.Date.Value;
            spots = spots.Where(p => p.IsActive && !p.Reservations.Any(r => r.Date == date && r.Status == ReservationStatus.Active));
        }

        // הספירה נעשית אחרי הסינון ולפני החיתוך לעמוד
        var totalCount = await spots.CountAsync(ct);

        // מיון לפי השדה שנבחר. ThenBy(Id) מבטיח סדר יציב, אחרת עמודים עלולים לחזור על אותה שורה
        var ordered = (query.SortBy, query.SortDir) switch
        {
            (ParkingSpotSortField.Floor, SortDirection.Desc) => spots.OrderByDescending(p => p.Floor).ThenBy(p => p.Id),
            (ParkingSpotSortField.Floor, _) => spots.OrderBy(p => p.Floor).ThenBy(p => p.Id),
            (_, SortDirection.Desc) => spots.OrderByDescending(p => p.Code).ThenBy(p => p.Id),
            _ => spots.OrderBy(p => p.Code).ThenBy(p => p.Id)
        };

        // pagination אמיתי: Skip ו-Take נכנסים ל-SQL (OFFSET / LIMIT), ולא נעשים בזיכרון.
        // Include טוען את ה-Features יחד עם המקומות באותה שאילתה (בלי N+1)
        var items = await ordered
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Include(p => p.Features)
            .ToListAsync(ct);

        return new PagedResult<ParkingSpot>
        {
            Items = items,
            Page = query.Page,
            PageSize = query.PageSize,
            TotalCount = totalCount
        };
    }

    // שאילתה אחת לכל העמוד: אילו מהמקומות שברשימה תפוסים בתאריך
    public async Task<IReadOnlySet<int>> GetReservedSpotIdsAsync(
        IReadOnlyCollection<int> spotIds, DateOnly date, CancellationToken ct)
    {
        var ids = spotIds.ToList();

        var reserved = await Context.Reservations
            .AsNoTracking()
            .Where(r => r.Date == date && r.Status == ReservationStatus.Active && ids.Contains(r.ParkingSpotId))
            .Select(r => r.ParkingSpotId)
            .ToListAsync(ct);

        return reserved.ToHashSet();
    }

    // excludeId: בעדכון, מתעלמים מהמקום עצמו, כדי שלא יתנגש עם הקוד של עצמו
    public Task<bool> CodeExistsAsync(string code, int? excludeId, CancellationToken ct) =>
        Context.ParkingSpots.AnyAsync(p => p.Code == code && (excludeId == null || p.Id != excludeId), ct);

    public Task<bool> HasFutureReservationsAsync(int spotId, DateOnly fromDate, CancellationToken ct) =>
        Context.Reservations.AnyAsync(
            r => r.ParkingSpotId == spotId && r.Status == ReservationStatus.Active && r.Date >= fromDate, ct);

    // רק מסמן להוספה/הסרה. השמירה בפועל נעשית ב-SaveChangesAsync
    public async Task AddAsync(ParkingSpot spot, CancellationToken ct) =>
        await Context.ParkingSpots.AddAsync(spot, ct);

    public void Remove(ParkingSpot spot) => Context.ParkingSpots.Remove(spot);
}
