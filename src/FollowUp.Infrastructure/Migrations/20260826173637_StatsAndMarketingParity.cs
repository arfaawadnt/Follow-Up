using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FollowUp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class StatsAndMarketingParity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "income",
                table: "test_statistic",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "number",
                table: "marketing_visit",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "plan",
                table: "marketing_visit",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "scheduled_time",
                table: "marketing_visit",
                type: "time without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "address",
                table: "laboratory",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "investigation_notes",
                table: "complaint",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_valid",
                table: "complaint",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "outcome_summary",
                table: "complaint",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "outcome_type",
                table: "complaint",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "received_at",
                table: "complaint",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "representative_id",
                table: "complaint",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "resolution_summary",
                table: "complaint",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "validity_notes",
                table: "complaint",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            // Backfill sequential MV numbers for pre-existing rows (stable created_at order) before the unique index.
            migrationBuilder.Sql(
                "UPDATE marketing_visit SET number = sub.rn " +
                "FROM (SELECT id, row_number() OVER (ORDER BY created_at) AS rn FROM marketing_visit) sub " +
                "WHERE marketing_visit.id = sub.id;");

            migrationBuilder.CreateIndex(
                name: "ix_marketing_visit_number",
                table: "marketing_visit",
                column: "number",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_marketing_visit_number",
                table: "marketing_visit");

            migrationBuilder.DropColumn(
                name: "income",
                table: "test_statistic");

            migrationBuilder.DropColumn(
                name: "number",
                table: "marketing_visit");

            migrationBuilder.DropColumn(
                name: "plan",
                table: "marketing_visit");

            migrationBuilder.DropColumn(
                name: "scheduled_time",
                table: "marketing_visit");

            migrationBuilder.DropColumn(
                name: "address",
                table: "laboratory");

            migrationBuilder.DropColumn(
                name: "investigation_notes",
                table: "complaint");

            migrationBuilder.DropColumn(
                name: "is_valid",
                table: "complaint");

            migrationBuilder.DropColumn(
                name: "outcome_summary",
                table: "complaint");

            migrationBuilder.DropColumn(
                name: "outcome_type",
                table: "complaint");

            migrationBuilder.DropColumn(
                name: "received_at",
                table: "complaint");

            migrationBuilder.DropColumn(
                name: "representative_id",
                table: "complaint");

            migrationBuilder.DropColumn(
                name: "resolution_summary",
                table: "complaint");

            migrationBuilder.DropColumn(
                name: "validity_notes",
                table: "complaint");
        }
    }
}
