using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FollowUp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AreaManagementRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Two new representative types (AreaResponsible, AreaManager). The type is persisted by Name and guarded
            // by the SchemaHardening CHECK, which the model can't express — widen it here (raw SQL, same style).
            migrationBuilder.Sql("ALTER TABLE representative DROP CONSTRAINT IF EXISTS ck_representative_type;");
            migrationBuilder.Sql("ALTER TABLE representative ADD CONSTRAINT ck_representative_type " +
                "CHECK (type IN ('Collector','Marketing','Transfer','Scanning','AreaResponsible','AreaManager'));");

            migrationBuilder.AddColumn<Guid>(
                name: "area_manager_id",
                table: "area",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "area_responsible_id",
                table: "area",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_area_area_manager_id",
                table: "area",
                column: "area_manager_id");

            migrationBuilder.CreateIndex(
                name: "ix_area_area_responsible_id",
                table: "area",
                column: "area_responsible_id");

            migrationBuilder.AddForeignKey(
                name: "fk_area_representatives_area_manager_id",
                table: "area",
                column: "area_manager_id",
                principalTable: "representative",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_area_representatives_area_responsible_id",
                table: "area",
                column: "area_responsible_id",
                principalTable: "representative",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Restore the original 4-value CHECK. By design this fails while any AreaResponsible/AreaManager rep
            // exists — a downgrade must first reassign or remove those reps rather than silently invalidate them.
            migrationBuilder.Sql("ALTER TABLE representative DROP CONSTRAINT IF EXISTS ck_representative_type;");
            migrationBuilder.Sql("ALTER TABLE representative ADD CONSTRAINT ck_representative_type " +
                "CHECK (type IN ('Collector','Marketing','Transfer','Scanning'));");

            migrationBuilder.DropForeignKey(
                name: "fk_area_representatives_area_manager_id",
                table: "area");

            migrationBuilder.DropForeignKey(
                name: "fk_area_representatives_area_responsible_id",
                table: "area");

            migrationBuilder.DropIndex(
                name: "ix_area_area_manager_id",
                table: "area");

            migrationBuilder.DropIndex(
                name: "ix_area_area_responsible_id",
                table: "area");

            migrationBuilder.DropColumn(
                name: "area_manager_id",
                table: "area");

            migrationBuilder.DropColumn(
                name: "area_responsible_id",
                table: "area");
        }
    }
}
