using AutoMapper;
using ParkingSystem.Core.Entities;
using ParkingSystem.Core.Enums;
using ParkingSystem.Service.Mapping;

namespace ParkingSystem.Tests.Mapping;

public class MappingProfileTests
{
    private readonly IMapper _mapper;
    private readonly MapperConfiguration _configuration;

    public MappingProfileTests()
    {
        _configuration = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>());
        _mapper = _configuration.CreateMapper();
    }

    [Fact]
    public void Configuration_IsValid_EveryDestinationMemberIsMappedOrIgnoredOnPurpose()
    {
        _configuration.AssertConfigurationIsValid();
    }

    [Fact]
    public void CreateSpotRequest_MapsToAnActiveSpot_WithATrimmedCode_AndServerOwnedFieldsLeftEmpty()
    {
        var spot = _mapper.Map<ParkingSpot>(
            new ParkingSystem.Core.DTOs.ParkingSpots.CreateParkingSpotRequest { Code = "  G-09 ", Floor = 2, FeatureIds = { 1, 2 } });

        Assert.Equal("G-09", spot.Code);
        Assert.Equal(2, spot.Floor);
        Assert.True(spot.IsActive);
        Assert.Equal(0, spot.Id);
        Assert.Equal(0u, spot.Version);
        Assert.Null(spot.LastReservedAtUtc);
        Assert.Empty(spot.Features);     // המאפיינים נטענים ב-Service לפי המזהים
    }

    [Fact]
    public void UpdateSpotRequest_UpdatesTheSimpleFieldsOfAnExistingSpot_AndKeepsItsIdentityAndVersion()
    {
        var existing = new ParkingSpot { Id = 4, Code = "OLD", Floor = 0, IsActive = true, Version = 77 };
        existing.Features.Add(new Feature { Id = 1, Name = "נגיש" });

        _mapper.Map(
            new ParkingSystem.Core.DTOs.ParkingSpots.UpdateParkingSpotRequest { Code = " NEW ", Floor = 3, IsActive = false },
            existing);

        Assert.Equal("NEW", existing.Code);
        Assert.Equal(3, existing.Floor);
        Assert.False(existing.IsActive);
        Assert.Equal(4, existing.Id);
        Assert.Equal(77u, existing.Version);
        Assert.Single(existing.Features);   // המאפיינים לא נמחקים במיפוי
    }

    [Fact]
    public void RegisterRequest_MapsToAnEmployee_WithANormalizedEmail_AndNoPasswordOnTheEntity()
    {
        var user = _mapper.Map<User>(new ParkingSystem.Core.DTOs.Auth.RegisterRequest
        {
            FullName = "  Dana Levi ",
            Email = "  Dana@Example.COM ",
            Password = "Secret123!"
        });

        Assert.Equal("dana@example.com", user.Email);
        Assert.Equal("Dana Levi", user.FullName);
        Assert.Equal(UserRole.Employee, user.Role);
        Assert.Equal(string.Empty, user.PasswordHash);   // ה-hash נקבע ב-Service בלבד
    }

    [Fact]
    public void CreateReservationRequest_MapsToAnActiveReservation_WithoutAnOwnerOrCreationTime()
    {
        var reservation = _mapper.Map<Reservation>(new ParkingSystem.Core.DTOs.Reservations.CreateReservationRequest
        {
            ParkingSpotId = 4,
            Date = new DateOnly(2030, 5, 1)
        });

        Assert.Equal(4, reservation.ParkingSpotId);
        Assert.Equal(new DateOnly(2030, 5, 1), reservation.Date);
        Assert.Equal(ReservationStatus.Active, reservation.Status);
        Assert.Equal(0, reservation.UserId);                     // נקבע מה-token, לא מהבקשה
        Assert.Equal(default, reservation.CreatedAtUtc);
    }

    [Fact]
    public void Reservation_MapsToResponseWithUserNameSpotAndFeatures()
    {
        var reservation = new Reservation
        {
            Id = 3,
            Date = new DateOnly(2030, 5, 1),
            Status = ReservationStatus.Active,
            UserId = 9,
            User = new User { Id = 9, FullName = "Dana Levi" },
            ParkingSpot = new ParkingSpot
            {
                Id = 4,
                Code = "G-01",
                Floor = 0,
                Features = { new Feature { Id = 1, Name = "נגיש" } }
            }
        };

        var response = _mapper.Map<ParkingSystem.Core.DTOs.Reservations.ReservationResponse>(reservation);

        Assert.Equal("Dana Levi", response.UserFullName);
        Assert.Equal("G-01", response.ParkingSpot.Code);
        Assert.Equal("נגיש", Assert.Single(response.ParkingSpot.Features).Name);
        Assert.Null(response.ParkingSpot.IsAvailable); // את הערך הזה קובע ה-Service
    }
}
