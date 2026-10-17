using System.ComponentModel.DataAnnotations;

namespace ParkingSystem.Core.DTOs.Auth;

/// <summary>פרטי ההרשמה של משתמש חדש. ההרשמה תמיד יוצרת עובד (Employee).</summary>
public class RegisterRequest
{
    [Required]
    [StringLength(100, MinimumLength = 2)]
    public string FullName { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    [StringLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required]
    // BCrypt מתעלם מכל מה שאחרי 72 בייטים, ולכן לא מקבלים סיסמה ארוכה יותר
    [StringLength(72, MinimumLength = 8)]
    public string Password { get; set; } = string.Empty;
}
