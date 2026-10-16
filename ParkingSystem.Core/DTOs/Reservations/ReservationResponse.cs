using ParkingSystem.Core.DTOs.ParkingSpots;
using ParkingSystem.Core.Enums;

namespace ParkingSystem.Core.DTOs.Reservations;

/// <summary>הזמנה של מקום חניה ליום אחד, כולל פרטי המקום והמזמין.</summary>
public class ReservationResponse
{
    public int Id { get; set; }
    public DateOnly Date { get; set; }
    public ReservationStatus Status { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public int UserId { get; set; }
    public string UserFullName { get; set; } = string.Empty;
    public ParkingSpotResponse ParkingSpot { get; set; } = new();
}
