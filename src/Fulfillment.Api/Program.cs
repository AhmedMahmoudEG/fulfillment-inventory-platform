using Fulfillment.Api.Middleware;
using Fulfillment.Application.Categories;
using Fulfillment.Application.Inventory;
using Fulfillment.Application.Products;
using Fulfillment.Application.Warehouses;
using Fulfillment.Infrastructure;
using Fulfillment.Infrastructure.Identity;
using Fulfillment.Infrastructure.Repositories;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Services.AddInfrastructureServices(builder.Configuration);

builder.Services.AddScoped<ICategoryRepository, CategoryRepository>();
builder.Services.AddScoped<ICategoryService, CategoryService>();

builder.Services.AddScoped<IWarehouseRepository, WarehouseRepository>();
builder.Services.AddScoped<IWarehouseService, WarehouseService>();

builder.Services.AddScoped<IProductRepository, ProductRepository>();
builder.Services.AddScoped<IProductService, ProductService>();

builder.Services.AddScoped<IInventoryRepository, InventoryRepository>();
builder.Services.AddScoped<IInventoryService, InventoryService>();

var app = builder.Build();

app.UseExceptionHandler();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapGet("/health", () => Results.Ok(new { status = "Healthy", environment = app.Environment.EnvironmentName }));

// Run idempotent role seeding & admin bootstrap
await IdentityInitializer.InitializeAsync(app.Services);

app.Run();

public partial class Program { }
