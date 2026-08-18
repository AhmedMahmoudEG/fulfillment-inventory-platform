using Fulfillment.Application.Auth;
using Fulfillment.Application.Auth.DTOs;
using Fulfillment.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;

namespace Fulfillment.Infrastructure.Auth;

public class AuthService : IAuthService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly JwtTokenGenerator _jwtTokenGenerator;

    public AuthService(
        UserManager<ApplicationUser> userManager,
        JwtTokenGenerator jwtTokenGenerator)
    {
        _userManager = userManager;
        _jwtTokenGenerator = jwtTokenGenerator;
    }

    public async Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            throw new UnauthorizedAccessException("Invalid credentials.");
        }

        var user = await _userManager.FindByEmailAsync(request.Email.Trim());
        if (user == null)
        {
            throw new UnauthorizedAccessException("Invalid credentials.");
        }

        var isPasswordValid = await _userManager.CheckPasswordAsync(user, request.Password);
        if (!isPasswordValid)
        {
            throw new UnauthorizedAccessException("Invalid credentials.");
        }

        var roles = await _userManager.GetRolesAsync(user);
        var role = roles.FirstOrDefault() ?? string.Empty;

        var (token, expiresAt) = _jwtTokenGenerator.GenerateToken(user, role);

        var userDto = new UserDto(user.Id, user.Email ?? string.Empty, role);
        return new LoginResponse(token, expiresAt, userDto);
    }
}
