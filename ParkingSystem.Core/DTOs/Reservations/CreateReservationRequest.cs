using System.ComponentModel.DataAnnotations;

namespace ParkingSystem.Core.DTOs.Reservations;

/// <summary>בקשת הזמנה: איזה מקום ואיזה יום. זהות המזמין נלקחת מה-token ולא מכאן.</summary>
public class CreateReservationRequest
{
    /// <summary>מזהה מקום החניה (מתוך GET /api/parking-spots).</summary>
    [Range(1, int.MaxValue)]
    public int ParkingSpotId { get; set; }

    /// <summary>היום המבוקש (yyyy-MM-dd). לא בעבר, ועד מספר הימים המותר מראש (ברירת מחדל 30).</summary>
    // nullable כדי ש-[Required] יזהה תאריך חסר (אחרת DateOnly מקבל ערך ברירת מחדל)
    [Required]
    public DateOnly? Date { get; set; }
}
