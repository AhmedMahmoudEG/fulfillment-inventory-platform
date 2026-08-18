using Fulfillment.Application.Categories;
using Fulfillment.Application.Common.Exceptions;
using Fulfillment.Application.Products.DTOs;
using Fulfillment.Application.Warehouses;
using Fulfillment.Domain.Entities;

namespace Fulfillment.Application.Products;

public class ProductService : IProductService
{
    private readonly IProductRepository _productRepository;
    private readonly ICategoryRepository _categoryRepository;
    private readonly IWarehouseRepository _warehouseRepository;

    public ProductService(
        IProductRepository productRepository,
        ICategoryRepository categoryRepository,
        IWarehouseRepository warehouseRepository)
    {
        _productRepository = productRepository;
        _categoryRepository = categoryRepository;
        _warehouseRepository = warehouseRepository;
    }

    public async Task<ProductResponse> CreateAsync(CreateProductRequest request, CancellationToken cancellationToken = default)
    {
        var trimmedName = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            throw new ValidationException("Product name is required.");
        }

        var trimmedSku = request.SKU?.Trim();
        if (string.IsNullOrWhiteSpace(trimmedSku))
        {
            throw new ValidationException("Product SKU is required.");
        }

        if (HasMoreThanTwoDecimalPlaces(request.Price))
        {
            throw new ValidationException("Product price cannot exceed 2 decimal places.");
        }

        if (request.CategoryId == Guid.Empty)
        {
            throw new ValidationException("CategoryId is required.");
        }

        if (request.WarehouseId == Guid.Empty)
        {
            throw new ValidationException("WarehouseId is required.");
        }

        var category = await _categoryRepository.GetByIdAsync(request.CategoryId, cancellationToken);
        if (category == null)
        {
            throw new NotFoundException("Category not found.");
        }

        var warehouse = await _warehouseRepository.GetByIdAsync(request.WarehouseId, cancellationToken);
        if (warehouse == null)
        {
            throw new NotFoundException("Warehouse not found.");
        }

        if (await _productRepository.ExistsBySkuAsync(trimmedSku, cancellationToken))
        {
            throw new ConflictException("A product with this SKU already exists.");
        }

        var trimmedDescription = request.Description?.Trim();

        var product = new Product
        {
            Name = trimmedName,
            Description = trimmedDescription,
            SKU = trimmedSku,
            Price = request.Price,
            CategoryId = request.CategoryId
        };

        var initialInventory = new Fulfillment.Domain.Entities.Inventory(product.Id, request.WarehouseId, initialQuantity: 0);
        product.Inventories.Add(initialInventory);

        await _productRepository.AddAsync(product, cancellationToken);
        await _productRepository.SaveChangesAsync(cancellationToken);

        return MapToResponse(product, request.WarehouseId);
    }

    public async Task<List<ProductResponse>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var products = await _productRepository.GetAllActiveAsync(cancellationToken);
        return products.Select(p => MapToResponse(p)).ToList();
    }

    public async Task<ProductResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var product = await _productRepository.GetByIdAsync(id, cancellationToken);
        if (product == null)
        {
            throw new NotFoundException("Product not found.");
        }

        return MapToResponse(product);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var product = await _productRepository.GetByIdForDeletionAsync(id, cancellationToken);
        if (product == null)
        {
            throw new NotFoundException("Product not found.");
        }

        product.IsDeleted = true;
        await _productRepository.SaveChangesAsync(cancellationToken);
    }

    private static bool HasMoreThanTwoDecimalPlaces(decimal value)
    {
        return decimal.Remainder(value * 100m, 1m) != 0m;
    }

    private static ProductResponse MapToResponse(Product product, Guid fallbackWarehouseId = default)
    {
        var inventory = product.Inventories.FirstOrDefault();
        var warehouseId = inventory?.WarehouseId ?? fallbackWarehouseId;
        var inventoryQuantity = inventory?.Quantity ?? 0;

        return new ProductResponse(
            product.Id,
            product.Name,
            product.Description,
            product.SKU,
            product.Price,
            product.CategoryId,
            warehouseId,
            inventoryQuantity);
    }
}
