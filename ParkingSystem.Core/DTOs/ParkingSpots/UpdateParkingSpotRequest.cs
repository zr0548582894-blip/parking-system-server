namespace ParkingSystem.Core.DTOs.ParkingSpots;

/// <summary>עדכון מלא (PUT): אותם שדות כמו ביצירה, ובנוסף האם המקום פעיל.</summary>
public class UpdateParkingSpotRequest : CreateParkingSpotRequest
{
    public bool IsActive { get; set; } = true;
}
