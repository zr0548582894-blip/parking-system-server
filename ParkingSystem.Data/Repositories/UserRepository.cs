using Microsoft.EntityFrameworkCore;
using ParkingSystem.Core.Entities;
using ParkingSystem.Core.Interfaces.Repositories;
using ParkingSystem.Data.Context;

namespace ParkingSystem.Data.Repositories;

public class UserRepository : Repository, IUserRepository
{
    public UserRepository(AppDbContext context) : base(context)
    {
    }

    // קריאה בלבד, ולכן AsNoTracking
    public Task<User?> GetByIdAsync(int id, CancellationToken ct) =>
        Context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, ct);

    public Task<User?> GetByEmailAsync(string email, CancellationToken ct) =>
        Context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Email == email, ct);

    public Task<bool> EmailExistsAsync(string email, CancellationToken ct) =>
        Context.Users.AnyAsync(u => u.Email == email, ct);

    // רק מסמן להוספה. השמירה בפועל נעשית ב-SaveChangesAsync
    public async Task AddAsync(User user, CancellationToken ct) =>
        await Context.Users.AddAsync(user, ct);
}
