using ParkingSystem.Core.Enums;

namespace ParkingSystem.Core.Entities;

public class Reservation
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public int ParkingSpotId { get; set; }
    public ParkingSpot ParkingSpot { get; set; } = null!;

    // יום שלם אחד, ללא שעה
    public DateOnly Date { get; set; }
    public ReservationStatus Status { get; set; } = ReservationStatus.Active;
    public DateTime CreatedAtUtc { get; set; }
}
