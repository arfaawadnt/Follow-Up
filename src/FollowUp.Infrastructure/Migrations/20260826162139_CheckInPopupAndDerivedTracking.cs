using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FollowUp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CheckInPopupAndDerivedTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "destination_lab",
                table: "outsource_sample",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AddColumn<string>(
                name: "notes",
                table: "daily_visit",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "outsource_count",
                table: "daily_visit",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "request_count",
                table: "daily_visit",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "total_required",
                table: "daily_visit",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "notes",
                table: "daily_visit");

            migrationBuilder.DropColumn(
                name: "outsource_count",
                table: "daily_visit");

            migrationBuilder.DropColumn(
                name: "request_count",
                table: "daily_visit");

            migrationBuilder.DropColumn(
                name: "total_required",
                table: "daily_visit");

            migrationBuilder.AlterColumn<string>(
                name: "destination_lab",
                table: "outsource_sample",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);
        }
    }
}
