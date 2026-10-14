using ParkingSystem.Core.Common;
using ParkingSystem.Core.DTOs.Common;
using ParkingSystem.Core.Entities;

namespace ParkingSystem.Core.Interfaces.Repositories;

public interface IReservationRepository : IRepository
{
    // עם מעקב, כולל המקום והמשתמש, לצורך ביטול
    Task<Reservation?> GetByIdAsync(int id, CancellationToken ct);

    // הזמנה פעילה בלבד נחשבת "תפוסה"
    Task<bool> IsSpotReservedAsync(int spotId, DateOnly date, CancellationToken ct);
    Task<bool> UserHasReservationAsync(int userId, DateOnly date, CancellationToken ct);

    // userId = null: כל ההזמנות (Admin). אחרת: ההזמנות של אותו משתמש
    Task<PagedResult<Reservation>> GetPagedAsync(int? userId, PaginationQuery query, CancellationToken ct);

    Task AddAsync(Reservation reservation, CancellationToken ct);

    // מריץ את הפעולה בטרנזקציה אחת: הבדיקה העסקית והשמירה יחד.
    // אם הפעולה זורקת חריגה, הטרנזקציה מתבטלת
    Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct);
}
