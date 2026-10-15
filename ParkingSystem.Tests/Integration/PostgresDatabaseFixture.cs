using Microsoft.EntityFrameworkCore;
using Npgsql;
using ParkingSystem.Core.Entities;
using ParkingSystem.Core.Enums;
using ParkingSystem.Data.Context;

namespace ParkingSystem.Tests.Integration;

// יוצר בסיס נתונים נפרד ולא קבוע לכל מחלקת בדיקות, מחיל עליו את ה-migrations האמיתיים,
// ומוחק אותו בסיום. כך הבדיקות לא נוגעות בנתוני הפיתוח
public sealed class PostgresDatabaseFixture : IAsyncLifetime
{
    private string _databaseName = string.Empty;

    public string ConnectionString { get; private set; } = string.Empty;

    public AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(ConnectionString).Options);

    public async Task InitializeAsync()
    {
        if (!PostgresAvailability.IsAvailable)
        {
            return;
        }

        // השם נבנה מ-GUID בלבד, ולכן בטוח לשלב אותו בפקודה
        _databaseName = $"parking_test_{Guid.NewGuid():N}";

        await ExecuteAdminAsync($"CREATE DATABASE \"{_databaseName}\"");

        ConnectionString = new NpgsqlConnectionStringBuilder(PostgresAvailability.AdminConnectionString)
        {
            Database = _databaseName
        }.ConnectionString;

        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (_databaseName.Length == 0)
        {
            return;
        }

        NpgsqlConnection.ClearAllPools();
        await ExecuteAdminAsync($"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)");
    }

    public async Task<int> AddUserAsync(string? email = null)
    {
        await using var context = CreateContext();
        var user = new User
        {
            Email = email ?? $"u{Guid.NewGuid():N}@test.local",
            FullName = "Test User",
            PasswordHash = "hash",
            Role = UserRole.Employee
        };

        context.Users.Add(user);
        await context.SaveChangesAsync();

        return user.Id;
    }

    public async Task<int> AddSpotAsync(bool isActive = true)
    {
        await using var context = CreateContext();
        var spot = new ParkingSpot { Code = $"S{Guid.NewGuid():N}"[..12], Floor = 1, IsActive = isActive };

        context.ParkingSpots.Add(spot);
        await context.SaveChangesAsync();

        return spot.Id;
    }

    private static async Task ExecuteAdminAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(PostgresAvailability.AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
