namespace ParkingSystem.Core.Options;

// משתמשי דמו שה-Seed יוצר. האימיילים אינם סוד, אבל הסיסמאות חייבות להגיע מהקונפיגורציה
// (User Secrets בפיתוח, משתנה סביבה ב-Render) ולעולם לא מקובץ בקוד או ב-Git.
// סיסמה ריקה = המשתמש הזה לא ייווצר
public class SeedUsersOptions
{
    public const string SectionName = "SeedUsers";

    public string AdminEmail { get; set; } = "admin@parking.local";
    public string AdminPassword { get; set; } = string.Empty;

    public string EmployeeEmail { get; set; } = "employee@parking.local";
    public string EmployeePassword { get; set; } = string.Empty;
}
