using AutoMapper;
using ParkingSystem.Core.DTOs.ParkingSpots;
using ParkingSystem.Core.Interfaces.Repositories;
using ParkingSystem.Core.Interfaces.Services;

namespace ParkingSystem.Service.Services;

public class FeatureService : IFeatureService
{
    private readonly IFeatureRepository _features;
    private readonly IMapper _mapper;

    public FeatureService(IFeatureRepository features, IMapper mapper)
    {
        _features = features;
        _mapper = mapper;
    }

    public async Task<IReadOnlyList<FeatureResponse>> GetAllAsync(CancellationToken ct)
    {
        var features = await _features.GetAllAsync(ct);

        return _mapper.Map<List<FeatureResponse>>(features);
    }
}
