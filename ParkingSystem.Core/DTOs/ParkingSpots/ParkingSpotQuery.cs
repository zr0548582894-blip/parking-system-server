using System.ComponentModel.DataAnnotations;
using ParkingSystem.Core.DTOs.Common;
using ParkingSystem.Core.Enums;

namespace ParkingSystem.Core.DTOs.ParkingSpots;

// פרמטרי הסינון, המיון וה-pagination של רשימת המקומות (נקשרים מ-query string)
public class ParkingSpotQuery : PaginationQuery
{
    public int? Floor { get; set; }
    public bool? IsActive { get; set; }
    public int? FeatureId { get; set; }

    [StringLength(20)]
    public string? Search { get; set; }

    // התאריך שלפיו מחשבים פנוי/תפוס
    public DateOnly? Date { get; set; }
      
    // true = רק מקומות פנויים בתאריך Date (דורש Date; הבדיקה ב-Service)
    public bool Available { get; set; }

    public ParkingSpotSortField SortBy { get; set; } = ParkingSpotSortField.Code;
    public SortDirection SortDir { get; set; } = SortDirection.Asc;
}
