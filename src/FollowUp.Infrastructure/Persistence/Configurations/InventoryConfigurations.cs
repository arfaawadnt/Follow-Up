using FollowUp.Domain.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FollowUp.Infrastructure.Persistence.Configurations;

// Inventory module. Quantities are numeric(18,3); Money columns take numeric(18,2) and Enumerations persist by Name via
// the DbContext conventions. "Serial" is a PostgreSQL identity (GENERATED ALWAYS) on every document / ledger row so the
// human numbers (PO-00012, GR-00007, TR-00003, movement #) are stable. Every FK from stock to master data is Restrict:
// an item / store / distributor / manufacturer with history is deactivated, never deleted. Line tables are owned
// children of their document (cascade with it — a document is never deleted once it has stock effects anyway).

internal sealed class ManufacturerConfiguration : IEntityTypeConfiguration<Manufacturer>
{
    public void Configure(EntityTypeBuilder<Manufacturer> b)
    {
        b.ToTable("manufacturer");
        b.HasKey(x => x.Id);
        b.IgnoreDomainEvents();
        b.MapAuditable();
        b.Property(x => x.Name).HasMaxLength(150).IsRequired();
        b.Property(x => x.Country).HasMaxLength(80);
        b.Property(x => x.Notes).HasMaxLength(500);
        b.Property(x => x.IsActive);
        b.HasIndex(x => x.Name).IsUnique();
    }
}

internal sealed class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> b)
    {
        b.ToTable("supplier");
        b.HasKey(x => x.Id);
        b.IgnoreDomainEvents();
        b.MapAuditable();
        b.Property(x => x.Name).HasMaxLength(150).IsRequired();
        b.Property(x => x.ContactPerson).HasMaxLength(120);
        b.Property(x => x.Phone).HasMaxLength(40);
        b.Property(x => x.Email).HasMaxLength(150);
        b.Property(x => x.Address).HasMaxLength(300);
        b.Property(x => x.Notes).HasMaxLength(500);
        b.Property(x => x.IsActive);
        b.HasIndex(x => x.Name).IsUnique();
    }
}

internal sealed class StoreConfiguration : IEntityTypeConfiguration<Store>
{
    public void Configure(EntityTypeBuilder<Store> b)
    {
        b.ToTable("store");
        b.HasKey(x => x.Id);
        b.IgnoreDomainEvents();
        b.MapAuditable();
        b.Property(x => x.Name).HasMaxLength(120).IsRequired();
        b.Property(x => x.Branch).HasMaxLength(100).IsRequired();
        b.Property(x => x.Location).HasMaxLength(200);
        b.Property(x => x.IsActive);
        b.HasIndex(x => x.Name).IsUnique();
        b.HasIndex(x => x.Branch);
    }
}

internal sealed class InventoryItemConfiguration : IEntityTypeConfiguration<InventoryItem>
{
    public void Configure(EntityTypeBuilder<InventoryItem> b)
    {
        b.ToTable("inventory_item");
        b.HasKey(x => x.Id);
        b.IgnoreDomainEvents();
        b.MapAuditable();
        b.Property(x => x.Code).HasMaxLength(40).IsRequired();
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Kind);
        b.Property(x => x.CatalogNumber).HasMaxLength(80);
        b.Property(x => x.Unit).HasMaxLength(20).IsRequired();
        b.Property(x => x.MinStock).HasPrecision(18, 3);
        b.Property(x => x.ReorderQuantity).HasPrecision(18, 3);
        b.Property(x => x.ExpiryWarningDays);
        b.Property(x => x.StorageConditions).HasMaxLength(200);
        b.Property(x => x.Notes).HasMaxLength(1000);
        b.Property(x => x.IsActive);
        b.HasOne<Manufacturer>().WithMany().HasForeignKey(x => x.ManufacturerId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.Code).IsUnique();
        b.HasIndex(x => x.ManufacturerId);

