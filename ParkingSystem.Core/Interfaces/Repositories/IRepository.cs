namespace ParkingSystem.Core.Interfaces.Repositories;

public interface IRepository
{
    // המימוש ב-Data מתרגם התנגשויות של EF ושל PostgreSQL ל-ReservationConflictException
    Task SaveChangesAsync(CancellationToken ct);
}
