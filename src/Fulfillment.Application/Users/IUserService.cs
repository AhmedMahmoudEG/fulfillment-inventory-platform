using Fulfillment.Application.Users.DTOs;

namespace Fulfillment.Application.Users;

public interface IUserService
{
    Task<UserResponse> CreateUserAsync(CreateUserRequest request, CancellationToken cancellationToken = default);
}