        // Test links: owned child table (item × test code + type, quantity consumed per test).
        b.OwnsMany(x => x.TestLinks, t =>
        {
            t.ToTable("inventory_item_test");
            t.WithOwner().HasForeignKey("inventory_item_id");
            t.HasKey(x => x.Id);
            t.Property(x => x.TestCode).HasMaxLength(32).IsRequired();
            t.Property(x => x.TestType);
            t.Property(x => x.TestName).HasMaxLength(200).IsRequired();
            t.Property(x => x.QuantityPerTest).HasPrecision(18, 3);
            t.HasIndex("inventory_item_id", nameof(ItemTestLink.TestCode), nameof(ItemTestLink.TestType)).IsUnique();
            t.HasIndex(x => new { x.TestCode, x.TestType });
        });
        b.Navigation(x => x.TestLinks).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class PurchaseOrderConfiguration : IEntityTypeConfiguration<PurchaseOrder>
{
    public void Configure(EntityTypeBuilder<PurchaseOrder> b)
    {
        b.ToTable("purchase_order");
        b.HasKey(x => x.Id);
        b.IgnoreDomainEvents();
        b.MapAuditable();
        b.Property(x => x.Serial).UseIdentityAlwaysColumn();
        b.Property(x => x.OrderDate);
        b.Property(x => x.ExpectedDate);
        b.Property(x => x.Status);
        b.Property(x => x.Reference).HasMaxLength(80);
        b.Property(x => x.Notes).HasMaxLength(1000);
        b.Property(x => x.OrderedOn);
        b.Property(x => x.ClosedOn);
        b.Ignore(x => x.Number);
        b.Ignore(x => x.Total);
        b.Ignore(x => x.IsOpen);
        b.Ignore(x => x.IsEditable);
        b.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Store>().WithMany().HasForeignKey(x => x.StoreId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.Serial).IsUnique();
        b.HasIndex(x => new { x.OrderDate, x.Status });
        b.HasIndex(x => x.SupplierId);
        b.HasIndex(x => x.StoreId);

        b.OwnsMany(x => x.Lines, l =>
        {
            l.ToTable("purchase_order_line");
            l.WithOwner().HasForeignKey("purchase_order_id");
            l.HasKey(x => x.Id);
            l.Property(x => x.OrderedQuantity).HasPrecision(18, 3);
            l.Property(x => x.UnitPrice);
            l.Property(x => x.ReceivedQuantity).HasPrecision(18, 3);
            l.Property(x => x.Notes).HasMaxLength(300);
            l.Ignore(x => x.Outstanding);
            l.Ignore(x => x.IsFullyReceived);
            l.Ignore(x => x.LineTotal);
            l.HasOne<InventoryItem>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
            l.HasIndex("purchase_order_id", nameof(PurchaseOrderLine.ItemId)).IsUnique();
        });
        b.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class GoodsReceiptConfiguration : IEntityTypeConfiguration<GoodsReceipt>
{
    public void Configure(EntityTypeBuilder<GoodsReceipt> b)
    {
        b.ToTable("goods_receipt");
        b.HasKey(x => x.Id);
        b.IgnoreDomainEvents();
        b.MapAuditable();
        b.Property(x => x.Serial).UseIdentityAlwaysColumn();
        b.Property(x => x.ReceivedDate);
        b.Property(x => x.DeliveryNote).HasMaxLength(80);
        b.Property(x => x.InvoiceNumber).HasMaxLength(80);
        b.Property(x => x.Notes).HasMaxLength(1000);
        b.Ignore(x => x.Number);
        b.HasOne<PurchaseOrder>().WithMany().HasForeignKey(x => x.PurchaseOrderId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Store>().WithMany().HasForeignKey(x => x.StoreId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.Serial).IsUnique();
        b.HasIndex(x => x.PurchaseOrderId);
        b.HasIndex(x => new { x.StoreId, x.ReceivedDate });

        b.OwnsMany(x => x.Lines, l =>
        {
            l.ToTable("goods_receipt_line");
            l.WithOwner().HasForeignKey("goods_receipt_id");
            l.HasKey(x => x.Id);
            l.Property(x => x.Quantity).HasPrecision(18, 3);
            l.Property(x => x.LotNumber).HasMaxLength(64).IsRequired();
            l.Property(x => x.ExpiryDate);
            l.Property(x => x.UnitCost);
            l.HasOne<InventoryItem>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
            l.HasIndex(x => x.OrderLineId);
            l.HasIndex(x => x.ItemId);
        });
        b.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class StockLotConfiguration : IEntityTypeConfiguration<StockLot>
{
    public void Configure(EntityTypeBuilder<StockLot> b)
    {
        b.ToTable("stock_lot");
        b.HasKey(x => x.Id);
        b.IgnoreDomainEvents();
        b.MapAuditable();
        b.Property(x => x.LotNumber).HasMaxLength(64).IsRequired();
        b.Property(x => x.ExpiryDate);
        b.Property(x => x.Quantity).HasPrecision(18, 3);
        b.Property(x => x.UnitCost);
        b.Property(x => x.FirstReceivedOn);
        b.Ignore(x => x.Value);
        b.HasOne<InventoryItem>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Store>().WithMany().HasForeignKey(x => x.StoreId).OnDelete(DeleteBehavior.Restrict);
        // One row per item × store × lot number (case-insensitive lookups happen in the repository on the stored casing).
        b.HasIndex(x => new { x.ItemId, x.StoreId, x.LotNumber }).IsUnique();
        b.HasIndex(x => new { x.StoreId, x.ItemId });
        b.HasIndex(x => x.ExpiryDate);
    }
}

internal sealed class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> b)
    {
        b.ToTable("stock_movement");
        b.HasKey(x => x.Id);
        b.IgnoreDomainEvents();
        b.MapAuditable();
        b.Property(x => x.Serial).UseIdentityAlwaysColumn();
        b.Property(x => x.Date);
        b.Property(x => x.Type);
        b.Property(x => x.LotNumber).HasMaxLength(64).IsRequired();
        b.Property(x => x.ExpiryDate);
        b.Property(x => x.Quantity).HasPrecision(18, 3);
        b.Property(x => x.BalanceAfter).HasPrecision(18, 3);
        b.Property(x => x.UnitCost);
        b.Property(x => x.ReferenceKind).HasMaxLength(32).IsRequired();
        b.Property(x => x.ReferenceId);
        b.Property(x => x.Reason).HasMaxLength(40);
        b.Property(x => x.TestCode).HasMaxLength(32);
        b.Property(x => x.Notes).HasMaxLength(500);
        b.HasOne<InventoryItem>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Store>().WithMany().HasForeignKey(x => x.StoreId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<StockLot>().WithMany().HasForeignKey(x => x.LotId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.Serial).IsUnique();
        b.HasIndex(x => new { x.StoreId, x.Date });
        b.HasIndex(x => new { x.ItemId, x.Date });
        b.HasIndex(x => x.LotId);
        b.HasIndex(x => x.ReferenceId);
    }
}

internal sealed class StockTransferConfiguration : IEntityTypeConfiguration<StockTransfer>
{
    public void Configure(EntityTypeBuilder<StockTransfer> b)
    {
        b.ToTable("stock_transfer");
        b.HasKey(x => x.Id);
        b.IgnoreDomainEvents();
        b.MapAuditable();
        b.Property(x => x.Serial).UseIdentityAlwaysColumn();
        b.Property(x => x.Date);
        b.Property(x => x.Status);
        b.Property(x => x.Notes).HasMaxLength(1000);
        b.Property(x => x.ReceivedDate);
        b.Property(x => x.ReceiveNotes).HasMaxLength(1000);
        b.Ignore(x => x.Number);
        b.HasOne<Store>().WithMany().HasForeignKey(x => x.FromStoreId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Store>().WithMany().HasForeignKey(x => x.ToStoreId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.Serial).IsUnique();
        b.HasIndex(x => new { x.Date, x.Status });
        b.HasIndex(x => x.FromStoreId);
        b.HasIndex(x => x.ToStoreId);

        b.OwnsMany(x => x.Lines, l =>
        {
            l.ToTable("stock_transfer_line");
            l.WithOwner().HasForeignKey("stock_transfer_id");
            l.HasKey(x => x.Id);
            l.Property(x => x.LotNumber).HasMaxLength(64).IsRequired();
            l.Property(x => x.ExpiryDate);
            l.Property(x => x.Quantity).HasPrecision(18, 3);
            l.Property(x => x.ReceivedQuantity).HasPrecision(18, 3);
            l.Property(x => x.UnitCost);
            l.Ignore(x => x.Shortfall);
            l.HasOne<InventoryItem>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
            l.HasOne<StockLot>().WithMany().HasForeignKey(x => x.SourceLotId).OnDelete(DeleteBehavior.Restrict);
            l.HasIndex(x => x.SourceLotId);
        });
        b.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
