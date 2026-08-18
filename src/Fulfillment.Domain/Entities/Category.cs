namespace Fulfillment.Domain.Entities;

public class Category
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public bool IsDeleted { get; set; }

    public ICollection<Product> Products { get; set; } = new List<Product>();

    public bool CanDelete()
    {
        return !Products.Any(p => !p.IsDeleted);
    }
}
