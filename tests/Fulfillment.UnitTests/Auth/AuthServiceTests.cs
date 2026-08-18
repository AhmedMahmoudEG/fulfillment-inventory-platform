using Fulfillment.Application.Auth.DTOs;
using Fulfillment.Infrastructure.Auth;
using Fulfillment.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Moq;

namespace Fulfillment.UnitTests.Auth;

public class AuthServiceTests
{
    private readonly Mock<UserManager<ApplicationUser>> _mockUserManager;
    private readonly JwtTokenGenerator _jwtTokenGenerator;
    private readonly AuthService _authService;

    public AuthServiceTests()
    {
        var store = new Mock<IUserStore<ApplicationUser>>();
        _mockUserManager = new Mock<UserManager<ApplicationUser>>(
            store.Object, null!, null!, null!, null!, null!, null!, null!, null!);

        var jwtOptions = Options.Create(new JwtSettings
        {
            Issuer = "TestIssuer",
            Audience = "TestAudience",
            SigningKey = "SuperSecretTestSigningKeyThatIsAtLeast256BitsLong!",
            ExpirationMinutes = 60
        });

        _jwtTokenGenerator = new JwtTokenGenerator(jwtOptions);
        _authService = new AuthService(_mockUserManager.Object, _jwtTokenGenerator);
    }

    [Fact]
    public async Task LoginAsync_ValidCredentials_ReturnsLoginResponse()
    {
        var user = new ApplicationUser { Id = "user-1", Email = "admin@example.com", UserName = "admin@example.com" };
        _mockUserManager.Setup(m => m.FindByEmailAsync("admin@example.com")).ReturnsAsync(user);
        _mockUserManager.Setup(m => m.CheckPasswordAsync(user, "Password123!")).ReturnsAsync(true);
        _mockUserManager.Setup(m => m.GetRolesAsync(user)).ReturnsAsync(new List<string> { "Admin" });

        var result = await _authService.LoginAsync(new LoginRequest("admin@example.com", "Password123!"));

        Assert.NotNull(result);
        Assert.NotNull(result.Token);
        Assert.True(result.ExpiresAt > DateTime.UtcNow);
        Assert.Equal("user-1", result.User.Id);
        Assert.Equal("admin@example.com", result.User.Email);
        Assert.Equal("Admin", result.User.Role);
    }

    [Fact]
    public async Task LoginAsync_NonexistentEmail_ThrowsUnauthorizedAccessExceptionWithGenericMessage()
    {
        _mockUserManager.Setup(m => m.FindByEmailAsync("unknown@example.com")).ReturnsAsync((ApplicationUser?)null);

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _authService.LoginAsync(new LoginRequest("unknown@example.com", "Password123!")));

        Assert.Equal("Invalid credentials.", ex.Message);
    }

    [Fact]
    public async Task LoginAsync_InvalidPassword_ThrowsUnauthorizedAccessExceptionWithGenericMessage()
    {
        var user = new ApplicationUser { Id = "user-1", Email = "admin@example.com" };
        _mockUserManager.Setup(m => m.FindByEmailAsync("admin@example.com")).ReturnsAsync(user);
        _mockUserManager.Setup(m => m.CheckPasswordAsync(user, "WrongPassword!")).ReturnsAsync(false);

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _authService.LoginAsync(new LoginRequest("admin@example.com", "WrongPassword!")));

        Assert.Equal("Invalid credentials.", ex.Message);
    }

    [Fact]
    public async Task LoginAsync_EmptyCredentials_ThrowsUnauthorizedAccessException()
    {
        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _authService.LoginAsync(new LoginRequest("   ", "")));

        Assert.Equal("Invalid credentials.", ex.Message);
    }
}
