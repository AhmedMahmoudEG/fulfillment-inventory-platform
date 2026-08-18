using Fulfillment.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fulfillment.Infrastructure.Persistence.Configurations;

public class InventoryConfiguration : IEntityTypeConfiguration<Inventory>
{
    public void Configure(EntityTypeBuilder<Inventory> builder)
    {
        builder.ToTable("Inventories", t =>
        {
            t.HasCheckConstraint("CK_Inventory_Quantity_NonNegative", "[Quantity] >= 0");
        });

        builder.HasKey(i => i.Id);

        builder.Property(i => i.Quantity)
            .IsRequired();

        builder.HasIndex(i => new { i.ProductId, i.WarehouseId })
            .IsUnique();

        builder.HasMany(i => i.Adjustments)
            .WithOne(ia => ia.Inventory)
            .HasForeignKey(ia => ia.InventoryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
