using Fulfillment.Application.Categories.DTOs;
using Fulfillment.Application.Common.Exceptions;
using Fulfillment.Domain.Entities;

namespace Fulfillment.Application.Categories;

public class CategoryService : ICategoryService
{
    private readonly ICategoryRepository _repository;

    public CategoryService(ICategoryRepository repository)
    {
        _repository = repository;
    }

    public async Task<CategoryResponse> CreateAsync(CreateCategoryRequest request, CancellationToken cancellationToken = default)
    {
        var trimmedName = request.Name?.Trim();

        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            throw new ValidationException("Category name is required.");
        }

        if (await _repository.ExistsByNameAsync(trimmedName, cancellationToken))
        {
            throw new ConflictException("A category with this name already exists.");
        }

        var category = new Category
        {
            Name = trimmedName
        };

        await _repository.AddAsync(category, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        return new CategoryResponse(category.Id, category.Name);
    }

    public async Task<List<CategoryResponse>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var categories = await _repository.GetAllActiveAsync(cancellationToken);
        return categories.Select(c => new CategoryResponse(c.Id, c.Name)).ToList();
    }

    public async Task<CategoryResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var category = await _repository.GetByIdAsync(id, cancellationToken);
        if (category == null)
        {
            throw new NotFoundException("Category not found.");
        }

        return new CategoryResponse(category.Id, category.Name);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var category = await _repository.GetByIdForDeletionAsync(id, cancellationToken);
        if (category == null)
        {
            throw new NotFoundException("Category not found.");
        }

        if (!category.CanDelete())
        {
            throw new ConflictException("Cannot delete category because active products are associated with it.");
        }

        category.IsDeleted = true;
        await _repository.SaveChangesAsync(cancellationToken);
    }
}
