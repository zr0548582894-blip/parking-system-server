using AutoMapper;
using Moq;
using ParkingSystem.Core.Entities;
using ParkingSystem.Core.Interfaces.Repositories;
using ParkingSystem.Service.Mapping;
using ParkingSystem.Service.Services;

namespace ParkingSystem.Tests.Services;

public class FeatureServiceTests
{
    private readonly Mock<IFeatureRepository> _features = new();
    private readonly FeatureService _service;

    public FeatureServiceTests()
    {
        var mapper = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>()).CreateMapper();
        _service = new FeatureService(_features.Object, mapper);
    }

    [Fact]
    public async Task GetAll_ReturnsFeaturesAsResponsesInRepositoryOrder()
    {
        _features.Setup(f => f.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Feature> { new() { Id = 1, Name = "נגיש" }, new() { Id = 2, Name = "מקורה" } });

        var result = await _service.GetAllAsync(CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Equal("נגיש", result[0].Name);
        Assert.Equal(2, result[1].Id);
    }

    [Fact]
    public async Task GetAll_NoFeatures_ReturnsEmptyList()
    {
        _features.Setup(f => f.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Feature>());

        var result = await _service.GetAllAsync(CancellationToken.None);

        Assert.Empty(result);
    }
}
