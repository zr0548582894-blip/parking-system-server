using System.ComponentModel.DataAnnotations;

namespace ParkingSystem.Core.DTOs.ParkingSpots;

/// <summary>פרטי מקום חניה חדש: קוד ייחודי, קומה, ורשימת מזהי מאפיינים.</summary>
public class CreateParkingSpotRequest
{
    [Required]
    [StringLength(20, MinimumLength = 1)]
    public string Code { get; set; } = string.Empty;

    [Range(-10, 100)]
    public int Floor { get; set; }

    public List<int> FeatureIds { get; set; } = new();
}
