using AutoMapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using ParkingSystem.Core.Common;
using ParkingSystem.Core.DTOs.Common;
using ParkingSystem.Core.DTOs.Reservations;
using ParkingSystem.Core.Entities;
using ParkingSystem.Core.Enums;
using ParkingSystem.Core.Exceptions;
using ParkingSystem.Core.Interfaces.Repositories;
using ParkingSystem.Core.Options;
using ParkingSystem.Service.Mapping;
using ParkingSystem.Service.Services;
using ParkingSystem.Tests.Helpers;

namespace ParkingSystem.Tests.Services;

public class ReservationServiceTests
{
    private static readonly DateTimeOffset Now = new(2030, 6, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2030, 6, 1);
    private const int UserId = 5;
    private const int SpotId = 4;

    private readonly Mock<IReservationRepository> _reservations = new();
    private readonly Mock<IParkingSpotRepository> _spots = new();
    private readonly Mock<ILogger<ReservationService>> _logger = new();
    private readonly ReservationService _service;

    public ReservationServiceTests()
    {
        // הטרנזקציה המדומה פשוט מריצה את הפעולה שה-Service מעביר לה
        _reservations
            .Setup(r => r.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<Result<int>>>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<Result<int>>>, CancellationToken>((action, token) => action(token));

        var mapper = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>()).CreateMapper();

        _service = new ReservationService(
            _reservations.Object, _spots.Object, mapper, new FixedTimeProvider(Now),
            Options.Create(new ReservationRulesOptions { MaxDaysAhead = 30 }), _logger.Object);
    }

    private static CancellationToken Ct => CancellationToken.None;

    private static CreateReservationRequest Request(DateOnly? date) =>
        new() { ParkingSpotId = SpotId, Date = date };

    private static ParkingSpot ActiveSpot() => new() { Id = SpotId, Code = "G-01", IsActive = true };

    private void SpotExists(ParkingSpot spot) =>
        _spots.Setup(s => s.GetByIdAsync(SpotId, It.IsAny<CancellationToken>())).ReturnsAsync(spot);

    // מכין מצב שבו ההזמנה תיווצר בהצלחה, ומחזיר את מה שנשמר
    private Reservation ArrangeSuccessfulCreate(DateOnly date, ParkingSpot spot)
    {
        var saved = new Reservation();
        SpotExists(spot);
        _reservations.Setup(r => r.AddAsync(It.IsAny<Reservation>(), It.IsAny<CancellationToken>()))
            .Callback<Reservation, CancellationToken>((reservation, _) =>
            {
                reservation.Id = 77;
                saved = reservation;
            })
            .Returns(Task.CompletedTask);
        _reservations.Setup(r => r.GetDetailsAsync(77, It.IsAny<CancellationToken>())).ReturnsAsync(() =>
        {
            saved.User = new User { Id = UserId, FullName = "Dana Levi" };
            saved.ParkingSpot = spot;
            return saved;
        });
        return saved;
    }

    // ---------- יצירה: תקינה ----------

