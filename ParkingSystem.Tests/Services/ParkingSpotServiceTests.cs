using AutoMapper;
using Microsoft.Extensions.Logging;
using Moq;
using ParkingSystem.Core.Common;
using ParkingSystem.Core.DTOs.ParkingSpots;
using ParkingSystem.Core.Entities;
using ParkingSystem.Core.Exceptions;
using ParkingSystem.Core.Interfaces.Repositories;
using ParkingSystem.Service.Mapping;
using ParkingSystem.Service.Services;
using ParkingSystem.Tests.Helpers;

namespace ParkingSystem.Tests.Services;

public class ParkingSpotServiceTests
{
    private static readonly DateTimeOffset Now = new(2030, 6, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2030, 6, 1);

    private readonly Mock<IParkingSpotRepository> _spots = new();
    private readonly Mock<IFeatureRepository> _features = new();
    private readonly CapturingLogger<ParkingSpotService> _logger = new();
    private readonly ParkingSpotService _service;

    public ParkingSpotServiceTests()
    {
        var mapper = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>()).CreateMapper();
        _service = new ParkingSpotService(_spots.Object, _features.Object, mapper, new FixedTimeProvider(Now), _logger);
    }

    private void AssertSingleWarning() =>
        Assert.Equal(LogLevel.Warning, Assert.Single(_logger.Entries).Level);

    private static CancellationToken Ct => CancellationToken.None;

    private void FeaturesExist(params Feature[] features) =>
        _features.Setup(f => f.GetByIdsAsync(It.IsAny<IReadOnlyCollection<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<Feature>)features.ToList());

    // ---------- רשימה ----------

    [Fact]
    public async Task GetPaged_AvailableWithoutDate_ReturnsValidationAndDoesNotQuery()
    {
        var result = await _service.GetPagedAsync(new ParkingSpotQuery { Available = true }, Ct);

        Assert.Equal(ErrorType.Validation, result.Error);
        _spots.Verify(s => s.GetPagedAsync(It.IsAny<ParkingSpotQuery>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetPaged_WithDate_MarksOnlyActiveUnreservedSpotsAsAvailable()
    {
        var query = new ParkingSpotQuery { Date = new DateOnly(2030, 6, 5), Page = 1, PageSize = 10 };
        _spots.Setup(s => s.GetPagedAsync(query, It.IsAny<CancellationToken>())).ReturnsAsync(new PagedResult<ParkingSpot>
        {
            Items = new List<ParkingSpot>
            {
                new() { Id = 1, Code = "A", IsActive = true },
                new() { Id = 2, Code = "B", IsActive = true },
                new() { Id = 3, Code = "C", IsActive = false }
            },
            Page = 1, PageSize = 10, TotalCount = 3
        });
        _spots.Setup(s => s.GetReservedSpotIdsAsync(
                It.IsAny<IReadOnlyCollection<int>>(), query.Date!.Value, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlySet<int>)new HashSet<int> { 2 });

        var result = await _service.GetPagedAsync(query, Ct);

        var items = result.Value!.Items;
        Assert.True(items[0].IsAvailable);   // פעיל ופנוי
        Assert.False(items[1].IsAvailable);  // תפוס
        Assert.False(items[2].IsAvailable);  // לא פעיל
        Assert.Equal(3, result.Value.TotalCount);
    }

    [Fact]
    public async Task GetPaged_WithoutDate_LeavesAvailabilityEmptyAndSkipsReservedQuery()
    {
        var query = new ParkingSpotQuery();
        _spots.Setup(s => s.GetPagedAsync(query, It.IsAny<CancellationToken>())).ReturnsAsync(new PagedResult<ParkingSpot>
        {
            Items = new List<ParkingSpot> { new() { Id = 1, Code = "A", IsActive = true } },
            Page = 1, PageSize = 10, TotalCount = 1
        });

        var result = await _service.GetPagedAsync(query, Ct);

        Assert.Null(result.Value!.Items[0].IsAvailable);
        _spots.Verify(s => s.GetReservedSpotIdsAsync(
            It.IsAny<IReadOnlyCollection<int>>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---------- מקום בודד ----------

    [Fact]
    public async Task GetById_Existing_ReturnsSpot()
    {
        _spots.Setup(s => s.GetDetailsAsync(4, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ParkingSpot { Id = 4, Code = "G-01", Floor = 0 });

        var result = await _service.GetByIdAsync(4, Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal("G-01", result.Value!.Code);
    }

    [Fact]
    public async Task GetById_Missing_ReturnsNotFound()
    {
        var result = await _service.GetByIdAsync(99, Ct);

        Assert.Equal(ErrorType.NotFound, result.Error);
    }

    // ---------- יצירה ----------

    [Fact]
    public async Task Create_ValidRequest_AddsActiveSpotWithTrimmedCodeAndFeatures()
    {
        ParkingSpot? added = null;
        FeaturesExist(new Feature { Id = 1, Name = "נגיש" });
        _spots.Setup(s => s.AddAsync(It.IsAny<ParkingSpot>(), It.IsAny<CancellationToken>()))
            .Callback<ParkingSpot, CancellationToken>((spot, _) => added = spot).Returns(Task.CompletedTask);

        var result = await _service.CreateAsync(
            new CreateParkingSpotRequest { Code = "  G-09 ", Floor = 0, FeatureIds = { 1, 1 } }, Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal("G-09", added!.Code);
        Assert.True(added.IsActive);
        Assert.Single(added.Features);
        _spots.Verify(s => s.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_CodeAlreadyExists_ReturnsConflictAndAddsNothing()
    {
        _spots.Setup(s => s.CodeExistsAsync("G-09", null, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await _service.CreateAsync(new CreateParkingSpotRequest { Code = "G-09" }, Ct);

        Assert.Equal(ErrorType.Conflict, result.Error);
        _spots.Verify(s => s.AddAsync(It.IsAny<ParkingSpot>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_UnknownFeature_ReturnsValidation()
    {
        FeaturesExist(); // המזהה שנשלח לא קיים

        var result = await _service.CreateAsync(new CreateParkingSpotRequest { Code = "G-09", FeatureIds = { 42 } }, Ct);

        Assert.Equal(ErrorType.Validation, result.Error);
        _spots.Verify(s => s.AddAsync(It.IsAny<ParkingSpot>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_ParallelDuplicateCode_UniqueViolationOnSave_ReturnsConflict()
    {
        _spots.Setup(s => s.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DuplicateSpotCodeException("duplicate"));

        var result = await _service.CreateAsync(new CreateParkingSpotRequest { Code = "G-09" }, Ct);

        Assert.Equal(ErrorType.Conflict, result.Error);
    }

    // ---------- עדכון ----------

    [Fact]
    public async Task Update_ValidRequest_ChangesFieldsAndReplacesFeatures()
    {
        var spot = new ParkingSpot { Id = 4, Code = "OLD", Floor = 0, IsActive = true, Features = { new Feature { Id = 1 } } };
        _spots.Setup(s => s.GetByIdAsync(4, It.IsAny<CancellationToken>())).ReturnsAsync(spot);
        FeaturesExist(new Feature { Id = 2, Name = "מקורה" });

        var result = await _service.UpdateAsync(
            4, new UpdateParkingSpotRequest { Code = "NEW", Floor = 3, IsActive = false, FeatureIds = { 2 } }, Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal("NEW", spot.Code);
        Assert.Equal(3, spot.Floor);
        Assert.False(spot.IsActive);
        Assert.Equal(2, Assert.Single(spot.Features).Id);
        _spots.Verify(s => s.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Update_MissingSpot_ReturnsNotFound()
    {
        var result = await _service.UpdateAsync(99, new UpdateParkingSpotRequest { Code = "X" }, Ct);

        Assert.Equal(ErrorType.NotFound, result.Error);
    }

    [Fact]
    public async Task Update_CodeUsedByAnotherSpot_ReturnsConflictAndChecksExcludingItself()
    {
        _spots.Setup(s => s.GetByIdAsync(4, It.IsAny<CancellationToken>())).ReturnsAsync(new ParkingSpot { Id = 4, Code = "OLD" });
        _spots.Setup(s => s.CodeExistsAsync("TAKEN", 4, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await _service.UpdateAsync(4, new UpdateParkingSpotRequest { Code = "TAKEN" }, Ct);

        Assert.Equal(ErrorType.Conflict, result.Error);
        _spots.Verify(s => s.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_WithFutureReservations_ReturnsConflictAndDoesNotChangeSpot()
    {
        var spot = new ParkingSpot { Id = 4, Code = "OLD", IsActive = true };
        _spots.Setup(s => s.GetByIdAsync(4, It.IsAny<CancellationToken>())).ReturnsAsync(spot);
        _spots.Setup(s => s.HasFutureReservationsAsync(4, Today, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await _service.UpdateAsync(4, new UpdateParkingSpotRequest { Code = "NEW", IsActive = false }, Ct);

        Assert.Equal(ErrorType.Conflict, result.Error);
        Assert.Equal("OLD", spot.Code);
        Assert.True(spot.IsActive);
        _spots.Verify(s => s.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(typeof(ReservationConflictException))]
    [InlineData(typeof(DuplicateSpotCodeException))]
    public async Task Update_ConflictOnSave_ReturnsConflict(Type exceptionType)
    {
        _spots.Setup(s => s.GetByIdAsync(4, It.IsAny<CancellationToken>())).ReturnsAsync(new ParkingSpot { Id = 4, Code = "OLD" });
        _spots.Setup(s => s.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync((Exception)Activator.CreateInstance(exceptionType, "conflict", null)!);

        var result = await _service.UpdateAsync(4, new UpdateParkingSpotRequest { Code = "NEW" }, Ct);

        Assert.Equal(ErrorType.Conflict, result.Error);
    }

    // ---------- כל התנגשות על מקום נרשמת בלוג כ-Warning ----------

    [Fact]
    public async Task Create_CodeAlreadyExists_IsLoggedAsWarning()
    {
        _spots.Setup(s => s.CodeExistsAsync("G-09", null, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        await _service.CreateAsync(new CreateParkingSpotRequest { Code = "G-09" }, Ct);

        AssertSingleWarning();
    }

    [Fact]
    public async Task Create_ParallelDuplicateCode_IsLoggedAsWarning()
    {
        _spots.Setup(s => s.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DuplicateSpotCodeException("duplicate"));

        await _service.CreateAsync(new CreateParkingSpotRequest { Code = "G-09" }, Ct);

        AssertSingleWarning();
    }

    [Fact]
    public async Task Update_ChangedByAnotherRequest_IsLoggedAsWarning()
    {
        _spots.Setup(s => s.GetByIdAsync(4, It.IsAny<CancellationToken>())).ReturnsAsync(new ParkingSpot { Id = 4, Code = "OLD" });
        _spots.Setup(s => s.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ReservationConflictException("conflict"));

        await _service.UpdateAsync(4, new UpdateParkingSpotRequest { Code = "NEW" }, Ct);

        AssertSingleWarning();
    }

    [Fact]
    public async Task Delete_WithFutureReservations_IsLoggedAsWarning()
    {
        _spots.Setup(s => s.GetByIdAsync(4, It.IsAny<CancellationToken>())).ReturnsAsync(new ParkingSpot { Id = 4, IsActive = true });
        _spots.Setup(s => s.HasFutureReservationsAsync(4, Today, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        await _service.DeleteAsync(4, Ct);

        AssertSingleWarning();
    }

    // ---------- הסרה ("מחיקה") ----------

    [Fact]
    public async Task Delete_SpotWithoutFutureReservations_DeactivatesItAndKeepsTheRow()
    {
        var spot = new ParkingSpot { Id = 4, IsActive = true };
        _spots.Setup(s => s.GetByIdAsync(4, It.IsAny<CancellationToken>())).ReturnsAsync(spot);
        _spots.Setup(s => s.HasFutureReservationsAsync(4, Today, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await _service.DeleteAsync(4, Ct);

        Assert.True(result.IsSuccess);
        Assert.False(spot.IsActive);
        _spots.Verify(s => s.Remove(It.IsAny<ParkingSpot>()), Times.Never);
        _spots.Verify(s => s.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Delete_MissingSpot_ReturnsNotFound()
    {
        var result = await _service.DeleteAsync(99, Ct);

        Assert.Equal(ErrorType.NotFound, result.Error);
    }

    [Fact]
    public async Task Delete_SpotWithFutureReservations_ReturnsConflictAndStaysActive()
    {
        var spot = new ParkingSpot { Id = 4, IsActive = true };
        _spots.Setup(s => s.GetByIdAsync(4, It.IsAny<CancellationToken>())).ReturnsAsync(spot);
        _spots.Setup(s => s.HasFutureReservationsAsync(4, Today, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await _service.DeleteAsync(4, Ct);

        Assert.Equal(ErrorType.Conflict, result.Error);
        Assert.True(spot.IsActive);
        _spots.Verify(s => s.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Delete_AlreadyInactiveSpot_SucceedsWithoutSaving()
    {
        _spots.Setup(s => s.GetByIdAsync(4, It.IsAny<CancellationToken>())).ReturnsAsync(new ParkingSpot { Id = 4, IsActive = false });

        var result = await _service.DeleteAsync(4, Ct);

        Assert.True(result.IsSuccess);
        _spots.Verify(s => s.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
