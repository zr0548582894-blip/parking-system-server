namespace ParkingSystem.Core.Exceptions;

// נזרקת משכבת Data כשהאילוץ הייחודי של קוד מקום החניה נפגע, למשל בשתי יצירות מקבילות עם אותו קוד.
// ה-Service תופס אותה ומחזיר Conflict, בלי להכיר את EF או את Npgsql
public class DuplicateSpotCodeException : Exception
{
    public DuplicateSpotCodeException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
