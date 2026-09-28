using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FollowUp.Infrastructure.Migrations
{
    /// <summary>
    /// 2026-09-28 — detailed_registration stores when the registration was created in LDM (reg.created_date) and when each
    /// test line was added (reg_selected_services.created_date) so Detailed Statistics can flag tests added after the
    /// registration (blue within 3 h, red beyond). Two nullable columns, no default, no index: instant on the live table.
    /// </summary>
    public partial class DetailedRegistrationCreatedDates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "reg_created_at",
                table: "detailed_registration",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "test_created_at",
                table: "detailed_registration",
                type: "timestamp without time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "reg_created_at",
                table: "detailed_registration");

            migrationBuilder.DropColumn(
                name: "test_created_at",
                table: "detailed_registration");
        }
    }
}
