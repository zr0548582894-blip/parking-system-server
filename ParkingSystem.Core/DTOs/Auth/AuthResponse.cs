using ParkingSystem.Core.Enums;

namespace ParkingSystem.Core.DTOs.Auth;

/// <summary>תשובת הרשמה או התחברות: ה-token לשליחה בכל בקשה מוגנת, ופרטי המשתמש.</summary>
public class AuthResponse
{
    public string Token { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public int UserId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public UserRole Role { get; set; }
}

