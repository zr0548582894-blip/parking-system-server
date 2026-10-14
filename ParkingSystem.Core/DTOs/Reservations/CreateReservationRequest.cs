using System.ComponentModel.DataAnnotations;

namespace ParkingSystem.Core.DTOs.Reservations;

public class CreateReservationRequest
{
    [Range(1, int.MaxValue)]
    public int ParkingSpotId { get; set; }

    // nullable כדי ש-[Required] יזהה תאריך חסר (אחרת DateOnly מקבל ערך ברירת מחדל)
    [Required]
    public DateOnly? Date { get; set; }
}
