namespace Fulfillment.Domain.Entities;

public class Warehouse
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string? Location { get; set; }
    public bool IsDeleted { get; set; }

    public ICollection<Inventory> Inventories { get; set; } = new List<Inventory>();

    public bool CanDelete()
    {
        return !Inventories.Any(i => i.Product == null || !i.Product.IsDeleted);
    }
}
