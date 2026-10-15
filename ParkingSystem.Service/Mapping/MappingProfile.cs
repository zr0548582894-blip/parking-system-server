using AutoMapper;
using ParkingSystem.Core.DTOs.ParkingSpots;
using ParkingSystem.Core.DTOs.Reservations;
using ParkingSystem.Core.Entities;

namespace ParkingSystem.Service.Mapping;

// הגדרות המיפוי מישויות ל-DTOs. שדות עם אותו שם מועתקים אוטומטית,
// וכאן מגדירים רק את מה ש-AutoMapper לא יכול לנחש לבד
public class MappingProfile : Profile
{
    public MappingProfile()
    {
        CreateMap<Feature, FeatureResponse>();

        // רשימת ה-Features ממופה אוטומטית, כי קיים מיפוי Feature -> FeatureResponse
        CreateMap<ParkingSpot, ParkingSpotResponse>()
            // פנוי/תפוס תלוי בתאריך שהתבקש, ולכן ה-Service מחשב אותו ולא המיפוי
            .ForMember(response => response.IsAvailable, options => options.Ignore());

        CreateMap<Reservation, ReservationResponse>()
            .ForMember(response => response.UserFullName, options => options.MapFrom(reservation => reservation.User.FullName));
    }
}
