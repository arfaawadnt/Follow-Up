using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FollowUp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class OperationsReferenceParity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "checked_in_at",
                table: "visit_history",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "received_at",
                table: "visit_history",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "scheduled_time",
                table: "visit_history",
                type: "time without time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "transfer_confirmed_at",
                table: "visit_history",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "notes",
                table: "sample_tracking",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "notes",
                table: "outsource_sample",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "checked_in_at",
                table: "visit_history");

            migrationBuilder.DropColumn(
                name: "received_at",
                table: "visit_history");

            migrationBuilder.DropColumn(
                name: "scheduled_time",
                table: "visit_history");

            migrationBuilder.DropColumn(
                name: "transfer_confirmed_at",
                table: "visit_history");

            migrationBuilder.DropColumn(
                name: "notes",
                table: "sample_tracking");

            migrationBuilder.DropColumn(
                name: "notes",
                table: "outsource_sample");
        }
    }
}
