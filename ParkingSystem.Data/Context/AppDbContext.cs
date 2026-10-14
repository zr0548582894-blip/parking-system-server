using Microsoft.EntityFrameworkCore;
using ParkingSystem.Core.Entities;

namespace ParkingSystem.Data.Context;

public class AppDbContext : DbContext
{
    // ה-connection string מגיע מהקונפיגורציה דרך DbContextOptions (נרשם ב-Program.cs)
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<ParkingSpot> ParkingSpots => Set<ParkingSpot>();
    public DbSet<Reservation> Reservations => Set<Reservation>();
    public DbSet<Feature> Features => Set<Feature>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // טוען את כל הגדרות ה-Fluent API (IEntityTypeConfiguration) מהפרויקט הזה
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
