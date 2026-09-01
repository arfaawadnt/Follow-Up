using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FollowUp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class OracleReferenceLabsSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "source",
                table: "representative",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "source_code",
                table: "representative",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "source",
                table: "ref_item",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "source",
                table: "laboratory",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "source",
                table: "city",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "source_code",
                table: "city",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "source",
                table: "area",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "source_code",
                table: "area",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

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

            migrationBuilder.DropColumn(
                name: "source",
                table: "representative");

            migrationBuilder.DropColumn(
                name: "source_code",
                table: "representative");

            migrationBuilder.DropColumn(
                name: "source",
                table: "ref_item");

            migrationBuilder.DropColumn(
                name: "source",
                table: "laboratory");

            migrationBuilder.DropColumn(
                name: "source",
                table: "city");

            migrationBuilder.DropColumn(
                name: "source_code",
                table: "city");

            migrationBuilder.DropColumn(
                name: "source",
                table: "area");

            migrationBuilder.DropColumn(
                name: "source_code",
                table: "area");
        }
    }
}
