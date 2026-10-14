using ParkingSystem.Core.Common;
using ParkingSystem.Core.DTOs.ParkingSpots;
using ParkingSystem.Core.Entities;

namespace ParkingSystem.Core.Interfaces.Repositories;

public interface IParkingSpotRepository : IRepository
{
    // עם מעקב (tracking), לשימוש כשרוצים לעדכן את המקום
    Task<ParkingSpot?> GetByIdAsync(int id, CancellationToken ct);

    // ללא מעקב, לקריאה בלבד, כולל ה-Features
    Task<ParkingSpot?> GetDetailsAsync(int id, CancellationToken ct);

    // סינון, מיון ו-Skip/Take מתבצעים בתוך השאילתה מול בסיס הנתונים
    Task<PagedResult<ParkingSpot>> GetPagedAsync(ParkingSpotQuery query, CancellationToken ct);

    // שאילתה אחת לכל העמוד: אילו מהמקומות תפוסים בתאריך (מונע N+1)
    Task<IReadOnlySet<int>> GetReservedSpotIdsAsync(IReadOnlyCollection<int> spotIds, DateOnly date, CancellationToken ct);

    Task<bool> CodeExistsAsync(string code, int? excludeId, CancellationToken ct);
    Task<bool> HasFutureReservationsAsync(int spotId, DateOnly fromDate, CancellationToken ct);

    Task AddAsync(ParkingSpot spot, CancellationToken ct);
    void Remove(ParkingSpot spot);
}
