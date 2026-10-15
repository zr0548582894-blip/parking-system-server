using AutoMapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ParkingSystem.Core.Common;
using ParkingSystem.Core.DTOs.Common;
using ParkingSystem.Core.DTOs.Reservations;
using ParkingSystem.Core.Entities;
using ParkingSystem.Core.Enums;
using ParkingSystem.Core.Exceptions;
using ParkingSystem.Core.Interfaces.Repositories;
using ParkingSystem.Core.Interfaces.Services;
using ParkingSystem.Core.Options;

namespace ParkingSystem.Service.Services;

public class ReservationService : IReservationService
{
    private const string DateRequiredMessage = "יש לציין תאריך.";
    private const string DateInPastMessage = "לא ניתן להזמין תאריך שעבר.";
    private const string SpotNotFoundMessage = "מקום החניה לא נמצא.";
    private const string SpotInactiveMessage = "מקום החניה אינו פעיל.";
    private const string SpotTakenMessage = "המקום כבר תפוס בתאריך זה.";
    private const string UserAlreadyReservedMessage = "כבר יש לך הזמנה פעילה בתאריך זה.";
    private const string LostTheRaceMessage = "המקום נתפס על ידי משתמש אחר. נסי מקום אחר.";
    private const string ReservationNotFoundMessage = "ההזמנה לא נמצאה.";
    private const string NotAllowedToCancelMessage = "אין לך הרשאה לבטל הזמנה זו.";

    private readonly IReservationRepository _reservations;
    private readonly IParkingSpotRepository _spots;
    private readonly IMapper _mapper;
    private readonly TimeProvider _timeProvider;
    private readonly ReservationRulesOptions _rules;
    private readonly ILogger<ReservationService> _logger;

    public ReservationService(
        IReservationRepository reservations,
        IParkingSpotRepository spots,
        IMapper mapper,
        TimeProvider timeProvider,
        IOptions<ReservationRulesOptions> rules,
        ILogger<ReservationService> logger)
    {
        _reservations = reservations;
        _spots = spots;
        _mapper = mapper;
        _timeProvider = timeProvider;
        _rules = rules.Value;
        _logger = logger;
    }

    public async Task<Result<ReservationResponse>> CreateAsync(
        int userId, CreateReservationRequest request, CancellationToken ct)
    {
        if (request.Date is not { } date)
        {
            return Result<ReservationResponse>.Fail(ErrorType.Validation, DateRequiredMessage);
        }

        // כללים שתלויים רק בתאריך: נבדקים לפני שפותחים טרנזקציה
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(now);

        if (date < today)
        {
            return Result<ReservationResponse>.Fail(ErrorType.Validation, DateInPastMessage);
        }

        if (date > today.AddDays(_rules.MaxDaysAhead))
        {
            return Result<ReservationResponse>.Fail(
                ErrorType.Validation, $"ניתן להזמין עד {_rules.MaxDaysAhead} ימים מראש.");
        }

        Result<int> created;
        try
        {
            // הקריאה, הבדיקות והשמירה רצות בטרנזקציה אחת
            created = await _reservations.ExecuteInTransactionAsync(
                token => TryCreateAsync(userId, request.ParkingSpotId, date, now, token), ct);
        }
        catch (ReservationConflictException ex)
        {
            // התחרות: מישהו הזמין את המקום בין הקריאה לשמירה. ה-xmin של המקום לא תאם,
            // או שהאינדקס הייחודי בבסיס הנתונים עצר את ההזמנה הכפולה. הטרנזקציה כבר בוטלה
            _logger.LogWarning(ex,
                "Reservation conflict: user {UserId} lost the race for spot {SpotId} on {Date}.",
                userId, request.ParkingSpotId, date);

            return Result<ReservationResponse>.Fail(ErrorType.Conflict, LostTheRaceMessage);
        }

        if (!created.IsSuccess)
        {
            return Result<ReservationResponse>.Fail(created.Error!.Value, created.Message!);
        }

        // טוענים את ההזמנה מחדש, כולל המשתמש והמקום, כדי להחזיר תשובה מלאה
        var reservation = await _reservations.GetByIdAsync(created.Value, ct);

        return Result<ReservationResponse>.Success(_mapper.Map<ReservationResponse>(reservation));
    }

