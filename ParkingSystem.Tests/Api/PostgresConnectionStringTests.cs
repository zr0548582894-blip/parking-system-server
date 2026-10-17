using Npgsql;
using ParkingSystem.Data.Context;

namespace ParkingSystem.Tests.Api;

// Render נותן את כתובת בסיס הנתונים כ-URL. הבדיקות מוכיחות שההמרה נכונה ושמחרוזת רגילה לא משתנה
public class PostgresConnectionStringTests
{
    [Fact]
    public void AUrlFromTheCloud_IsConvertedToTheNpgsqlFormat()
    {
        var result = PostgresConnectionString.Normalize("postgresql://app_user:s3cret@dpg-abc123-a:5433/parking_system");

        var parsed = new NpgsqlConnectionStringBuilder(result);
        Assert.Equal("dpg-abc123-a", parsed.Host);
        Assert.Equal(5433, parsed.Port);
        Assert.Equal("parking_system", parsed.Database);
        Assert.Equal("app_user", parsed.Username);
        Assert.Equal("s3cret", parsed.Password);
    }

    [Fact]
    public void BothUrlSchemesAreAccepted_AndThePortDefaultsTo5432()
    {
        foreach (var url in new[] { "postgres://u:p@host/db", "POSTGRESQL://u:p@host/db" })
        {
            var parsed = new NpgsqlConnectionStringBuilder(PostgresConnectionString.Normalize(url));

            Assert.Equal("host", parsed.Host);
            Assert.Equal(5432, parsed.Port);
        }
    }

    [Fact]
    public void SpecialCharactersInThePassword_AreDecoded()
    {
        var result = PostgresConnectionString.Normalize("postgres://user:p%40ss%3Aw%2Frd@host:5432/db");

        Assert.Equal("p@ss:w/rd", new NpgsqlConnectionStringBuilder(result).Password);
    }

    [Theory]
    [InlineData("require", SslMode.Require)]
    [InlineData("verify-full", SslMode.VerifyFull)]
    [InlineData("disable", SslMode.Disable)]
    public void TheSslModeQueryParameter_IsRespected(string value, SslMode expected)
    {
        var result = PostgresConnectionString.Normalize($"postgres://u:p@host/db?sslmode={value}");

        Assert.Equal(expected, new NpgsqlConnectionStringBuilder(result).SslMode);
    }

    [Fact]
    public void ARegularConnectionString_IsReturnedUnchanged()
    {
        const string regular = "Host=localhost;Port=5432;Database=parking_system;Username=postgres";

        Assert.Equal(regular, PostgresConnectionString.Normalize(regular));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AMissingConnectionString_FailsWithAClearMessage(string? value)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => PostgresConnectionString.Normalize(value));

        Assert.Contains("ConnectionStrings:Default", exception.Message);
    }
}
