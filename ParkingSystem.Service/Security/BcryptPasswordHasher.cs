using ParkingSystem.Core.Interfaces.Security;

namespace ParkingSystem.Service.Security;

// הצפנת סיסמאות ב-BCrypt: האלגוריתם איטי בכוונה ומוסיף "מלח" אקראי לכל סיסמה,
// כך שאותה סיסמה נותנת hash שונה בכל פעם, ואי אפשר לשחזר את הסיסמה מה-hash
public class BcryptPasswordHasher : IPasswordHasher
{
    private const int WorkFactor = 11;

    public string Hash(string password) => BCrypt.Net.BCrypt.HashPassword(password, WorkFactor);

    public bool Verify(string password, string passwordHash)
    {
        // BCrypt זורק חריגה על hash ריק, ו-hash ריק פשוט לא מתאים לאף סיסמה
        if (string.IsNullOrEmpty(passwordHash))
        {
            return false;
        }

        try
        {
            return BCrypt.Net.BCrypt.Verify(password, passwordHash);
        }
        catch (BCrypt.Net.SaltParseException)
        {
            // ערך שאינו hash תקין של BCrypt הוא סיסמה שלא מתאימה, ולא תקלה
            return false;
        }
    }
}
