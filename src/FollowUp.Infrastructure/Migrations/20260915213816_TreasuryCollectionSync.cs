using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FollowUp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TreasuryCollectionSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "reason_id",
                table: "treasury_entry",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<decimal>(
                name: "collected_cash",
                table: "treasury_entry",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "collection_id",
                table: "treasury_entry",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "origin",
                table: "treasury_entry",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Manual");

            migrationBuilder.AddColumn<string>(
                name: "system_note",
                table: "treasury_entry",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "validated_at",
                table: "treasury_entry",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "validated_by",
                table: "treasury_entry",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "validation_note",
                table: "treasury_entry",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "validation_status",
                table: "treasury_entry",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "NotRequired");

            migrationBuilder.CreateTable(
                name: "treasury_grant",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    treasury_id = table.Column<Guid>(type: "uuid", nullable: false),
                    can_view = table.Column<bool>(type: "boolean", nullable: false),
                    can_validate = table.Column<bool>(type: "boolean", nullable: false),
                    can_update = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, defaultValue: "system"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_treasury_grant", x => x.id);
                    table.ForeignKey(
                        name: "fk_treasury_grant_role_role_id",
                        column: x => x.role_id,
                        principalTable: "role",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_treasury_grant_treasury_treasury_id",
                        column: x => x.treasury_id,
                        principalTable: "treasury",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_treasury_entry_collection_id",
                table: "treasury_entry",
                column: "collection_id",
                unique: true,
                filter: "collection_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_treasury_entry_treasury_id_validation_status",
                table: "treasury_entry",
                columns: new[] { "treasury_id", "validation_status" });

            migrationBuilder.CreateIndex(
                name: "ix_treasury_grant_role_id_treasury_id",
                table: "treasury_grant",
                columns: new[] { "role_id", "treasury_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_treasury_grant_treasury_id",
                table: "treasury_grant",
                column: "treasury_id");

            migrationBuilder.AddForeignKey(
                name: "fk_treasury_entry_collection_collection_id",
                table: "treasury_entry",
                column: "collection_id",
                principalTable: "collection",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // Mirrors the TreasuryEntry invariants (raw SQL like the other accounting CHECKs; not part of the EF snapshot):
            //   AutoCollection rows mirror exactly one collection, are debits only, carry no reason and are Pending or Validated;
            //   manual rows carry a reason, no collection link, and need no validation.
            migrationBuilder.Sql(@"
ALTER TABLE treasury_entry ADD CONSTRAINT ck_treasury_entry_origin CHECK (
    (origin = 'AutoCollection') = (collection_id IS NOT NULL)
    AND (origin <> 'AutoCollection' OR (credit = 0 AND reason_id IS NULL AND validation_status IN ('Pending', 'Validated')))
    AND (origin = 'AutoCollection' OR (reason_id IS NOT NULL AND validation_status = 'NotRequired'))
    AND ((validation_status = 'Validated') = (validated_at IS NOT NULL AND validated_by IS NOT NULL)));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_treasury_entry_collection_collection_id",
                table: "treasury_entry");

            migrationBuilder.DropTable(
                name: "treasury_grant");

            migrationBuilder.DropIndex(
                name: "ix_treasury_entry_collection_id",
                table: "treasury_entry");

            migrationBuilder.DropIndex(
                name: "ix_treasury_entry_treasury_id_validation_status",
                table: "treasury_entry");

            migrationBuilder.DropColumn(
                name: "collected_cash",
                table: "treasury_entry");

            migrationBuilder.DropColumn(
                name: "collection_id",
                table: "treasury_entry");

            migrationBuilder.DropColumn(
                name: "origin",
                table: "treasury_entry");

            migrationBuilder.DropColumn(
                name: "system_note",
                table: "treasury_entry");

            migrationBuilder.DropColumn(
                name: "validated_at",
                table: "treasury_entry");

            migrationBuilder.DropColumn(
                name: "validated_by",
                table: "treasury_entry");

            migrationBuilder.DropColumn(
                name: "validation_note",
                table: "treasury_entry");

            migrationBuilder.DropColumn(
                name: "validation_status",
                table: "treasury_entry");

            migrationBuilder.AlterColumn<Guid>(
                name: "reason_id",
                table: "treasury_entry",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
