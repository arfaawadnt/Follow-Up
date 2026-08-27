using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FollowUp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class VisitHistoryTransferLeg : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "car_plate",
                table: "visit_history",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "driver_mobile",
                table: "visit_history",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "driver_name",
                table: "visit_history",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "transfer_rep_id",
                table: "visit_history",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "car_plate",
                table: "visit_history");

            migrationBuilder.DropColumn(
                name: "driver_mobile",
                table: "visit_history");

            migrationBuilder.DropColumn(
                name: "driver_name",
                table: "visit_history");

            migrationBuilder.DropColumn(
                name: "transfer_rep_id",
                table: "visit_history");
        }
    }
}
