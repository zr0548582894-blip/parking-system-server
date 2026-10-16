using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ParkingSystem.Core.DTOs.ParkingSpots;
using ParkingSystem.Core.Interfaces.Services;

namespace ParkingSystem.API.Controllers;

[Route("api/features")]
[Authorize]
public class FeaturesController : ApiControllerBase
{
    private readonly IFeatureService _featureService;

    public FeaturesController(IFeatureService featureService)
    {
        _featureService = featureService;
    }

    /// <summary>רשימת המאפיינים של מקומות החניה (נגיש, מקורה, טעינה לרכב חשמלי).</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<FeatureResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetAll(CancellationToken ct) =>
        Ok(await _featureService.GetAllAsync(ct));
}
