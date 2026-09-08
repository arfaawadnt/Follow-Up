using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FollowUp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDeliveryLogContent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "body",
                table: "notification_delivery_log",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "parameters_json",
                table: "notification_delivery_log",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "subject",
                table: "notification_delivery_log",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "body",
                table: "notification_delivery_log");

            migrationBuilder.DropColumn(
                name: "parameters_json",
                table: "notification_delivery_log");

            migrationBuilder.DropColumn(
                name: "subject",
                table: "notification_delivery_log");
        }
    }
}
