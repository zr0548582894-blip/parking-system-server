namespace ParkingSystem.Tests.Integration;

// כמו [Fact], אבל מדלג על הבדיקה כש-PostgreSQL לא זמין
public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (!PostgresAvailability.IsAvailable)
        {
            Skip = "PostgreSQL is not reachable. Start it with 'docker compose up -d' " +
                   $"(or set {PostgresAvailability.EnvironmentVariable}) to run this test.";
        }
    }
}
