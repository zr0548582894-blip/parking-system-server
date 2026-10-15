using AutoMapper;
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
    private const string ChangedByAnotherMessage = "המקום עודכן על ידי משתמש אחר. נסי שוב.";

    private readonly IParkingSpotRepository _spots;
    private readonly IFeatureRepository _features;
    private readonly IMapper _mapper;
    private readonly TimeProvider _timeProvider;

    public ParkingSpotService(
        IParkingSpotRepository spots,
        IFeatureRepository features,
        IMapper mapper,
        TimeProvider timeProvider)
    {
        _spots = spots;
        _features = features;
        _mapper = mapper;
        _timeProvider = timeProvider;
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
            return Result<ParkingSpotResponse>.Fail(ErrorType.Conflict, CodeTakenMessage);
        }

        var features = await FindFeaturesAsync(request.FeatureIds, ct);
        if (features is null)
        {
            return Result<ParkingSpotResponse>.Fail(ErrorType.Validation, UnknownFeatureMessage);
        }

        var spot = new ParkingSpot { Code = code, Floor = request.Floor, IsActive = true, Features = features };

        await _spots.AddAsync(spot, ct);

        try
        {
            await _spots.SaveChangesAsync(ct);
        }
        catch (DuplicateSpotCodeException)
        {
            // שתי יצירות מקבילות עם אותו קוד עברו יחד את הבדיקה, והאינדקס הייחודי עצר את השנייה
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

        var code = request.Code.Trim();

        // excludeId: המקום לא מתנגש עם הקוד של עצמו
        if (await _spots.CodeExistsAsync(code, excludeId: id, ct))
        {
            return Result<ParkingSpotResponse>.Fail(ErrorType.Conflict, CodeTakenMessage);
        }

        var features = await FindFeaturesAsync(request.FeatureIds, ct);
        if (features is null)
        {
            return Result<ParkingSpotResponse>.Fail(ErrorType.Validation, UnknownFeatureMessage);
        }

        spot.Code = code;
        spot.Floor = request.Floor;
        spot.IsActive = request.IsActive;
        spot.Features.Clear();
        foreach (var feature in features)
        {
            spot.Features.Add(feature);
        }

        try
        {
            await _spots.SaveChangesAsync(ct);
        }
        catch (ReservationConflictException)
        {
            // ה-token (xmin) של המקום לא תאם: מישהו עדכן אותו בין הקריאה לשמירה
            return Result<ParkingSpotResponse>.Fail(ErrorType.Conflict, ChangedByAnotherMessage);
        }
        catch (DuplicateSpotCodeException)
        {
            // מישהו אחר תפס את הקוד החדש בדיוק באותו רגע
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
            return Result.Fail(ErrorType.Conflict, HasFutureReservationsMessage);
        }

        spot.IsActive = false;

        try
        {
            await _spots.SaveChangesAsync(ct);
        }
        catch (ReservationConflictException)
        {
            // מישהו שינה את המקום (או הזמין אותו) בין הקריאה לשמירה
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
