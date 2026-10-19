using AutoMapper;
using Microsoft.Extensions.Logging;
using ParkingSystem.Core.Common;
using ParkingSystem.Core.DTOs.ParkingSpots;
using ParkingSystem.Core.Entities;
using ParkingSystem.Core.Exceptions;
using ParkingSystem.Core.Interfaces.Repositories;
using ParkingSystem.Core.Interfaces.Services;

namespace ParkingSystem.Service.Services;

public class ParkingSpotService : IParkingSpotService
{
    private const string SpotNotFoundMessage = "מקום החניה לא נמצא.";
    private const string CodeTakenMessage = "קוד המקום כבר קיים.";
    private const string UnknownFeatureMessage = "אחד או יותר מהמאפיינים שנבחרו לא קיים.";
    private const string DateRequiredMessage = "כדי לסנן מקומות פנויים יש לציין תאריך.";
    private const string HasFutureReservationsMessage = "לא ניתן להסיר מקום שיש לו הזמנות עתידיות.";
    private const string HasFutureReservationsEditMessage = "לא ניתן לערוך מקום שיש לו הזמנות עתידיות.";
    private const string ChangedByAnotherMessage = "המקום עודכן על ידי משתמש אחר. נסי שוב.";

    private readonly IParkingSpotRepository _spots;
    private readonly IFeatureRepository _features;
    private readonly IMapper _mapper;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ParkingSpotService> _logger;

    public ParkingSpotService(
        IParkingSpotRepository spots,
        IFeatureRepository features,
        IMapper mapper,
        TimeProvider timeProvider,
        ILogger<ParkingSpotService> logger)
    {
        _spots = spots;
        _features = features;
        _mapper = mapper;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<Result<PagedResult<ParkingSpotResponse>>> GetPagedAsync(ParkingSpotQuery query, CancellationToken ct)
    {
        // ולידציה עסקית: סינון "פנויים" לא הגיוני בלי תאריך
        if (query.Available && !query.Date.HasValue)
        {
            return Result<PagedResult<ParkingSpotResponse>>.Fail(ErrorType.Validation, DateRequiredMessage);
        }

        var page = await _spots.GetPagedAsync(query, ct);
        var items = _mapper.Map<List<ParkingSpotResponse>>(page.Items);

        // כשהתבקש תאריך, מסמנים פנוי/תפוס בעזרת שאילתה אחת לכל העמוד
        if (query.Date.HasValue && items.Count > 0)
        {
            var reservedIds = await _spots.GetReservedSpotIdsAsync(
                items.Select(item => item.Id).ToList(), query.Date.Value, ct);

            foreach (var item in items)
            {
                item.IsAvailable = item.IsActive && !reservedIds.Contains(item.Id);
            }
        }

        return Result<PagedResult<ParkingSpotResponse>>.Success(new PagedResult<ParkingSpotResponse>
        {
            Items = items,
            Page = page.Page,
            PageSize = page.PageSize,
            TotalCount = page.TotalCount
        });
    }

    public async Task<Result<ParkingSpotResponse>> GetByIdAsync(int id, CancellationToken ct)
    {
        var spot = await _spots.GetDetailsAsync(id, ct);

        return spot is null
            ? Result<ParkingSpotResponse>.Fail(ErrorType.NotFound, SpotNotFoundMessage)
            : Result<ParkingSpotResponse>.Success(_mapper.Map<ParkingSpotResponse>(spot));
    }

    public async Task<Result<ParkingSpotResponse>> CreateAsync(CreateParkingSpotRequest request, CancellationToken ct)
    {
        var code = request.Code.Trim();

        if (await _spots.CodeExistsAsync(code, excludeId: null, ct))
        {
            _logger.LogWarning("Parking spot conflict: code {Code} already exists.", code);
            return Result<ParkingSpotResponse>.Fail(ErrorType.Conflict, CodeTakenMessage);
        }

        var features = await FindFeaturesAsync(request.FeatureIds, ct);
        if (features is null)
        {
            return Result<ParkingSpotResponse>.Fail(ErrorType.Validation, UnknownFeatureMessage);
        }

        // AutoMapper: מנקה את הקוד וקובע שהמקום פעיל. המאפיינים נטענו מבסיס הנתונים ומתווספים כאן
        var spot = _mapper.Map<ParkingSpot>(request);
        spot.Features = features;

        await _spots.AddAsync(spot, ct);

        try
        {
            await _spots.SaveChangesAsync(ct);
        }
        catch (DuplicateSpotCodeException exception)
        {
            // שתי יצירות מקבילות עם אותו קוד עברו יחד את הבדיקה, והאינדקס הייחודי עצר את השנייה
            _logger.LogWarning(exception, "Parking spot conflict: code {Code} was taken by a parallel request.", code);
            return Result<ParkingSpotResponse>.Fail(ErrorType.Conflict, CodeTakenMessage);
        }

        return Result<ParkingSpotResponse>.Success(_mapper.Map<ParkingSpotResponse>(spot));
    }

    public async Task<Result<ParkingSpotResponse>> UpdateAsync(int id, UpdateParkingSpotRequest request, CancellationToken ct)
    {
        var spot = await _spots.GetByIdAsync(id, ct);
        if (spot is null)
        {
            return Result<ParkingSpotResponse>.Fail(ErrorType.NotFound, SpotNotFoundMessage);
        }

        // מקום עם הזמנות פעילות עתידיות לא נערך: אחרת אפשר להפוך אותו ללא פעיל או לשנות אותו מתחת למי שהזמין
        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);
        if (await _spots.HasFutureReservationsAsync(id, today, ct))
        {
            _logger.LogWarning("Parking spot conflict: spot {SpotId} has future reservations and cannot be edited.", id);
            return Result<ParkingSpotResponse>.Fail(ErrorType.Conflict, HasFutureReservationsEditMessage);
        }

        var code = request.Code.Trim();

        // excludeId: המקום לא מתנגש עם הקוד של עצמו
        if (await _spots.CodeExistsAsync(code, excludeId: id, ct))
        {
            _logger.LogWarning("Parking spot conflict: code {Code} is used by another spot (update of spot {SpotId}).", code, id);
            return Result<ParkingSpotResponse>.Fail(ErrorType.Conflict, CodeTakenMessage);
        }

        var features = await FindFeaturesAsync(request.FeatureIds, ct);
        if (features is null)
        {
            return Result<ParkingSpotResponse>.Fail(ErrorType.Validation, UnknownFeatureMessage);
        }

        // AutoMapper מעדכן את השדות הפשוטים (קוד, קומה, פעיל) על המקום הקיים.
        // המזהה, הגרסה וההזמנות לא נוגעים, והמאפיינים מוחלפים ידנית בשורות הבאות
        _mapper.Map(request, spot);
        spot.Features.Clear();
        foreach (var feature in features)
        {
            spot.Features.Add(feature);
        }

        try
        {
            await _spots.SaveChangesAsync(ct);
        }
        catch (ReservationConflictException exception)
        {
            // ה-token (xmin) של המקום לא תאם: מישהו עדכן אותו בין הקריאה לשמירה
            _logger.LogWarning(exception, "Parking spot conflict: spot {SpotId} was changed by another request.", id);
            return Result<ParkingSpotResponse>.Fail(ErrorType.Conflict, ChangedByAnotherMessage);
        }
        catch (DuplicateSpotCodeException exception)
        {
            // מישהו אחר תפס את הקוד החדש בדיוק באותו רגע
            _logger.LogWarning(exception, "Parking spot conflict: code {Code} was taken by a parallel request.", code);
            return Result<ParkingSpotResponse>.Fail(ErrorType.Conflict, CodeTakenMessage);
        }

        return Result<ParkingSpotResponse>.Success(_mapper.Map<ParkingSpotResponse>(spot));
    }

