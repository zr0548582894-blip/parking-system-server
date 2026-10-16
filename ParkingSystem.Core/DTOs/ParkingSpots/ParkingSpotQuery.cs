using System.ComponentModel.DataAnnotations;
using ParkingSystem.Core.DTOs.Common;
using ParkingSystem.Core.Enums;

namespace ParkingSystem.Core.DTOs.ParkingSpots;

/// <summary>פרמטרי הסינון, המיון וה-pagination של רשימת מקומות החניה (נשלחים ב-query string).</summary>
public class ParkingSpotQuery : PaginationQuery
{
    /// <summary>רק מקומות בקומה הזו (שלילית = מרתף).</summary>
    public int? Floor { get; set; }

    /// <summary>רק מקומות פעילים (true) או לא פעילים (false). בלי ערך: כולם.</summary>
    public bool? IsActive { get; set; }

    /// <summary>רק מקומות שיש להם את המאפיין הזה (מזהה מתוך GET /api/features).</summary>
    public int? FeatureId { get; set; }

    /// <summary>חיפוש חלקי בקוד המקום, בלי רגישות לאותיות (עד 20 תווים).</summary>
    [StringLength(20)]
    public string? Search { get; set; }

    /// <summary>התאריך שלפיו כל מקום מסומן פנוי או תפוס (בפורמט yyyy-MM-dd).</summary>
    public DateOnly? Date { get; set; }

    /// <summary>true = רק מקומות פנויים בתאריך Date. דורש לציין Date.</summary>
    public bool Available { get; set; }

    /// <summary>לפי איזה שדה למיין: Code או Floor.</summary>
    public ParkingSpotSortField SortBy { get; set; } = ParkingSpotSortField.Code;

    /// <summary>כיוון המיון: Asc (עולה) או Desc (יורד).</summary>
    public SortDirection SortDir { get; set; } = SortDirection.Asc;
}
