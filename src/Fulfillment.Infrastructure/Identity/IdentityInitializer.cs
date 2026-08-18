using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Fulfillment.Infrastructure.Identity;

public static class IdentityInitializer
{
    public static readonly string[] RequiredRoles = new[]
    {
        "Admin",
        "Manager",
        "Warehouse Operator",
        "Sales Agent"
    };

    public static async Task InitializeAsync(IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("IdentityInitializer");

        try
        {
            // 1. Idempotent Role Seeding
            foreach (var role in RequiredRoles)
            {
                if (!await roleManager.RoleExistsAsync(role))
                {
                    await roleManager.CreateAsync(new IdentityRole(role));
                }
            }

            // 2. Idempotent Admin Bootstrap
            if (userManager.Users.Any())
            {
                return;
            }

            var seedEmail = configuration["SEED_ADMIN_EMAIL"] ?? configuration["SeedAdmin:Email"];
            var seedPassword = configuration["SEED_ADMIN_PASSWORD"] ?? configuration["SeedAdmin:Password"];

            if (string.IsNullOrWhiteSpace(seedEmail) || string.IsNullOrWhiteSpace(seedPassword))
            {
                logger.LogInformation("No bootstrap admin credentials provided in environment variables. Skipping bootstrap account creation.");
                return;
            }

            var adminUser = new ApplicationUser
            {
                UserName = seedEmail.Trim(),
                Email = seedEmail.Trim(),
                EmailConfirmed = true
            };

            var createResult = await userManager.CreateAsync(adminUser, seedPassword);
            if (!createResult.Succeeded)
            {
                logger.LogError("Failed to create bootstrap admin user during initialization.");
                return;
            }

            // Atomicity Safeguard: Rollback user if role assignment fails
            var roleResult = await userManager.AddToRoleAsync(adminUser, "Admin");
            if (!roleResult.Succeeded)
            {
                var deleteResult = await userManager.DeleteAsync(adminUser);
                if (!deleteResult.Succeeded)
                {
                    logger.LogError("Failed to assign Admin role to bootstrap user and failed to rollback user deletion.");
                }
                else
                {
                    logger.LogError("Failed to assign Admin role to bootstrap user. Bootstrap creation rolled back.");
                }
                return;
            }

            logger.LogInformation("Initial bootstrap Admin user successfully created.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "IdentityInitializer encountered an error accessing database during startup.");
        }
    }
}
