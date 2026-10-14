namespace ParkingSystem.Core.DTOs.ParkingSpots;

public class ParkingSpotResponse
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public int Floor { get; set; }
    public bool IsActive { get; set; }
    public List<FeatureResponse> Features { get; set; } = new();

    // null כשלא התבקש תאריך; אחרת פנוי או תפוס באותו תאריך
    public bool? IsAvailable { get; set; }
}
