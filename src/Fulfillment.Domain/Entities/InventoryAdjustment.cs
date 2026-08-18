namespace Fulfillment.Domain.Entities;

public class InventoryAdjustment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid InventoryId { get; set; }
    public Inventory? Inventory { get; set; }
    public int PreviousQuantity { get; set; }
    public int NewQuantity { get; set; }
    public string? Reason { get; set; }
    public string AdjustedByUserId { get; set; } = string.Empty;
    public DateTime AdjustedAtUtc { get; set; } = DateTime.UtcNow;
}
