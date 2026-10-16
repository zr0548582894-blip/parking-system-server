namespace ParkingSystem.Core.DTOs.ParkingSpots;

/// <summary>מקום חניה כפי שהוא מוצג ללקוח.</summary>
public class ParkingSpotResponse
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public int Floor { get; set; }
    public bool IsActive { get; set; }
    public List<FeatureResponse> Features { get; set; } = new();

    /// <summary>פנוי (true) או תפוס/לא פעיל (false) בתאריך שהתבקש. null כשלא התבקש תאריך.</summary>
    public bool? IsAvailable { get; set; }
}
