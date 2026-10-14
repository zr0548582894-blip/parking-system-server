using ParkingSystem.Core.Entities;

namespace ParkingSystem.Core.Interfaces.Repositories;

public interface IFeatureRepository
{
    Task<IReadOnlyList<Feature>> GetAllAsync(CancellationToken ct);

    // עם מעקב, כדי לשייך Features למקום
    Task<IReadOnlyList<Feature>> GetByIdsAsync(IReadOnlyCollection<int> ids, CancellationToken ct);
}
