namespace ParkingSystem.Core.Exceptions;

// נזרקת משכבת Data כשהאילוץ הייחודי של האימייל נפגע, למשל בשתי הרשמות מקבילות עם אותו אימייל.
// ה-Service תופס אותה ומחזיר Conflict, בלי להכיר את EF או את Npgsql
public class DuplicateEmailException : Exception
{
    public DuplicateEmailException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
