using System.ComponentModel.DataAnnotations;

namespace ParkingSystem.Core.DTOs.Common;

/// <summary>פרמטרי pagination: איזה עמוד להציג וכמה פריטים בעמוד.</summary>
public class PaginationQuery
{
    /// <summary>מספר העמוד, החל מ-1.</summary>
    [Range(1, int.MaxValue)]
    public int Page { get; set; } = 1;

    /// <summary>כמה פריטים בעמוד (1 עד 100).</summary>
    [Range(1, 100)]
    public int PageSize { get; set; } = 10;
}
