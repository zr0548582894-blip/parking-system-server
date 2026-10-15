namespace ParkingSystem.Core.Common;

// כלל אחד לנרמול אימייל (ללא רווחים, באותיות קטנות), לשימוש ב-Service וב-Seed,
// כדי שהשוואת האימיילים והאילוץ הייחודי יתנהגו אותו דבר בכל מקום
public static class EmailNormalizer
{
    public static string Normalize(string email) => email.Trim().ToLowerInvariant();
}
