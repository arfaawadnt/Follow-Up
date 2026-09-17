using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace FollowUp.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// 2026-09-17 — Inventory module: manufacturers, distributors (supplier), stores, items + their test links, purchase
    /// orders + lines, goods receipts + lines, stock lots (item × store × lot number, expiry), the stock-movement ledger and
    /// store-to-store transfers + lines. Additive only (13 new tables, no existing table touched); raw-SQL CHECKs guard the
    /// domain invariants.
    /// </summary>
    public partial class InventoryModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "manufacturer",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    country = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, defaultValue: "system"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_manufacturer", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "store",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    branch = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    location = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, defaultValue: "system"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_store", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "supplier",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    contact_person = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    phone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    email = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    address = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, defaultValue: "system"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_supplier", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "inventory_item",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    manufacturer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    catalog_number = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    min_stock = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    reorder_quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    expiry_warning_days = table.Column<int>(type: "integer", nullable: false),
                    storage_conditions = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, defaultValue: "system"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inventory_item", x => x.id);
                    table.ForeignKey(
                        name: "fk_inventory_item_manufacturers_manufacturer_id",
                        column: x => x.manufacturer_id,
                        principalTable: "manufacturer",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_transfer",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    serial = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    from_store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    to_store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    received_date = table.Column<DateOnly>(type: "date", nullable: true),
                    receive_notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, defaultValue: "system"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_transfer", x => x.id);
                    table.ForeignKey(
                        name: "fk_stock_transfer_stores_from_store_id",
                        column: x => x.from_store_id,
                        principalTable: "store",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_transfer_stores_to_store_id",
                        column: x => x.to_store_id,
                        principalTable: "store",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "purchase_order",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    serial = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_date = table.Column<DateOnly>(type: "date", nullable: false),
                    expected_date = table.Column<DateOnly>(type: "date", nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    reference = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ordered_on = table.Column<DateOnly>(type: "date", nullable: true),
                    closed_on = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, defaultValue: "system"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_purchase_order", x => x.id);
                    table.ForeignKey(
                        name: "fk_purchase_order_stores_store_id",
                        column: x => x.store_id,
                        principalTable: "store",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_purchase_order_suppliers_supplier_id",
                        column: x => x.supplier_id,
                        principalTable: "supplier",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "inventory_item_test",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    test_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    test_type = table.Column<int>(type: "integer", nullable: false),
                    test_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    quantity_per_test = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    inventory_item_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inventory_item_test", x => x.id);
                    table.ForeignKey(
                        name: "fk_inventory_item_test_inventory_item_inventory_item_id",
                        column: x => x.inventory_item_id,
                        principalTable: "inventory_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "stock_lot",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lot_number = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    expiry_date = table.Column<DateOnly>(type: "date", nullable: true),
                    quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    unit_cost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    first_received_on = table.Column<DateOnly>(type: "date", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, defaultValue: "system"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_lot", x => x.id);
                    table.ForeignKey(
                        name: "fk_stock_lot_inventory_item_item_id",
                        column: x => x.item_id,
                        principalTable: "inventory_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_lot_stores_store_id",
                        column: x => x.store_id,
                        principalTable: "store",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "goods_receipt",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    serial = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    purchase_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    received_date = table.Column<DateOnly>(type: "date", nullable: false),
                    delivery_note = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    invoice_number = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, defaultValue: "system"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_goods_receipt", x => x.id);
                    table.ForeignKey(
                        name: "fk_goods_receipt_purchase_orders_purchase_order_id",
                        column: x => x.purchase_order_id,
                        principalTable: "purchase_order",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_goods_receipt_stores_store_id",
                        column: x => x.store_id,
                        principalTable: "store",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_goods_receipt_suppliers_supplier_id",
                        column: x => x.supplier_id,
                        principalTable: "supplier",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "purchase_order_line",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ordered_quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    received_quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    notes = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    purchase_order_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_purchase_order_line", x => x.id);
                    table.ForeignKey(
                        name: "fk_purchase_order_line_inventory_item_item_id",
                        column: x => x.item_id,
                        principalTable: "inventory_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_purchase_order_line_purchase_order_purchase_order_id",
                        column: x => x.purchase_order_id,
                        principalTable: "purchase_order",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "stock_movement",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    serial = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lot_number = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    expiry_date = table.Column<DateOnly>(type: "date", nullable: true),
                    quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    balance_after = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    unit_cost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    reference_kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    reference_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    test_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, defaultValue: "system"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_movement", x => x.id);
                    table.ForeignKey(
                        name: "fk_stock_movement_inventory_item_item_id",
                        column: x => x.item_id,
                        principalTable: "inventory_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_movement_stock_lot_lot_id",
                        column: x => x.lot_id,
                        principalTable: "stock_lot",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_movement_stores_store_id",
                        column: x => x.store_id,
                        principalTable: "store",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_transfer_line",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_lot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lot_number = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    expiry_date = table.Column<DateOnly>(type: "date", nullable: true),
                    quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    received_quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    unit_cost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    stock_transfer_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_transfer_line", x => x.id);
                    table.ForeignKey(
                        name: "fk_stock_transfer_line_inventory_item_item_id",
                        column: x => x.item_id,
                        principalTable: "inventory_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_transfer_line_stock_lot_source_lot_id",
                        column: x => x.source_lot_id,
                        principalTable: "stock_lot",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_transfer_line_stock_transfer_stock_transfer_id",
                        column: x => x.stock_transfer_id,
                        principalTable: "stock_transfer",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "goods_receipt_line",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    lot_number = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    expiry_date = table.Column<DateOnly>(type: "date", nullable: true),
                    unit_cost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    goods_receipt_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_goods_receipt_line", x => x.id);
                    table.ForeignKey(
                        name: "fk_goods_receipt_line_goods_receipt_goods_receipt_id",
                        column: x => x.goods_receipt_id,
                        principalTable: "goods_receipt",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_goods_receipt_line_inventory_items_item_id",
                        column: x => x.item_id,
                        principalTable: "inventory_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_goods_receipt_purchase_order_id",
                table: "goods_receipt",
                column: "purchase_order_id");

            migrationBuilder.CreateIndex(
                name: "ix_goods_receipt_serial",
                table: "goods_receipt",
                column: "serial",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_goods_receipt_store_id_received_date",
                table: "goods_receipt",
                columns: new[] { "store_id", "received_date" });

            migrationBuilder.CreateIndex(
                name: "ix_goods_receipt_supplier_id",
                table: "goods_receipt",
                column: "supplier_id");

            migrationBuilder.CreateIndex(
                name: "ix_goods_receipt_line_goods_receipt_id",
                table: "goods_receipt_line",
                column: "goods_receipt_id");

            migrationBuilder.CreateIndex(
                name: "ix_goods_receipt_line_item_id",
                table: "goods_receipt_line",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_goods_receipt_line_order_line_id",
                table: "goods_receipt_line",
                column: "order_line_id");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_item_code",
                table: "inventory_item",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inventory_item_manufacturer_id",
                table: "inventory_item",
                column: "manufacturer_id");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_item_test_inventory_item_id_test_code_test_type",
                table: "inventory_item_test",
                columns: new[] { "inventory_item_id", "test_code", "test_type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inventory_item_test_test_code_test_type",
                table: "inventory_item_test",
                columns: new[] { "test_code", "test_type" });

            migrationBuilder.CreateIndex(
                name: "ix_manufacturer_name",
                table: "manufacturer",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_purchase_order_order_date_status",
                table: "purchase_order",
                columns: new[] { "order_date", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_purchase_order_serial",
                table: "purchase_order",
                column: "serial",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_purchase_order_store_id",
                table: "purchase_order",
                column: "store_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_order_supplier_id",
                table: "purchase_order",
                column: "supplier_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_order_line_item_id",
                table: "purchase_order_line",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_order_line_purchase_order_id_item_id",
                table: "purchase_order_line",
                columns: new[] { "purchase_order_id", "item_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stock_lot_expiry_date",
                table: "stock_lot",
                column: "expiry_date");

            migrationBuilder.CreateIndex(
                name: "ix_stock_lot_item_id_store_id_lot_number",
                table: "stock_lot",
                columns: new[] { "item_id", "store_id", "lot_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stock_lot_store_id_item_id",
                table: "stock_lot",
                columns: new[] { "store_id", "item_id" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_movement_item_id_date",
                table: "stock_movement",
                columns: new[] { "item_id", "date" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_movement_lot_id",
                table: "stock_movement",
                column: "lot_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_movement_reference_id",
                table: "stock_movement",
                column: "reference_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_movement_serial",
                table: "stock_movement",
                column: "serial",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stock_movement_store_id_date",
                table: "stock_movement",
                columns: new[] { "store_id", "date" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfer_date_status",
                table: "stock_transfer",
                columns: new[] { "date", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfer_from_store_id",
                table: "stock_transfer",
                column: "from_store_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfer_serial",
                table: "stock_transfer",
                column: "serial",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfer_to_store_id",
                table: "stock_transfer",
                column: "to_store_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfer_line_item_id",
                table: "stock_transfer_line",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfer_line_source_lot_id",
                table: "stock_transfer_line",
                column: "source_lot_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfer_line_stock_transfer_id",
                table: "stock_transfer_line",
                column: "stock_transfer_id");

            migrationBuilder.CreateIndex(
                name: "ix_store_branch",
                table: "store",
                column: "branch");

            migrationBuilder.CreateIndex(
                name: "ix_store_name",
                table: "store",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_supplier_name",
                table: "supplier",
                column: "name",
                unique: true);

            // Raw-SQL CHECKs mirroring the domain invariants (the same discipline as the Accounting ledgers): quantities
            // never go negative, a line is never over-received, the movement sign follows its type, a transfer never
            // targets its own source.
            migrationBuilder.Sql(@"
ALTER TABLE inventory_item ADD CONSTRAINT ck_inventory_item_limits CHECK (min_stock >= 0 AND reorder_quantity >= 0 AND expiry_warning_days BETWEEN 0 AND 730);
ALTER TABLE inventory_item_test ADD CONSTRAINT ck_inventory_item_test_quantity CHECK (quantity_per_test > 0);
ALTER TABLE purchase_order_line ADD CONSTRAINT ck_purchase_order_line_quantities CHECK (ordered_quantity > 0 AND received_quantity >= 0 AND received_quantity <= ordered_quantity AND unit_price >= 0);
ALTER TABLE goods_receipt_line ADD CONSTRAINT ck_goods_receipt_line_quantity CHECK (quantity > 0 AND unit_cost >= 0);
ALTER TABLE stock_lot ADD CONSTRAINT ck_stock_lot_quantity CHECK (quantity >= 0 AND unit_cost >= 0);
ALTER TABLE stock_movement ADD CONSTRAINT ck_stock_movement_sign CHECK (
    quantity <> 0 AND balance_after >= 0
    AND ((type IN ('Receipt', 'TransferIn') AND quantity > 0)
      OR (type IN ('Consumption', 'Disposal', 'TransferOut', 'ReturnToSupplier') AND quantity < 0)
      OR type = 'Adjustment'));
ALTER TABLE stock_transfer ADD CONSTRAINT ck_stock_transfer_stores CHECK (from_store_id <> to_store_id);
ALTER TABLE stock_transfer_line ADD CONSTRAINT ck_stock_transfer_line_quantities CHECK (quantity > 0 AND (received_quantity IS NULL OR (received_quantity >= 0 AND received_quantity <= quantity)));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "goods_receipt_line");

            migrationBuilder.DropTable(
                name: "inventory_item_test");

            migrationBuilder.DropTable(
                name: "purchase_order_line");

            migrationBuilder.DropTable(
                name: "stock_movement");

            migrationBuilder.DropTable(
                name: "stock_transfer_line");

            migrationBuilder.DropTable(
                name: "goods_receipt");

            migrationBuilder.DropTable(
                name: "stock_lot");

            migrationBuilder.DropTable(
                name: "stock_transfer");

            migrationBuilder.DropTable(
                name: "purchase_order");

            migrationBuilder.DropTable(
                name: "inventory_item");

            migrationBuilder.DropTable(
                name: "store");

            migrationBuilder.DropTable(
                name: "supplier");

            migrationBuilder.DropTable(
                name: "manufacturer");
        }
    }
}
