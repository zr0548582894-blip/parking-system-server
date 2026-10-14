using Microsoft.EntityFrameworkCore;
using ParkingSystem.Core.Common;
using ParkingSystem.Core.DTOs.Common;
using ParkingSystem.Core.Entities;
using ParkingSystem.Core.Enums;
using ParkingSystem.Core.Interfaces.Repositories;
using ParkingSystem.Data.Context;

namespace ParkingSystem.Data.Repositories;

public class ReservationRepository : Repository, IReservationRepository
{
    public ReservationRepository(AppDbContext context) : base(context)
    {
    }

    // עם מעקב: משמשת לביטול הזמנה (שינוי ה-Status). טוענת גם את המקום ואת המשתמש
    public Task<Reservation?> GetByIdAsync(int id, CancellationToken ct) =>
        Context.Reservations
            .Include(r => r.User)
            .Include(r => r.ParkingSpot).ThenInclude(p => p.Features)
            .FirstOrDefaultAsync(r => r.Id == id, ct);

    // רק הזמנה פעילה נחשבת "תפוסה". הזמנה שבוטלה לא חוסמת
    public Task<bool> IsSpotReservedAsync(int spotId, DateOnly date, CancellationToken ct) =>
        Context.Reservations.AnyAsync(
            r => r.ParkingSpotId == spotId && r.Date == date && r.Status == ReservationStatus.Active, ct);

    public Task<bool> UserHasReservationAsync(int userId, DateOnly date, CancellationToken ct) =>
        Context.Reservations.AnyAsync(
            r => r.UserId == userId && r.Date == date && r.Status == ReservationStatus.Active, ct);

    public async Task<PagedResult<Reservation>> GetPagedAsync(int? userId, PaginationQuery query, CancellationToken ct)
    {
        IQueryable<Reservation> reservations = Context.Reservations.AsNoTracking();

        // userId == null: כל ההזמנות (Admin). אחרת: רק של אותו משתמש
        if (userId.HasValue)
        {
            reservations = reservations.Where(r => r.UserId == userId.Value);
        }

        var totalCount = await reservations.CountAsync(ct);

        // Include + ThenInclude: ההזמנה, המשתמש, המקום וה-Features של המקום, הכול באותה שאילתה
        var items = await reservations
            .OrderByDescending(r => r.Date).ThenByDescending(r => r.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Include(r => r.User)
            .Include(r => r.ParkingSpot).ThenInclude(p => p.Features)
            .ToListAsync(ct);

        return new PagedResult<Reservation>
        {
            Items = items,
            Page = query.Page,
            PageSize = query.PageSize,
            TotalCount = totalCount
        };
    }

    // רק מסמן להוספה. השמירה בפועל נעשית ב-SaveChangesAsync
    public async Task AddAsync(Reservation reservation, CancellationToken ct) =>
        await Context.Reservations.AddAsync(reservation, ct);

    // הבדיקה העסקית והשמירה רצות בטרנזקציה אחת. אם הפעולה זורקת חריגה
    // (למשל ReservationConflictException), הטרנזקציה מתבטלת בעת ה-Dispose
    public async Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct)
    {
        await using var transaction = await Context.Database.BeginTransactionAsync(ct);

        var result = await action(ct);

        await transaction.CommitAsync(ct);
        return result;
    }
}
