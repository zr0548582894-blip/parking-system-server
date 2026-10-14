namespace ParkingSystem.Core.DTOs.ParkingSpots;

// עדכון מלא (PUT): אותם שדות כמו ביצירה, ובנוסף IsActive
public class UpdateParkingSpotRequest : CreateParkingSpotRequest
{
    public bool IsActive { get; set; } = true;
}
