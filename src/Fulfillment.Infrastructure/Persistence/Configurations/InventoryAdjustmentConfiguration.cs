using Fulfillment.Domain.Entities;
using Fulfillment.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fulfillment.Infrastructure.Persistence.Configurations;

public class InventoryAdjustmentConfiguration : IEntityTypeConfiguration<InventoryAdjustment>
{
    public void Configure(EntityTypeBuilder<InventoryAdjustment> builder)
    {
        builder.ToTable("InventoryAdjustments");

        builder.HasKey(ia => ia.Id);

        builder.Property(ia => ia.PreviousQuantity)
            .IsRequired();

        builder.Property(ia => ia.NewQuantity)
            .IsRequired();

        builder.Property(ia => ia.Reason)
            .HasMaxLength(500);

        builder.Property(ia => ia.AdjustedByUserId)
            .IsRequired();

        builder.Property(ia => ia.AdjustedAtUtc)
            .IsRequired();

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(ia => ia.AdjustedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
