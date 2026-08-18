using Fulfillment.Application.Auth.DTOs;

namespace Fulfillment.Application.Auth;

public interface IAuthService
{
    Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);
}
