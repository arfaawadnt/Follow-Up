using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FollowUp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class OracleCatalogueSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_test_setup_code",
                table: "test_setup");

            migrationBuilder.AddColumn<decimal>(
                name: "cost",
                table: "test_setup",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "source",
                table: "test_setup",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "test_type",
                table: "test_setup",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "source",
                table: "test_group",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "ix_test_setup_code_test_type",
                table: "test_setup",
                columns: new[] { "code", "test_type" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_test_setup_code_test_type",
                table: "test_setup");

            migrationBuilder.DropColumn(
                name: "cost",
                table: "test_setup");

            migrationBuilder.DropColumn(
                name: "source",
                table: "test_setup");

            migrationBuilder.DropColumn(
                name: "test_type",
                table: "test_setup");

            migrationBuilder.DropColumn(
                name: "source",
                table: "test_group");

            migrationBuilder.CreateIndex(
                name: "ix_test_setup_code",
                table: "test_setup",
                column: "code",
                unique: true);
        }
    }
}
