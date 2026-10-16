namespace ParkingSystem.Core.DTOs.ParkingSpots;

/// <summary>מאפיין של מקום חניה (למשל: נגיש, מקורה, טעינה לרכב חשמלי).</summary>
public class FeatureResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}
