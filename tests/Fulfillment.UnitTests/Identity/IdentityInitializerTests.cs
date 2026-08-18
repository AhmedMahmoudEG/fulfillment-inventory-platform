using Fulfillment.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace Fulfillment.UnitTests.Identity;

public class IdentityInitializerTests
{
    private readonly Mock<RoleManager<IdentityRole>> _mockRoleManager;
    private readonly Mock<UserManager<ApplicationUser>> _mockUserManager;
    private readonly Mock<IServiceProvider> _mockServiceProvider;
    private readonly Mock<IServiceProvider> _scopedProvider;

    public IdentityInitializerTests()
    {
        var roleStore = new Mock<IRoleStore<IdentityRole>>();
        _mockRoleManager = new Mock<RoleManager<IdentityRole>>(
            roleStore.Object, null!, null!, null!, null!);

        var userStore = new Mock<IUserStore<ApplicationUser>>();
        _mockUserManager = new Mock<UserManager<ApplicationUser>>(
            userStore.Object, null!, null!, null!, null!, null!, null!, null!, null!);

        var serviceScope = new Mock<IServiceScope>();
        var serviceScopeFactory = new Mock<IServiceScopeFactory>();

        _mockServiceProvider = new Mock<IServiceProvider>();
        _mockServiceProvider.Setup(s => s.GetService(typeof(IServiceScopeFactory))).Returns(serviceScopeFactory.Object);
        serviceScopeFactory.Setup(f => f.CreateScope()).Returns(serviceScope.Object);

        _scopedProvider = new Mock<IServiceProvider>();
        serviceScope.Setup(s => s.ServiceProvider).Returns(_scopedProvider.Object);

        _scopedProvider.Setup(s => s.GetService(typeof(RoleManager<IdentityRole>))).Returns(_mockRoleManager.Object);
        _scopedProvider.Setup(s => s.GetService(typeof(UserManager<ApplicationUser>))).Returns(_mockUserManager.Object);
        _scopedProvider.Setup(s => s.GetService(typeof(ILoggerFactory))).Returns(new LoggerFactory());
    }

    [Fact]
    public async Task InitializeAsync_CreatesMissingRoles_Idempotently()
    {
        var configuration = new ConfigurationBuilder().Build();
        _scopedProvider.Setup(s => s.GetService(typeof(IConfiguration))).Returns(configuration);

        _mockRoleManager.Setup(r => r.RoleExistsAsync(It.IsAny<string>())).ReturnsAsync(false);
        _mockRoleManager.Setup(r => r.CreateAsync(It.IsAny<IdentityRole>())).ReturnsAsync(IdentityResult.Success);

        // Simulate 1 user existing to bypass bootstrap admin creation
        var usersQuery = new List<ApplicationUser> { new ApplicationUser { Id = "1" } }.AsAsyncQueryable();
        _mockUserManager.Setup(u => u.Users).Returns(usersQuery);

        await IdentityInitializer.InitializeAsync(_mockServiceProvider.Object);

        _mockRoleManager.Verify(r => r.CreateAsync(It.Is<IdentityRole>(role => role.Name == "Admin")), Times.Once);
        _mockRoleManager.Verify(r => r.CreateAsync(It.Is<IdentityRole>(role => role.Name == "Manager")), Times.Once);
        _mockRoleManager.Verify(r => r.CreateAsync(It.Is<IdentityRole>(role => role.Name == "Warehouse Operator")), Times.Once);
        _mockRoleManager.Verify(r => r.CreateAsync(It.Is<IdentityRole>(role => role.Name == "Sales Agent")), Times.Once);
    }

    [Fact]
    public async Task InitializeAsync_ExistingRoles_AreNotDuplicated()
    {
        var configuration = new ConfigurationBuilder().Build();
        _scopedProvider.Setup(s => s.GetService(typeof(IConfiguration))).Returns(configuration);

        _mockRoleManager.Setup(r => r.RoleExistsAsync(It.IsAny<string>())).ReturnsAsync(true);

        var usersQuery = new List<ApplicationUser> { new ApplicationUser { Id = "1" } }.AsAsyncQueryable();
        _mockUserManager.Setup(u => u.Users).Returns(usersQuery);

        await IdentityInitializer.InitializeAsync(_mockServiceProvider.Object);

        _mockRoleManager.Verify(r => r.CreateAsync(It.IsAny<IdentityRole>()), Times.Never);
    }

