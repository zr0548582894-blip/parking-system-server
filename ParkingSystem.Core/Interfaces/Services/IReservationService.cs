using ParkingSystem.Core.Common;
using ParkingSystem.Core.DTOs.Common;
using ParkingSystem.Core.DTOs.Reservations;
using ParkingSystem.Core.Enums;

namespace ParkingSystem.Core.Interfaces.Services;

public interface IReservationService
{
    // לב המערכת: הזמנת מקום ליום. מחזיר Conflict כשהמקום נתפס על ידי מישהו אחר
    Task<Result<ReservationResponse>> CreateAsync(int userId, CreateReservationRequest request, CancellationToken ct);

    // ביטול מותר לבעלים או ל-Admin בלבד
    Task<Result> CancelAsync(int reservationId, int userId, UserRole role, CancellationToken ct);

    // הזמנה אחת: הבעלים או Admin בלבד
    Task<Result<ReservationResponse>> GetByIdAsync(int reservationId, int userId, UserRole role, CancellationToken ct);

    Task<Result<PagedResult<ReservationResponse>>> GetMineAsync(int userId, PaginationQuery query, CancellationToken ct);
    Task<Result<PagedResult<ReservationResponse>>> GetAllAsync(PaginationQuery query, CancellationToken ct);
}
