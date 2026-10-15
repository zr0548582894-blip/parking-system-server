using Npgsql;

namespace ParkingSystem.Tests.Integration;

// בודק פעם אחת אם אפשר להתחבר ל-PostgreSQL. אם לא (למשל Docker כבוי), בדיקות האינטגרציה מדלגות
// עם הודעה ברורה ולא מכשילות את dotnet test.
// ברירת המחדל היא בסיס הנתונים המקומי מ-docker-compose (בלי סיסמה). אפשר לשנות במשתנה הסביבה
internal static class PostgresAvailability
{
    public const string EnvironmentVariable = "PARKING_TEST_POSTGRES";

    public static string AdminConnectionString { get; } =
        Environment.GetEnvironmentVariable(EnvironmentVariable)
        ?? "Host=localhost;Port=5432;Username=postgres;Database=postgres;Timeout=3";

    public static bool IsAvailable { get; } = Probe();

    private static bool Probe()
    {
        try
        {
            using var connection = new NpgsqlConnection(AdminConnectionString);
            connection.Open();
            return true;
        }
        catch (Exception)
        {
            // כל כשל בהתחברות פירושו "לא זמין": זו בדיקת זמינות בלבד
            return false;
        }
    }
}
