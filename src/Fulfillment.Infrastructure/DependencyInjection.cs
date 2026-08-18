using System.Text;
using Fulfillment.Application.Auth;
using Fulfillment.Application.Users;
using Fulfillment.Infrastructure.Auth;
using Fulfillment.Infrastructure.Identity;
using Fulfillment.Infrastructure.Persistence;
using Fulfillment.Infrastructure.Users;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Fulfillment.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");

        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseSqlServer(connectionString,
                b => b.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName)));

        services.AddIdentity<ApplicationUser, IdentityRole>(options =>
        {
            options.Password.RequireDigit = true;
            options.Password.RequiredLength = 8;
            options.Password.RequireNonAlphanumeric = false;
            options.User.RequireUniqueEmail = true;
        })
        .AddEntityFrameworkStores<ApplicationDbContext>()
        .AddDefaultTokenProviders();

        var jwtSection = configuration.GetSection(JwtSettings.SectionName);
        services.Configure<JwtSettings>(jwtSection);
        var jwtSettings = jwtSection.Get<JwtSettings>();

        if (string.IsNullOrWhiteSpace(jwtSettings?.SigningKey))
        {
            throw new InvalidOperationException("JWT SigningKey configuration is missing or empty.");
        }

        string signingKey = jwtSettings.SigningKey;

        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = !string.IsNullOrWhiteSpace(jwtSettings.Issuer) ? jwtSettings.Issuer : "FulfillmentApi",
                ValidAudience = !string.IsNullOrWhiteSpace(jwtSettings.Audience) ? jwtSettings.Audience : "FulfillmentClients",
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey))
            };
        });

        services.AddAuthorization(options =>
        {
            options.AddPolicy("InventoryView", policy =>
                policy.RequireRole("Admin", "Manager", "Warehouse Operator", "Sales Agent"));

            options.AddPolicy("InventoryAdjust", policy =>
                policy.RequireRole("Admin", "Manager", "Warehouse Operator"));

            options.AddPolicy("UserManagement", policy =>
                policy.RequireRole("Admin"));
        });

        services.AddSingleton<JwtTokenGenerator>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IUserService, UserService>();

        return services;
    }
}
