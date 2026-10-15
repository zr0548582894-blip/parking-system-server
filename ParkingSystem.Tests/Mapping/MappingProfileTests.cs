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
