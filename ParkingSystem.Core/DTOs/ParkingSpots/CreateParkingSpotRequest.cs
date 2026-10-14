using System.ComponentModel.DataAnnotations;

namespace ParkingSystem.Core.DTOs.ParkingSpots;

public class CreateParkingSpotRequest
{
    [Required]
    [StringLength(20, MinimumLength = 1)]
    public string Code { get; set; } = string.Empty;

    [Range(-10, 100)]
    public int Floor { get; set; }

    public List<int> FeatureIds { get; set; } = new();
}