    public async Task<Result> CancelAsync(int reservationId, int userId, UserRole role, CancellationToken ct)
    {
        var reservation = await _reservations.GetByIdAsync(reservationId, ct);
        if (reservation is null)
        {
            return Result.Fail(ErrorType.NotFound, ReservationNotFoundMessage);
        }

        // רק הבעלים או Admin
        if (role != UserRole.Admin && reservation.UserId != userId)
        {
            return Result.Fail(ErrorType.Forbidden, NotAllowedToCancelMessage);
        }

        // כבר מבוטלת: אין מה לעשות, והתוצאה זהה
        if (reservation.Status == ReservationStatus.Cancelled)
        {
            return Result.Success();
        }

        // ההזמנה נשמרת בהיסטוריה, ואינה נספרת יותר כתופסת את המקום (האינדקס הייחודי חל על פעילות בלבד)
        reservation.Status = ReservationStatus.Cancelled;
        await _reservations.SaveChangesAsync(ct);

        return Result.Success();
    }

    public async Task<Result<PagedResult<ReservationResponse>>> GetMineAsync(
        int userId, PaginationQuery query, CancellationToken ct) =>
        ToPagedResponse(await _reservations.GetPagedAsync(userId, query, ct));

    public async Task<Result<PagedResult<ReservationResponse>>> GetAllAsync(
        PaginationQuery query, CancellationToken ct) =>
        ToPagedResponse(await _reservations.GetPagedAsync(userId: null, query, ct));

    // הבדיקות העסקיות התלויות במצב הנתונים והשמירה. רצות בתוך הטרנזקציה
    private async Task<Result<int>> TryCreateAsync(
        int userId, int spotId, DateOnly date, DateTime now, CancellationToken ct)
    {
        var spot = await _spots.GetByIdAsync(spotId, ct);
        if (spot is null)
        {
            return Result<int>.Fail(ErrorType.NotFound, SpotNotFoundMessage);
        }

        if (!spot.IsActive)
        {
            return Result<int>.Fail(ErrorType.Validation, SpotInactiveMessage);
        }

        // הגנה ראשונה: הודעה ברורה במקרה הרגיל (לא מספיקה לבדה בבקשות מקבילות)
        if (await _reservations.IsSpotReservedAsync(spotId, date, ct))
        {
            return Result<int>.Fail(ErrorType.Conflict, SpotTakenMessage);
        }

        if (await _reservations.UserHasReservationAsync(userId, date, ct))
        {
            return Result<int>.Fail(ErrorType.Conflict, UserAlreadyReservedMessage);
        }

        var reservation = new Reservation
        {
            UserId = userId,
            ParkingSpotId = spotId,
            Date = date,
            Status = ReservationStatus.Active,
            CreatedAtUtc = now
        };

        // הגנה שנייה: מעדכנים את המקום עצמו, כדי שה-UPDATE יכלול את ה-xmin שנקרא.
        // אם מישהו אחר עדכן אותו בינתיים, השמירה תיכשל ותיזרק חריגת התנגשות
        spot.LastReservedAtUtc = now;

        await _reservations.AddAsync(reservation, ct);
        await _reservations.SaveChangesAsync(ct);

        return Result<int>.Success(reservation.Id);
    }

    private Result<PagedResult<ReservationResponse>> ToPagedResponse(PagedResult<Reservation> page) =>
        Result<PagedResult<ReservationResponse>>.Success(new PagedResult<ReservationResponse>
        {
            Items = _mapper.Map<List<ReservationResponse>>(page.Items),
            Page = page.Page,
            PageSize = page.PageSize,
            TotalCount = page.TotalCount
        });
}
