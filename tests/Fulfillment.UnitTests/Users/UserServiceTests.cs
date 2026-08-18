using Fulfillment.Application.Common.Exceptions;
using Fulfillment.Application.Users.DTOs;
using Fulfillment.Infrastructure.Identity;
using Fulfillment.Infrastructure.Users;
using Microsoft.AspNetCore.Identity;
using Moq;

namespace Fulfillment.UnitTests.Users;

public class UserServiceTests
{
    private readonly Mock<UserManager<ApplicationUser>> _mockUserManager;
    private readonly UserService _userService;

    public UserServiceTests()
    {
        var store = new Mock<IUserStore<ApplicationUser>>();
        _mockUserManager = new Mock<UserManager<ApplicationUser>>(
            store.Object, null!, null!, null!, null!, null!, null!, null!, null!);

        _userService = new UserService(_mockUserManager.Object);
    }

    [Fact]
    public async Task CreateUserAsync_ValidRequest_CreatesUserAndAssignsSingleRole()
    {
        _mockUserManager.Setup(m => m.FindByEmailAsync("manager@example.com")).ReturnsAsync((ApplicationUser?)null);
        _mockUserManager.Setup(m => m.CreateAsync(It.IsAny<ApplicationUser>(), "Password123!"))
            .ReturnsAsync(IdentityResult.Success);
        _mockUserManager.Setup(m => m.AddToRoleAsync(It.IsAny<ApplicationUser>(), "Manager"))
            .ReturnsAsync(IdentityResult.Success);

        var response = await _userService.CreateUserAsync(new CreateUserRequest("manager@example.com", "Password123!", "Manager"));

        Assert.NotNull(response);
        Assert.Equal("manager@example.com", response.Email);
        Assert.Equal("Manager", response.Role);

        _mockUserManager.Verify(m => m.CreateAsync(It.Is<ApplicationUser>(u => u.Email == "manager@example.com"), "Password123!"), Times.Once);
        _mockUserManager.Verify(m => m.AddToRoleAsync(It.IsAny<ApplicationUser>(), "Manager"), Times.Once);
    }

    [Fact]
    public async Task CreateUserAsync_UnsupportedRole_ThrowsValidationException()
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            _userService.CreateUserAsync(new CreateUserRequest("user@example.com", "Password123!", "SuperAdmin")));
    }

    [Fact]
    public async Task CreateUserAsync_DuplicateEmail_ThrowsConflictException()
    {
        var existing = new ApplicationUser { Email = "existing@example.com" };
        _mockUserManager.Setup(m => m.FindByEmailAsync("existing@example.com")).ReturnsAsync(existing);

        await Assert.ThrowsAsync<ConflictException>(() =>
            _userService.CreateUserAsync(new CreateUserRequest("existing@example.com", "Password123!", "Manager")));
    }

    [Fact]
    public async Task CreateUserAsync_WeakPassword_ThrowsValidationException()
    {
        _mockUserManager.Setup(m => m.FindByEmailAsync("user@example.com")).ReturnsAsync((ApplicationUser?)null);
        _mockUserManager.Setup(m => m.CreateAsync(It.IsAny<ApplicationUser>(), "weak"))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "Passwords must be at least 8 characters." }));

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _userService.CreateUserAsync(new CreateUserRequest("user@example.com", "weak", "Manager")));

        Assert.Contains("Passwords must be at least 8 characters", ex.Message);
    }

    [Fact]
    public async Task CreateUserAsync_RoleAssignmentFails_RollsBackCreatedUser()
    {
        _mockUserManager.Setup(m => m.FindByEmailAsync("user@example.com")).ReturnsAsync((ApplicationUser?)null);
        _mockUserManager.Setup(m => m.CreateAsync(It.IsAny<ApplicationUser>(), "Password123!"))
            .ReturnsAsync(IdentityResult.Success);
        _mockUserManager.Setup(m => m.AddToRoleAsync(It.IsAny<ApplicationUser>(), "Manager"))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "Role assignment failed" }));
        _mockUserManager.Setup(m => m.DeleteAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync(IdentityResult.Success);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _userService.CreateUserAsync(new CreateUserRequest("user@example.com", "Password123!", "Manager")));

        // Verify rollback: user deletion was triggered!
        _mockUserManager.Verify(m => m.DeleteAsync(It.IsAny<ApplicationUser>()), Times.Once);
    }
}
