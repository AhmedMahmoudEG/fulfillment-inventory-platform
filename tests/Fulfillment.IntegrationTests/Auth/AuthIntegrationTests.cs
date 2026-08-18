using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Fulfillment.Application.Auth.DTOs;
using Fulfillment.Application.Categories.DTOs;
using Fulfillment.Application.Inventory.DTOs;
using Fulfillment.Application.Products.DTOs;
using Fulfillment.Application.Users.DTOs;
using Fulfillment.Application.Warehouses.DTOs;
using Fulfillment.Infrastructure.Identity;
using Fulfillment.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Fulfillment.IntegrationTests.Auth;

[Collection("IntegrationTests")]
public class AuthIntegrationTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _factory;

    public AuthIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Jwt:SigningKey", "TestSigningKeyAtLeast256BitsLongForSecurity12345!");
        });
    }

    private ApplicationDbContext GetDbContext(IServiceScope scope)
    {
        return scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    }

    private async Task ClearAllDataAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = GetDbContext(scope);
        await db.Database.ExecuteSqlRawAsync(@"
            DELETE FROM InventoryAdjustments;
            DELETE FROM Inventories;
            DELETE FROM Products;
            DELETE FROM Categories;
            DELETE FROM Warehouses;
            DELETE FROM AspNetUserRoles;
            DELETE FROM AspNetUserClaims;
            DELETE FROM AspNetUsers;
        ");
    }

    public async Task InitializeAsync()
    {
        await ClearAllDataAsync();
    }

    public async Task DisposeAsync()
    {
        await ClearAllDataAsync();
    }

    private async Task<(HttpClient Client, UserDto User, string RawToken)> CreateUserAndGetClientAsync(string email, string password, string role)
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        if (!await roleManager.RoleExistsAsync(role))
        {
            await roleManager.CreateAsync(new IdentityRole(role));
        }

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true
        };

        var createRes = await userManager.CreateAsync(user, password);
        if (!createRes.Succeeded)
        {
            throw new InvalidOperationException($"Failed to seed test user: {createRes.Errors.FirstOrDefault()?.Description}");
        }

        var roleRes = await userManager.AddToRoleAsync(user, role);
        if (!roleRes.Succeeded)
        {
            await userManager.DeleteAsync(user);
            throw new InvalidOperationException($"Failed to assign test role: {roleRes.Errors.FirstOrDefault()?.Description}");
        }

        var client = _factory.CreateClient();
        var loginResp = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));
        Assert.Equal(HttpStatusCode.OK, loginResp.StatusCode);

        var loginResult = await loginResp.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(loginResult);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginResult.Token);

        return (client, loginResult.User, loginResult.Token);
    }

    [Fact]
    public async Task Login_ValidCredentials_Returns200OKWithJwtTokenAndUserDto()
    {
        var email = $"login_{Guid.NewGuid():N}@example.com";
        var (client, user, token) = await CreateUserAndGetClientAsync(email, "Password123!", "Admin");

        Assert.NotNull(token);
        Assert.Equal(email, user.Email);
        Assert.Equal("Admin", user.Role);

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);

        Assert.Equal(user.Id, jwt.Claims.First(c => c.Type == JwtRegisteredClaimNames.Sub || c.Type == System.Security.Claims.ClaimTypes.NameIdentifier).Value);
        Assert.Equal(email, jwt.Claims.First(c => c.Type == JwtRegisteredClaimNames.Email || c.Type == System.Security.Claims.ClaimTypes.Email).Value);
        Assert.Equal("Admin", jwt.Claims.First(c => c.Type == System.Security.Claims.ClaimTypes.Role || c.Type == "role").Value);

        // Verify jti claim is NOT present
        Assert.DoesNotContain(jwt.Claims, c => c.Type == JwtRegisteredClaimNames.Jti || c.Type == "jti");
    }

    [Fact]
    public async Task Login_NonexistentUser_Returns401UnauthorizedWithoutLeakingUserExistence()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("nonexistent@example.com", "Password123!"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_WrongPassword_Returns401Unauthorized()
    {
        var email = $"wrongpass_{Guid.NewGuid():N}@example.com";
        await CreateUserAndGetClientAsync(email, "CorrectPassword123!", "Manager");

        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "WrongPassword123!"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateUser_ByAdmin_Returns201Created()
    {
        var adminEmail = $"admin_{Guid.NewGuid():N}@example.com";
        var (adminClient, _, _) = await CreateUserAndGetClientAsync(adminEmail, "AdminPassword123!", "Admin");

        var newUserEmail = $"newuser_{Guid.NewGuid():N}@example.com";
        var request = new CreateUserRequest(newUserEmail, "UserPassword123!", "Warehouse Operator");

        var response = await adminClient.PostAsJsonAsync("/api/users", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var createdUser = await response.Content.ReadFromJsonAsync<UserResponse>();

        Assert.NotNull(createdUser);
        Assert.Equal(newUserEmail, createdUser.Email);
        Assert.Equal("Warehouse Operator", createdUser.Role);
    }

    [Fact]
    public async Task CreateUser_ByManager_Returns403Forbidden()
    {
        var managerEmail = $"mgr_{Guid.NewGuid():N}@example.com";
        var (mgrClient, _, _) = await CreateUserAndGetClientAsync(managerEmail, "ManagerPassword123!", "Manager");

        var request = new CreateUserRequest($"new_{Guid.NewGuid():N}@example.com", "UserPassword123!", "Sales Agent");
        var response = await mgrClient.PostAsJsonAsync("/api/users", request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateUser_BySalesAgent_Returns403Forbidden()
    {
        var salesEmail = $"sales_{Guid.NewGuid():N}@example.com";
        var (salesClient, _, _) = await CreateUserAndGetClientAsync(salesEmail, "SalesPassword123!", "Sales Agent");

        var request = new CreateUserRequest($"new_{Guid.NewGuid():N}@example.com", "UserPassword123!", "Warehouse Operator");
        var response = await salesClient.PostAsJsonAsync("/api/users", request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateUser_Unauthenticated_Returns401Unauthorized()
    {
        var client = _factory.CreateClient();
        var request = new CreateUserRequest($"new_{Guid.NewGuid():N}@example.com", "UserPassword123!", "Admin");

        var response = await client.PostAsJsonAsync("/api/users", request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateUser_DuplicateEmail_Returns409Conflict()
    {
        var adminEmail = $"admin_{Guid.NewGuid():N}@example.com";
        var (adminClient, _, _) = await CreateUserAndGetClientAsync(adminEmail, "AdminPassword123!", "Admin");

        var existingEmail = $"exist_{Guid.NewGuid():N}@example.com";
        await adminClient.PostAsJsonAsync("/api/users", new CreateUserRequest(existingEmail, "UserPassword123!", "Manager"));

        var duplicateResponse = await adminClient.PostAsJsonAsync("/api/users", new CreateUserRequest(existingEmail, "UserPassword123!", "Sales Agent"));
        Assert.Equal(HttpStatusCode.Conflict, duplicateResponse.StatusCode);
    }

    [Fact]
    public async Task AuthorizationMatrix_InventoryView_AllRolesAllowed()
    {
        var admin = await CreateUserAndGetClientAsync($"a_{Guid.NewGuid():N}@ex.com", "Pass123!", "Admin");
        var mgr = await CreateUserAndGetClientAsync($"m_{Guid.NewGuid():N}@ex.com", "Pass123!", "Manager");
        var op = await CreateUserAndGetClientAsync($"o_{Guid.NewGuid():N}@ex.com", "Pass123!", "Warehouse Operator");
        var sales = await CreateUserAndGetClientAsync($"s_{Guid.NewGuid():N}@ex.com", "Pass123!", "Sales Agent");

        Assert.Equal(HttpStatusCode.OK, (await admin.Client.GetAsync("/api/inventory")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await mgr.Client.GetAsync("/api/inventory")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await op.Client.GetAsync("/api/inventory")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await sales.Client.GetAsync("/api/inventory")).StatusCode);
    }

    [Fact]
    public async Task AuthorizationMatrix_InventoryAdjust_AdminManagerOperatorAllowed_SalesAgentForbidden()
    {
        var admin = await CreateUserAndGetClientAsync($"a_{Guid.NewGuid():N}@ex.com", "Pass123!", "Admin");
        var mgr = await CreateUserAndGetClientAsync($"m_{Guid.NewGuid():N}@ex.com", "Pass123!", "Manager");
        var op = await CreateUserAndGetClientAsync($"o_{Guid.NewGuid():N}@ex.com", "Pass123!", "Warehouse Operator");
        var sales = await CreateUserAndGetClientAsync($"s_{Guid.NewGuid():N}@ex.com", "Pass123!", "Sales Agent");

        // Seed Category, Warehouse, Product
        var catName = $"Cat_{Guid.NewGuid():N}";
        var catResp = await admin.Client.PostAsJsonAsync("/api/categories", new CreateCategoryRequest(catName));
        var cat = await catResp.Content.ReadFromJsonAsync<CategoryResponse>();

        var whName = $"Wh_{Guid.NewGuid():N}";
        var whResp = await admin.Client.PostAsJsonAsync("/api/warehouses", new CreateWarehouseRequest(whName, "Addr", "City"));
        var wh = await whResp.Content.ReadFromJsonAsync<WarehouseResponse>();

        var sku = $"SKU_{Guid.NewGuid():N}";
        var prodResp = await admin.Client.PostAsJsonAsync("/api/products", new CreateProductRequest("Laptop", "Desc", sku, 500m, cat!.Id, wh!.Id));
        var product = await prodResp.Content.ReadFromJsonAsync<ProductResponse>();

        // Admin adjust -> 200 OK
        var resAdmin = await admin.Client.PostAsJsonAsync($"/api/inventory/{product!.Id}/adjust", new AdjustStockRequest(10, "By Admin"));
        Assert.Equal(HttpStatusCode.OK, resAdmin.StatusCode);

        // Manager adjust -> 200 OK
        var resMgr = await mgr.Client.PostAsJsonAsync($"/api/inventory/{product.Id}/adjust", new AdjustStockRequest(20, "By Manager"));
        Assert.Equal(HttpStatusCode.OK, resMgr.StatusCode);

        // Operator adjust -> 200 OK
        var resOp = await op.Client.PostAsJsonAsync($"/api/inventory/{product.Id}/adjust", new AdjustStockRequest(30, "By Operator"));
        Assert.Equal(HttpStatusCode.OK, resOp.StatusCode);

        // Sales Agent adjust -> 403 Forbidden
        var resSales = await sales.Client.PostAsJsonAsync($"/api/inventory/{product.Id}/adjust", new AdjustStockRequest(40, "By Sales Agent"));
        Assert.Equal(HttpStatusCode.Forbidden, resSales.StatusCode);

        // Verify stock adjustment history accurately captures operator user ID in AdjustedByUserId
        using var scope = _factory.Services.CreateScope();
        var db = GetDbContext(scope);
        var invInDb = await db.Inventories.FirstOrDefaultAsync(i => i.ProductId == product.Id);
        var lastAdjustment = await db.InventoryAdjustments.OrderByDescending(a => a.AdjustedAtUtc).FirstOrDefaultAsync(a => a.InventoryId == invInDb!.Id);

        Assert.NotNull(lastAdjustment);
        Assert.Equal(op.User.Id, lastAdjustment.AdjustedByUserId);
        Assert.Equal(30, lastAdjustment.NewQuantity);
    }
}
