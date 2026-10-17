using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using AutoMapper;
using ParkingSystem.Core.Common;
using ParkingSystem.Core.DTOs.Reservations;
using ParkingSystem.Core.Entities;
using ParkingSystem.Core.Enums;
using ParkingSystem.Core.Exceptions;
using ParkingSystem.Core.Options;
using ParkingSystem.Data.Repositories;
using ParkingSystem.Service.Mapping;
using ParkingSystem.Service.Services;

namespace ParkingSystem.Tests.Integration;

// הוכחת התחרות מול PostgreSQL אמיתי (דרישת חלק ג). כל בדיקה בודקת שכבת הגנה אחרת
public class ReservationConcurrencyTests : IClassFixture<PostgresDatabaseFixture>
{
    private const int ParallelRequests = 8;

    private static readonly IMapper Mapper =
        new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>()).CreateMapper();

    private readonly PostgresDatabaseFixture _db;

    public ReservationConcurrencyTests(PostgresDatabaseFixture db)
    {
        _db = db;
    }

    private static DateOnly Tomorrow => DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);

    // ---------- שכבה 2: concurrency token (xmin) ----------

    [PostgresFact]
    public async Task TwoContexts_ChangeTheSameSpot_FirstSaveWins_SecondThrowsConcurrencyException()
    {
        var spotId = await _db.AddSpotAsync();

        await using var first = _db.CreateContext();
        await using var second = _db.CreateContext();

        // שני DbContext נפרדים קוראים את אותה ישות, באותה גרסה
        var spotInFirst = await first.ParkingSpots.SingleAsync(s => s.Id == spotId);
        var spotInSecond = await second.ParkingSpots.SingleAsync(s => s.Id == spotId);

        // שניהם משנים אותה
        spotInFirst.LastReservedAtUtc = DateTime.UtcNow;
        spotInSecond.LastReservedAtUtc = DateTime.UtcNow.AddMinutes(1);

        // הראשון שומר בהצלחה, והשני מקבל חריגה כי ה-xmin כבר השתנה
        await first.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }

    // ---------- שכבה 3: האינדקס הייחודי החלקי ----------

    [PostgresFact]
    public async Task UniqueIndex_StopsASecondActiveReservationForTheSameSpotAndDay_EvenWithoutTheToken()
    {
        var spotId = await _db.AddSpotAsync();
        var firstUser = await _db.AddUserAsync();
        var secondUser = await _db.AddUserAsync();

        await using var firstContext = _db.CreateContext();
        await using var secondContext = _db.CreateContext();
        var first = new ReservationRepository(firstContext);
        var second = new ReservationRepository(secondContext);

        // בכוונה לא נוגעים במקום: רק האינדקס עומד בין שתי ההזמנות
        await first.AddAsync(NewReservation(firstUser, spotId), CancellationToken.None);
        await second.AddAsync(NewReservation(secondUser, spotId), CancellationToken.None);

        await first.SaveChangesAsync(CancellationToken.None);
        await Assert.ThrowsAsync<ReservationConflictException>(() => second.SaveChangesAsync(CancellationToken.None));
    }

    [PostgresFact]
    public async Task UniqueIndex_StopsTheSameUserFromHoldingTwoSpotsOnTheSameDay()
    {
        var firstSpot = await _db.AddSpotAsync();
        var secondSpot = await _db.AddSpotAsync();
        var userId = await _db.AddUserAsync();

        await using var firstContext = _db.CreateContext();
        await using var secondContext = _db.CreateContext();
        var first = new ReservationRepository(firstContext);
        var second = new ReservationRepository(secondContext);

        await first.AddAsync(NewReservation(userId, firstSpot), CancellationToken.None);
        await second.AddAsync(NewReservation(userId, secondSpot), CancellationToken.None);

        await first.SaveChangesAsync(CancellationToken.None);
        await Assert.ThrowsAsync<ReservationConflictException>(() => second.SaveChangesAsync(CancellationToken.None));
    }

    [PostgresFact]
    public async Task UniqueIndex_IgnoresCancelledReservations_SoTheSpotCanBeBookedAgain()
    {
        var spotId = await _db.AddSpotAsync();
        var firstUser = await _db.AddUserAsync();
        var secondUser = await _db.AddUserAsync();

        await using var context = _db.CreateContext();
        var repository = new ReservationRepository(context);

        var cancelled = NewReservation(firstUser, spotId);
        await repository.AddAsync(cancelled, CancellationToken.None);
        await repository.SaveChangesAsync(CancellationToken.None);

        cancelled.Status = ReservationStatus.Cancelled;
        await repository.SaveChangesAsync(CancellationToken.None);

        await repository.AddAsync(NewReservation(secondUser, spotId), CancellationToken.None);
        await repository.SaveChangesAsync(CancellationToken.None); // לא זורק: ההזמנה הקודמת מבוטלת
    }

    // ---------- כל השכבות יחד, דרך ה-Service האמיתי ----------

    [PostgresFact]
    public async Task ParallelRequestsForTheSameSpotAndDay_ExactlyOneSucceeds_TheRestGetConflict()
    {
        var spotId = await _db.AddSpotAsync();
        var userIds = new List<int>();
        for (var i = 0; i < ParallelRequests; i++)
        {
            userIds.Add(await _db.AddUserAsync());
        }

        var results = await RunConcurrentlyAsync(userIds.Select(userId => (userId, spotId)).ToList(), Tomorrow);

        Assert.Equal(1, results.Count(r => r.IsSuccess));
        Assert.Equal(ParallelRequests - 1, results.Count(r => r.Error == ErrorType.Conflict));

        await using var check = _db.CreateContext();
        var active = await check.Reservations.CountAsync(r =>
            r.ParkingSpotId == spotId && r.Date == Tomorrow && r.Status == ReservationStatus.Active);
        Assert.Equal(1, active);
    }

    [PostgresFact]
    public async Task ParallelRequestsByTheSameUserForDifferentSpotsOnTheSameDay_ExactlyOneSucceeds()
    {
        var userId = await _db.AddUserAsync();
        var spotIds = new List<int>();
        for (var i = 0; i < ParallelRequests; i++)
        {
            spotIds.Add(await _db.AddSpotAsync());
        }

        var results = await RunConcurrentlyAsync(spotIds.Select(spotId => (userId, spotId)).ToList(), Tomorrow);

        Assert.Equal(1, results.Count(r => r.IsSuccess));
        Assert.Equal(ParallelRequests - 1, results.Count(r => r.Error == ErrorType.Conflict));
    }

    // ---------- עזרים ----------

    private static Reservation NewReservation(int userId, int spotId) => new()
    {
        UserId = userId,
        ParkingSpotId = spotId,
        Date = Tomorrow,
        Status = ReservationStatus.Active,
        CreatedAtUtc = DateTime.UtcNow
    };

    // כל בקשה מקבלת DbContext ו-Service משלה (כמו בקשת HTTP נפרדת), וכולן יוצאות יחד לדרך
    private async Task<List<Result<ReservationResponse>>> RunConcurrentlyAsync(
        List<(int UserId, int SpotId)> requests, DateOnly date)
    {
        var startGate = new TaskCompletionSource();

        var tasks = requests.Select(request => Task.Run(async () =>
        {
            await using var context = _db.CreateContext();
            var service = new ReservationService(
                new ReservationRepository(context),
                new ParkingSpotRepository(context),
                new UserRepository(context),
                Mapper,
                TimeProvider.System,
                Options.Create(new ReservationRulesOptions { MaxDaysAhead = 30 }),
                NullLogger<ReservationService>.Instance);

            await startGate.Task;

            return await service.CreateAsync(
                request.UserId,
                new CreateReservationRequest { ParkingSpotId = request.SpotId, Date = date },
                CancellationToken.None);
        })).ToList();

        startGate.SetResult();

        return (await Task.WhenAll(tasks)).ToList();
    }
}
