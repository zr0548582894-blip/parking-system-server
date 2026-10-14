using ParkingSystem.Core.DTOs.ParkingSpots;

namespace ParkingSystem.Core.Interfaces.Services;

public interface IFeatureService
{
    Task<IReadOnlyList<FeatureResponse>> GetAllAsync(CancellationToken ct);
}
