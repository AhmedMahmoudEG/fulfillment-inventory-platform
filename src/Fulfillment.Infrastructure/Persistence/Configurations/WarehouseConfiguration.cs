using Fulfillment.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fulfillment.Infrastructure.Persistence.Configurations;

public class WarehouseConfiguration : IEntityTypeConfiguration<Warehouse>
{
    public void Configure(EntityTypeBuilder<Warehouse> builder)
    {
        builder.ToTable("Warehouses");

        builder.HasKey(w => w.Id);

        builder.Property(w => w.Name)
            .IsRequired()
            .HasMaxLength(200)
            .UseCollation("SQL_Latin1_General_CP1_CS_AS");

        builder.HasIndex(w => w.Name)
            .IsUnique();

        builder.Property(w => w.Address)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(w => w.Location)
            .HasMaxLength(200);

        builder.Property(w => w.IsDeleted)
            .IsRequired();

        builder.HasQueryFilter(w => !w.IsDeleted);

        builder.HasMany(w => w.Inventories)
            .WithOne(i => i.Warehouse)
            .HasForeignKey(i => i.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
