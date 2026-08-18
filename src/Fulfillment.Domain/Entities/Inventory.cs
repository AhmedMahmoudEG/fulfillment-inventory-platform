namespace Fulfillment.Domain.Entities;

public class Inventory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProductId { get; set; }
    public Product? Product { get; set; }
    public Guid WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }
    public int Quantity { get; private set; }

    public ICollection<InventoryAdjustment> Adjustments { get; set; } = new List<InventoryAdjustment>();

    public Inventory()
    {
        Quantity = 0;
    }

    public Inventory(Guid productId, Guid warehouseId, int initialQuantity = 0)
    {
        if (initialQuantity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(initialQuantity), "Quantity must never be negative.");
        }

        ProductId = productId;
        WarehouseId = warehouseId;
        Quantity = initialQuantity;
    }

    public void UpdateQuantity(int newQuantity)
    {
        if (newQuantity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(newQuantity), "Quantity must never be negative.");
        }

        Quantity = newQuantity;
    }
}
