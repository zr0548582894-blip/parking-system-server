namespace ParkingSystem.Core.Exceptions;

// נזרקת משכבת Data כשהזמנה מתנגשת עם הזמנה אחרת (concurrency token או אינדקס ייחודי).
// כך ה-Service מכיר חריגה אחת מ-Core ולא תלוי ב-EF או ב-Npgsql
public class ReservationConflictException : Exception
{
    public ReservationConflictException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
