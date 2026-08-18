using Fulfillment.Api.Middleware;
using Fulfillment.Application.Categories;
using Fulfillment.Application.Warehouses;
using Fulfillment.Infrastructure;
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

builder.Services.AddScoped<Fulfillment.Application.Products.IProductRepository, ProductRepository>();
builder.Services.AddScoped<Fulfillment.Application.Products.IProductService, Fulfillment.Application.Products.ProductService>();

var app = builder.Build();

app.UseExceptionHandler();

app.MapControllers();

app.MapGet("/health", () => Results.Ok(new { status = "Healthy", environment = app.Environment.EnvironmentName }));

app.Run();

public partial class Program { }