    [Fact]
    public async Task InitializeAsync_ZeroUsersWithEnvCredentials_CreatesBootstrapAdmin()
    {
        var inMemorySettings = new Dictionary<string, string?>
        {
            {"SEED_ADMIN_EMAIL", "bootstrap@example.com"},
            {"SEED_ADMIN_PASSWORD", "Secret123!"}
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(inMemorySettings).Build();
        _scopedProvider.Setup(s => s.GetService(typeof(IConfiguration))).Returns(configuration);

        _mockRoleManager.Setup(r => r.RoleExistsAsync(It.IsAny<string>())).ReturnsAsync(true);

        var emptyUsersQuery = new List<ApplicationUser>().AsAsyncQueryable();
        _mockUserManager.Setup(u => u.Users).Returns(emptyUsersQuery);
        _mockUserManager.Setup(u => u.CreateAsync(It.IsAny<ApplicationUser>(), "Secret123!"))
            .ReturnsAsync(IdentityResult.Success);
        _mockUserManager.Setup(u => u.AddToRoleAsync(It.IsAny<ApplicationUser>(), "Admin"))
            .ReturnsAsync(IdentityResult.Success);

        await IdentityInitializer.InitializeAsync(_mockServiceProvider.Object);

        _mockUserManager.Verify(u => u.CreateAsync(It.Is<ApplicationUser>(user => user.Email == "bootstrap@example.com"), "Secret123!"), Times.Once);
        _mockUserManager.Verify(u => u.AddToRoleAsync(It.IsAny<ApplicationUser>(), "Admin"), Times.Once);
    }

    [Fact]
    public async Task InitializeAsync_ZeroUsersWithEnvCredentials_RoleAssignmentFails_RollsBackBootstrapUser()
    {
        var inMemorySettings = new Dictionary<string, string?>
        {
            {"SEED_ADMIN_EMAIL", "bootstrap@example.com"},
            {"SEED_ADMIN_PASSWORD", "Secret123!"}
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(inMemorySettings).Build();
        _scopedProvider.Setup(s => s.GetService(typeof(IConfiguration))).Returns(configuration);

        _mockRoleManager.Setup(r => r.RoleExistsAsync(It.IsAny<string>())).ReturnsAsync(true);

        var emptyUsersQuery = new List<ApplicationUser>().AsAsyncQueryable();
        _mockUserManager.Setup(u => u.Users).Returns(emptyUsersQuery);
        _mockUserManager.Setup(u => u.CreateAsync(It.IsAny<ApplicationUser>(), "Secret123!"))
            .ReturnsAsync(IdentityResult.Success);
        _mockUserManager.Setup(u => u.AddToRoleAsync(It.IsAny<ApplicationUser>(), "Admin"))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "Role error" }));
        _mockUserManager.Setup(u => u.DeleteAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync(IdentityResult.Success);

        await IdentityInitializer.InitializeAsync(_mockServiceProvider.Object);

        // Verify rollback: DeleteAsync was called when AddToRoleAsync failed!
        _mockUserManager.Verify(u => u.DeleteAsync(It.IsAny<ApplicationUser>()), Times.Once);
    }
}

public static class TestAsyncEnumerableExtensions
{
    public static IQueryable<T> AsAsyncQueryable<T>(this IEnumerable<T> source)
    {
        return new TestAsyncEnumerable<T>(source);
    }
}

internal class TestAsyncEnumerable<T> : EnumerableQuery<T>, IAsyncEnumerable<T>, IQueryable<T>
{
    public TestAsyncEnumerable(IEnumerable<T> enumerable)
        : base(enumerable)
    {
    }

    public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        return new TestAsyncEnumerator<T>(this.AsEnumerable().GetEnumerator());
    }
}

internal class TestAsyncEnumerator<T> : IAsyncEnumerator<T>
{
    private readonly IEnumerator<T> _inner;

    public TestAsyncEnumerator(IEnumerator<T> inner)
    {
        _inner = inner;
    }

    public T Current => _inner.Current;

    public ValueTask<bool> MoveNextAsync()
    {
        return new ValueTask<bool>(_inner.MoveNext());
    }

    public ValueTask DisposeAsync()
    {
        _inner.Dispose();
        return new ValueTask();
    }
}
