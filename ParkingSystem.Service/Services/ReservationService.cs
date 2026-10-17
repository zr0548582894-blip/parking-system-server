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
    private const string UserNotFoundMessage = "המשתמש לא נמצא. יש להתחבר מחדש.";
    private const string SpotNotFoundMessage = "מקום החניה לא נמצא.";
    private const string SpotInactiveMessage = "מקום החניה אינו פעיל.";
    private const string SpotTakenMessage = "המקום כבר תפוס בתאריך זה.";
    private const string UserAlreadyReservedMessage = "כבר יש לך הזמנה פעילה בתאריך זה.";
    private const string LostTheRaceMessage = "המקום נתפס על ידי משתמש אחר. נסי מקום אחר.";
    private const string ReservationNotFoundMessage = "ההזמנה לא נמצאה.";
    private const string NotAllowedToCancelMessage = "אין לך הרשאה לבטל הזמנה זו.";
    private const string NotAllowedToViewMessage = "אין לך הרשאה לצפות בהזמנה זו.";

    private readonly IReservationRepository _reservations;
    private readonly IParkingSpotRepository _spots;
    private readonly IUserRepository _users;
    private readonly IMapper _mapper;
    private readonly TimeProvider _timeProvider;
    private readonly ReservationRulesOptions _rules;
    private readonly ILogger<ReservationService> _logger;

    public ReservationService(
        IReservationRepository reservations,
        IParkingSpotRepository spots,
        IUserRepository users,
        IMapper mapper,
        TimeProvider timeProvider,
        IOptions<ReservationRulesOptions> rules,
        ILogger<ReservationService> logger)
    {
        _reservations = reservations;
        _spots = spots;
        _users = users;
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
                token => TryCreateAsync(userId, request, date, now, token), ct);
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

        // טוענים את ההזמנה מחדש (קריאה בלבד), כולל המשתמש והמקום, כדי להחזיר תשובה מלאה
        var reservation = await _reservations.GetDetailsAsync(created.Value, ct);

        return Result<ReservationResponse>.Success(_mapper.Map<ReservationResponse>(reservation));
    }

    public async Task<Result> CancelAsync(int reservationId, int userId, UserRole role, CancellationToken ct)
    {
        var reservation = await _reservations.GetByIdAsync(reservationId, ct);
        if (reservation is null)
        {
            return Result.Fail(ErrorType.NotFound, ReservationNotFoundMessage);
        }

        if (!CanAccess(reservation, userId, role))
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

    public async Task<Result<ReservationResponse>> GetByIdAsync(
        int reservationId, int userId, UserRole role, CancellationToken ct)
    {
        var reservation = await _reservations.GetDetailsAsync(reservationId, ct);
        if (reservation is null)
        {
            return Result<ReservationResponse>.Fail(ErrorType.NotFound, ReservationNotFoundMessage);
        }

        if (!CanAccess(reservation, userId, role))
        {
            return Result<ReservationResponse>.Fail(ErrorType.Forbidden, NotAllowedToViewMessage);
        }

        return Result<ReservationResponse>.Success(_mapper.Map<ReservationResponse>(reservation));
    }

    public async Task<Result<PagedResult<ReservationResponse>>> GetMineAsync(
        int userId, PaginationQuery query, CancellationToken ct) =>
        ToPagedResponse(await _reservations.GetPagedAsync(userId, query, ct));

    public async Task<Result<PagedResult<ReservationResponse>>> GetAllAsync(
        PaginationQuery query, CancellationToken ct) =>
        ToPagedResponse(await _reservations.GetPagedAsync(userId: null, query, ct));

    // הבדיקות העסקיות התלויות במצב הנתונים והשמירה. רצות בתוך הטרנזקציה
    private async Task<Result<int>> TryCreateAsync(
        int userId, CreateReservationRequest request, DateOnly date, DateTime now, CancellationToken ct)
    {
        // טוקן תקין של משתמש שנמחק: עונים 401 במקום ליפול על מפתח זר בבסיס הנתונים
        if (await _users.GetByIdAsync(userId, ct) is null)
        {
            return Result<int>.Fail(ErrorType.Unauthorized, UserNotFoundMessage);
        }

        var spotId = request.ParkingSpotId;
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
            _logger.LogWarning(
                "Reservation conflict: spot {SpotId} is already reserved on {Date} (requested by user {UserId}).",
                spotId, date, userId);
            return Result<int>.Fail(ErrorType.Conflict, SpotTakenMessage);
        }

        if (await _reservations.UserHasReservationAsync(userId, date, ct))
        {
            _logger.LogWarning(
                "Reservation conflict: user {UserId} already has a reservation on {Date}.", userId, date);
            return Result<int>.Fail(ErrorType.Conflict, UserAlreadyReservedMessage);
        }

        // AutoMapper: המקום, התאריך והסטטוס (פעילה). המזמין נלקח מה-token וזמן היצירה נקבע כאן
        var reservation = _mapper.Map<Reservation>(request);
        reservation.UserId = userId;
        reservation.CreatedAtUtc = now;

        // הגנה שנייה: מעדכנים את המקום עצמו, כדי שה-UPDATE יכלול את ה-xmin שנקרא.
        // אם מישהו אחר עדכן אותו בינתיים, השמירה תיכשל ותיזרק חריגת התנגשות
        spot.LastReservedAtUtc = now;

        await _reservations.AddAsync(reservation, ct);
        await _reservations.SaveChangesAsync(ct);

        return Result<int>.Success(reservation.Id);
    }

    // כלל הגישה להזמנה, לצפייה ולביטול: רק הבעלים או Admin
    private static bool CanAccess(Reservation reservation, int userId, UserRole role) =>
        role == UserRole.Admin || reservation.UserId == userId;

    private Result<PagedResult<ReservationResponse>> ToPagedResponse(PagedResult<Reservation> page) =>
        Result<PagedResult<ReservationResponse>>.Success(new PagedResult<ReservationResponse>
        {
            Items = _mapper.Map<List<ReservationResponse>>(page.Items),
            Page = page.Page,
            PageSize = page.PageSize,
            TotalCount = page.TotalCount
        });
}
