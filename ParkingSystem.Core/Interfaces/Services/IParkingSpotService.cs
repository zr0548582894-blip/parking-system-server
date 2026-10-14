using ParkingSystem.Core.Common;
using ParkingSystem.Core.DTOs.ParkingSpots;

namespace ParkingSystem.Core.Interfaces.Services;

public interface IParkingSpotService
{
    Task<Result<PagedResult<ParkingSpotResponse>>> GetPagedAsync(ParkingSpotQuery query, CancellationToken ct);
    Task<Result<ParkingSpotResponse>> GetByIdAsync(int id, CancellationToken ct);
    Task<Result<ParkingSpotResponse>> CreateAsync(CreateParkingSpotRequest request, CancellationToken ct);
    Task<Result<ParkingSpotResponse>> UpdateAsync(int id, UpdateParkingSpotRequest request, CancellationToken ct);
    Task<Result> DeleteAsync(int id, CancellationToken ct);
}
