using ParkingSystem.Core.Entities;

namespace ParkingSystem.Core.Interfaces.Repositories;

public interface IUserRepository : IRepository
{
    Task<User?> GetByIdAsync(int id, CancellationToken ct);
    Task<User?> GetByEmailAsync(string email, CancellationToken ct);
    Task<bool> EmailExistsAsync(string email, CancellationToken ct);
    Task AddAsync(User user, CancellationToken ct);
}
