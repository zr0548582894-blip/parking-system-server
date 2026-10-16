using AutoMapper;
using ParkingSystem.Core.Common;
using ParkingSystem.Core.DTOs.Auth;
using ParkingSystem.Core.DTOs.ParkingSpots;
using ParkingSystem.Core.DTOs.Reservations;
using ParkingSystem.Core.Entities;
using ParkingSystem.Core.Enums;

namespace ParkingSystem.Service.Mapping;

// הגדרות המיפוי בין ישויות ל-DTOs, בשני הכיוונים. שדות עם אותו שם מועתקים אוטומטית,
// וכאן מגדירים רק את מה ש-AutoMapper לא יכול לנחש לבד
public class MappingProfile : Profile
{
    public MappingProfile()
    {
        MapEntitiesToResponses();
        MapRequestsToEntities();
    }

    // ישות -> תשובה (מה שיוצא ללקוח)
    private void MapEntitiesToResponses()
    {
        CreateMap<Feature, FeatureResponse>();

        // רשימת ה-Features ממופה אוטומטית, כי קיים מיפוי Feature -> FeatureResponse
        CreateMap<ParkingSpot, ParkingSpotResponse>()
            // פנוי/תפוס תלוי בתאריך שהתבקש, ולכן ה-Service מחשב אותו ולא המיפוי
            .ForMember(response => response.IsAvailable, options => options.Ignore());

        CreateMap<Reservation, ReservationResponse>()
            .ForMember(response => response.UserFullName, options => options.MapFrom(reservation => reservation.User.FullName));
    }

    // בקשה -> ישות (מה שנכנס מהלקוח). שדות שהשרת קובע בעצמו (מזהה, גרסה, הרשאות, זמן יצירה)
    // מוגדרים כאן כ-Ignore, והשירות ממלא אותם אחרי המיפוי
    private void MapRequestsToEntities()
    {
        CreateMap<CreateParkingSpotRequest, ParkingSpot>()
            .ForMember(spot => spot.Code, options => options.MapFrom(request => request.Code.Trim()))
            .ForMember(spot => spot.IsActive, options => options.MapFrom(_ => true))
            .ForMember(spot => spot.Id, options => options.Ignore())
            .ForMember(spot => spot.LastReservedAtUtc, options => options.Ignore())
            .ForMember(spot => spot.Version, options => options.Ignore())
            .ForMember(spot => spot.Reservations, options => options.Ignore())
            // המאפיינים נטענים מבסיס הנתונים לפי המזהים, ולכן ה-Service מוסיף אותם
            .ForMember(spot => spot.Features, options => options.Ignore());

        // עדכון: אותם כללים, אבל IsActive מגיע מהבקשה
        CreateMap<UpdateParkingSpotRequest, ParkingSpot>()
            .IncludeBase<CreateParkingSpotRequest, ParkingSpot>()
            .ForMember(spot => spot.IsActive, options => options.MapFrom(request => request.IsActive));

        CreateMap<RegisterRequest, User>()
            .ForMember(user => user.Email, options => options.MapFrom(request => EmailNormalizer.Normalize(request.Email)))
            .ForMember(user => user.FullName, options => options.MapFrom(request => request.FullName.Trim()))
            // הרשמה עצמית יוצרת תמיד Employee. את התפקיד Admin אי אפשר לקבל דרך ה-API
            .ForMember(user => user.Role, options => options.MapFrom(_ => UserRole.Employee))
            // הסיסמה לעולם לא נשמרת כמו שהיא: ה-Service שומר hash אחרי המיפוי
            .ForMember(user => user.PasswordHash, options => options.Ignore())
            .ForMember(user => user.Id, options => options.Ignore())
            .ForMember(user => user.Reservations, options => options.Ignore());

        CreateMap<CreateReservationRequest, Reservation>()
            .ForMember(reservation => reservation.Date, options => options.MapFrom(request => request.Date!.Value))
            .ForMember(reservation => reservation.Status, options => options.MapFrom(_ => ReservationStatus.Active))
            // המזמין נלקח מה-token ולא מהבקשה, וזמן היצירה נקבע בשרת
            .ForMember(reservation => reservation.UserId, options => options.Ignore())
            .ForMember(reservation => reservation.CreatedAtUtc, options => options.Ignore())
            .ForMember(reservation => reservation.Id, options => options.Ignore())
            .ForMember(reservation => reservation.User, options => options.Ignore())
            .ForMember(reservation => reservation.ParkingSpot, options => options.Ignore());
    }
}
