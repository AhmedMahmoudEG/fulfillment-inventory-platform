using Fulfillment.Application.Common.Exceptions;
using Fulfillment.Application.Users;
using Fulfillment.Application.Users.DTOs;
using Fulfillment.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;

namespace Fulfillment.Infrastructure.Users;

public class UserService : IUserService
{
    public static readonly HashSet<string> ApprovedRoles = new(StringComparer.Ordinal)
    {
        "Admin",
        "Manager",
        "Warehouse Operator",
        "Sales Agent"
    };

    private readonly UserManager<ApplicationUser> _userManager;

    public UserService(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public async Task<UserResponse> CreateUserAsync(CreateUserRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
        {
            throw new ValidationException("Email is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Password))
        {
            throw new ValidationException("Password is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Role) || !ApprovedRoles.Contains(request.Role))
        {
            throw new ValidationException("Invalid or unsupported role.");
        }

        var trimmedEmail = request.Email.Trim();

        var existingUser = await _userManager.FindByEmailAsync(trimmedEmail);
        if (existingUser != null)
        {
            throw new ConflictException("A user with this email already exists.");
        }

        var user = new ApplicationUser
        {
            UserName = trimmedEmail,
            Email = trimmedEmail
        };

        var createResult = await _userManager.CreateAsync(user, request.Password);
        if (!createResult.Succeeded)
        {
            var firstError = createResult.Errors.FirstOrDefault()?.Description ?? "Failed to create user.";
            throw new ValidationException(firstError);
        }

        // Atomicity Safeguard: Assign exactly-one role or rollback created user
        var roleResult = await _userManager.AddToRoleAsync(user, request.Role);
        if (!roleResult.Succeeded)
        {
            var deleteResult = await _userManager.DeleteAsync(user);
            if (!deleteResult.Succeeded)
            {
                // Exception will be handled server-side by GlobalExceptionHandler
            }
            throw new InvalidOperationException("Failed to assign role to user. User creation rolled back.");
        }

        return new UserResponse(user.Id, user.Email!, request.Role);
    }
}