    [Fact]
    public async Task Create_ValidRequest_SavesActiveReservationAndTouchesTheSpotToken()
    {
        var spot = ActiveSpot();
        var date = Today.AddDays(1);
        ArrangeSuccessfulCreate(date, spot);

        var result = await _service.CreateAsync(UserId, Request(date), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(77, result.Value!.Id);
        Assert.Equal("Dana Levi", result.Value.UserFullName);
        Assert.Equal("G-01", result.Value.ParkingSpot.Code);
        Assert.Equal(ReservationStatus.Active, result.Value.Status);
        // עדכון המקום הוא מה שמפעיל את בדיקת ה-xmin בשמירה
        Assert.Equal(Now.UtcDateTime, spot.LastReservedAtUtc);
        _reservations.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_LastAllowedDay_IsAccepted()
    {
        var date = Today.AddDays(30);
        ArrangeSuccessfulCreate(date, ActiveSpot());

        var result = await _service.CreateAsync(UserId, Request(date), Ct);

        Assert.True(result.IsSuccess);
    }

    // ---------- יצירה: נדחית לפני בסיס הנתונים ----------

    [Theory]
    [InlineData(-1)]   // אתמול
    [InlineData(31)]   // מעבר לחלון של 30 יום
    public async Task Create_DateOutsideAllowedWindow_ReturnsValidationWithoutTouchingTheDatabase(int daysFromToday)
    {
        var result = await _service.CreateAsync(UserId, Request(Today.AddDays(daysFromToday)), Ct);

        Assert.Equal(ErrorType.Validation, result.Error);
        _reservations.Verify(r => r.ExecuteInTransactionAsync(
            It.IsAny<Func<CancellationToken, Task<Result<int>>>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_MissingDate_ReturnsValidation()
    {
        var result = await _service.CreateAsync(UserId, Request(null), Ct);

        Assert.Equal(ErrorType.Validation, result.Error);
    }

    // ---------- יצירה: נדחית בגלל מצב הנתונים ----------

    [Fact]
    public async Task Create_SpotDoesNotExist_ReturnsNotFound()
    {
        var result = await _service.CreateAsync(UserId, Request(Today.AddDays(1)), Ct);

        Assert.Equal(ErrorType.NotFound, result.Error);
    }

    [Fact]
    public async Task Create_InactiveSpot_ReturnsValidation()
    {
        SpotExists(new ParkingSpot { Id = SpotId, IsActive = false });

        var result = await _service.CreateAsync(UserId, Request(Today.AddDays(1)), Ct);

        Assert.Equal(ErrorType.Validation, result.Error);
        _reservations.Verify(r => r.AddAsync(It.IsAny<Reservation>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_SpotAlreadyReserved_ReturnsConflictAndSavesNothing()
    {
        var date = Today.AddDays(1);
        SpotExists(ActiveSpot());
        _reservations.Setup(r => r.IsSpotReservedAsync(SpotId, date, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await _service.CreateAsync(UserId, Request(date), Ct);

        Assert.Equal(ErrorType.Conflict, result.Error);
        _reservations.Verify(r => r.AddAsync(It.IsAny<Reservation>(), It.IsAny<CancellationToken>()), Times.Never);
        _reservations.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_SpotAlreadyReserved_IsLoggedAsWarning()
    {
        var date = Today.AddDays(1);
        SpotExists(ActiveSpot());
        _reservations.Setup(r => r.IsSpotReservedAsync(SpotId, date, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        await _service.CreateAsync(UserId, Request(date), Ct);

        _logger.Verify(l => l.Log(
            LogLevel.Warning,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((state, _) => true),
            It.IsAny<Exception?>(),
            It.Is<Func<It.IsAnyType, Exception?, string>>((formatter, _) => true)), Times.Once);
    }

    [Fact]
    public async Task Create_UserAlreadyHasReservationThatDay_ReturnsConflict()
    {
        var date = Today.AddDays(1);
        SpotExists(ActiveSpot());
        _reservations.Setup(r => r.UserHasReservationAsync(UserId, date, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await _service.CreateAsync(UserId, Request(date), Ct);

        Assert.Equal(ErrorType.Conflict, result.Error);
        _reservations.Verify(r => r.AddAsync(It.IsAny<Reservation>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---------- התחרות ----------

    [Fact]
    public async Task Create_LostTheRace_ConflictOnSave_ReturnsConflictAndLogsWarning()
    {
        // שני המשתמשים עברו את בדיקת "פנוי?", ובשמירה בסיס הנתונים עצר את השני (xmin או אינדקס ייחודי)
        var date = Today.AddDays(1);
        SpotExists(ActiveSpot());
        _reservations.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ReservationConflictException("conflict"));

        var result = await _service.CreateAsync(UserId, Request(date), Ct);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorType.Conflict, result.Error);
        _reservations.Verify(r => r.GetDetailsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        _logger.Verify(l => l.Log(
            LogLevel.Warning,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((state, _) => true),
            It.IsAny<Exception?>(),
            It.Is<Func<It.IsAnyType, Exception?, string>>((formatter, _) => true)), Times.Once);
    }

    // ---------- ביטול ----------

    private Reservation ExistingReservation(int ownerId, ReservationStatus status = ReservationStatus.Active)
    {
        var reservation = new Reservation { Id = 10, UserId = ownerId, Status = status };
        _reservations.Setup(r => r.GetByIdAsync(10, It.IsAny<CancellationToken>())).ReturnsAsync(reservation);
        return reservation;
    }

    [Fact]
    public async Task Cancel_ByOwner_MarksCancelledAndSaves()
    {
        var reservation = ExistingReservation(ownerId: UserId);

        var result = await _service.CancelAsync(10, UserId, UserRole.Employee, Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(ReservationStatus.Cancelled, reservation.Status);
        _reservations.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Cancel_ByAdminForSomeoneElse_IsAllowed()
    {
        var reservation = ExistingReservation(ownerId: UserId);

        var result = await _service.CancelAsync(10, userId: 999, UserRole.Admin, Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(ReservationStatus.Cancelled, reservation.Status);
    }

    [Fact]
    public async Task Cancel_ByAnotherEmployee_ReturnsForbiddenAndChangesNothing()
    {
        var reservation = ExistingReservation(ownerId: UserId);

        var result = await _service.CancelAsync(10, userId: 999, UserRole.Employee, Ct);

        Assert.Equal(ErrorType.Forbidden, result.Error);
        Assert.Equal(ReservationStatus.Active, reservation.Status);
        _reservations.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Cancel_MissingReservation_ReturnsNotFound()
    {
        var result = await _service.CancelAsync(404, UserId, UserRole.Employee, Ct);

        Assert.Equal(ErrorType.NotFound, result.Error);
    }

    [Fact]
    public async Task Cancel_AlreadyCancelled_SucceedsWithoutSaving()
    {
        ExistingReservation(ownerId: UserId, ReservationStatus.Cancelled);

        var result = await _service.CancelAsync(10, UserId, UserRole.Employee, Ct);

        Assert.True(result.IsSuccess);
        _reservations.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---------- הזמנה אחת ----------

    private void DetailsExist(int ownerId) =>
        _reservations.Setup(r => r.GetDetailsAsync(10, It.IsAny<CancellationToken>())).ReturnsAsync(new Reservation
        {
            Id = 10,
            UserId = ownerId,
            Date = Today,
            User = new User { Id = ownerId, FullName = "Dana Levi" },
            ParkingSpot = new ParkingSpot { Code = "G-01" }
        });

    [Fact]
    public async Task GetById_ByOwner_ReturnsTheReservation()
    {
        DetailsExist(ownerId: UserId);

        var result = await _service.GetByIdAsync(10, UserId, UserRole.Employee, Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal("G-01", result.Value!.ParkingSpot.Code);
    }

    [Fact]
    public async Task GetById_ByAdminForSomeoneElse_IsAllowed()
    {
        DetailsExist(ownerId: UserId);

        var result = await _service.GetByIdAsync(10, userId: 999, UserRole.Admin, Ct);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task GetById_ByAnotherEmployee_ReturnsForbidden()
    {
        DetailsExist(ownerId: UserId);

        var result = await _service.GetByIdAsync(10, userId: 999, UserRole.Employee, Ct);

        Assert.Equal(ErrorType.Forbidden, result.Error);
    }

    [Fact]
    public async Task GetById_Missing_ReturnsNotFound()
    {
        var result = await _service.GetByIdAsync(404, UserId, UserRole.Employee, Ct);

        Assert.Equal(ErrorType.NotFound, result.Error);
    }

    // ---------- רשימות ----------

    private static PagedResult<Reservation> OnePage() => new()
    {
        Items = new List<Reservation>
        {
            new()
            {
                Id = 1,
                Date = Today,
                User = new User { FullName = "Dana Levi" },
                ParkingSpot = new ParkingSpot { Code = "G-01" }
            }
        },
        Page = 1, PageSize = 10, TotalCount = 1
    };

    [Fact]
    public async Task GetMine_QueriesOnlyTheGivenUsersReservations()
    {
        var query = new PaginationQuery();
        _reservations.Setup(r => r.GetPagedAsync(UserId, query, It.IsAny<CancellationToken>())).ReturnsAsync(OnePage());

        var result = await _service.GetMineAsync(UserId, query, Ct);

        Assert.Equal("G-01", Assert.Single(result.Value!.Items).ParkingSpot.Code);
        Assert.Equal(1, result.Value.TotalCount);
    }

    [Fact]
    public async Task GetAll_QueriesWithoutUserFilter()
    {
        var query = new PaginationQuery();
        _reservations.Setup(r => r.GetPagedAsync(null, query, It.IsAny<CancellationToken>())).ReturnsAsync(OnePage());

        var result = await _service.GetAllAsync(query, Ct);

        Assert.Equal("Dana Levi", Assert.Single(result.Value!.Items).UserFullName);
        _reservations.Verify(r => r.GetPagedAsync(null, query, It.IsAny<CancellationToken>()), Times.Once);
    }
}
