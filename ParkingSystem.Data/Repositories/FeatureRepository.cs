using Microsoft.EntityFrameworkCore;
using ParkingSystem.Core.Entities;
using ParkingSystem.Core.Interfaces.Repositories;
using ParkingSystem.Data.Context;

namespace ParkingSystem.Data.Repositories;

public class FeatureRepository : IFeatureRepository
{
    private readonly AppDbContext _context;

    public FeatureRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<Feature>> GetAllAsync(CancellationToken ct) =>
        await _context.Features.AsNoTracking().OrderBy(f => f.Name).ToListAsync(ct);

    // עם מעקב (בלי AsNoTracking), כי ה-Features האלה ישויכו למקום חניה שנשמר
    public async Task<IReadOnlyList<Feature>> GetByIdsAsync(IReadOnlyCollection<int> ids, CancellationToken ct)
    {
        var idList = ids.ToList();
        return await _context.Features.Where(f => idList.Contains(f.Id)).ToListAsync(ct);
    }
}