    // "מחיקה" = הסרה מהשימוש: המקום נשאר בבסיס הנתונים עם ההיסטוריה שלו, אבל מסומן כלא פעיל,
    // ולכן אי אפשר להזמין אותו יותר. אפשר להחזיר אותו לשימוש בעדכון (IsActive = true)
    public async Task<Result> DeleteAsync(int id, CancellationToken ct)
    {
        var spot = await _spots.GetByIdAsync(id, ct);
        if (spot is null)
        {
            return Result.Fail(ErrorType.NotFound, SpotNotFoundMessage);
        }

        // כבר הוסר: אין מה לעשות, והתוצאה זהה
        if (!spot.IsActive)
        {
            return Result.Success();
        }

        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);
        if (await _spots.HasFutureReservationsAsync(id, today, ct))
        {
            _logger.LogWarning("Parking spot conflict: spot {SpotId} has future reservations and cannot be removed.", id);
            return Result.Fail(ErrorType.Conflict, HasFutureReservationsMessage);
        }

        spot.IsActive = false;

        try
        {
            await _spots.SaveChangesAsync(ct);
        }
        catch (ReservationConflictException exception)
        {
            // מישהו שינה את המקום (או הזמין אותו) בין הקריאה לשמירה
            _logger.LogWarning(exception, "Parking spot conflict: spot {SpotId} was changed by another request.", id);
            return Result.Fail(ErrorType.Conflict, ChangedByAnotherMessage);
        }

        return Result.Success();
    }

    // מחזיר את ה-Features לפי המזהים, או null אם אחד מהם לא קיים
    private async Task<List<Feature>?> FindFeaturesAsync(IEnumerable<int> featureIds, CancellationToken ct)
    {
        var ids = featureIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return new List<Feature>();
        }

        var features = await _features.GetByIdsAsync(ids, ct);

        return features.Count == ids.Count ? features.ToList() : null;
    }
}
