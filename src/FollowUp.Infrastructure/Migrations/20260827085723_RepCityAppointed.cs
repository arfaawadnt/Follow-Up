using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FollowUp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RepCityAppointed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "appointed_on",
                table: "representative",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "city",
                table: "representative",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "appointed_on",
                table: "representative");

            migrationBuilder.DropColumn(
                name: "city",
                table: "representative");
        }
    }
}
