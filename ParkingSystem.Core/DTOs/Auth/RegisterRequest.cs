using System.ComponentModel.DataAnnotations;
using ParkingSystem.Core.Validation;

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
    // BCrypt מתעלם מכל מה שאחרי 72 בייטים (לא תווים), ולכן מגבילים לפי בייטים
    [StringLength(72, MinimumLength = 8)]
    [MaxUtf8Bytes(72)]
    public string Password { get; set; } = string.Empty;
}
