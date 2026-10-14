using ParkingSystem.Core.Common;
using ParkingSystem.Core.DTOs.Auth;

namespace ParkingSystem.Core.Interfaces.Services;

public interface IAuthService
{
    Task<Result<AuthResponse>> RegisterAsync(RegisterRequest request, CancellationToken ct);
    Task<Result<AuthResponse>> LoginAsync(LoginRequest request, CancellationToken ct);
}
