using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FollowUp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UniqueOracleSourceCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_representative_source_code",
                table: "representative");

            migrationBuilder.DropIndex(
                name: "ix_city_source_code",
                table: "city");

            migrationBuilder.DropIndex(
                name: "ix_area_source_code",
                table: "area");

            migrationBuilder.CreateIndex(
                name: "ix_representative_source_code",
                table: "representative",
                column: "source_code",
                unique: true,
                filter: "source_code IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_city_source_code",
                table: "city",
                column: "source_code",
                unique: true,
                filter: "source_code IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_area_source_code",
                table: "area",
                column: "source_code",
                unique: true,
                filter: "source_code IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_representative_source_code",
                table: "representative");

            migrationBuilder.DropIndex(
                name: "ix_city_source_code",
                table: "city");

            migrationBuilder.DropIndex(
                name: "ix_area_source_code",
                table: "area");

            migrationBuilder.CreateIndex(
                name: "ix_representative_source_code",
                table: "representative",
                column: "source_code");

            migrationBuilder.CreateIndex(
                name: "ix_city_source_code",
                table: "city",
                column: "source_code");

            migrationBuilder.CreateIndex(
                name: "ix_area_source_code",
                table: "area",
                column: "source_code");
        }
    }
}
