using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FollowUp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class LabRepReferenceParity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // --- New reference/parity columns ---
            migrationBuilder.AddColumn<string>(
                name: "area", table: "representative", type: "character varying(100)", maxLength: 100, nullable: true);
            migrationBuilder.AddColumn<string>(
                name: "employment_type", table: "representative", type: "character varying(40)", maxLength: 40, nullable: true);
            migrationBuilder.AddColumn<int>(
                name: "avg_monthly_samples", table: "laboratory", type: "integer", nullable: true);
            migrationBuilder.AddColumn<DateOnly>(
                name: "license_date", table: "laboratory", type: "date", nullable: true);
            migrationBuilder.AddColumn<string>(
                name: "license_no", table: "laboratory", type: "character varying(100)", maxLength: 100, nullable: true);
            migrationBuilder.AddColumn<string>(
                name: "preferred_channel", table: "laboratory", type: "character varying(40)", maxLength: 40, nullable: true);

            // --- Multiple collectors: add the jsonb list, migrate the existing single collector into it, drop the old FK column ---
            migrationBuilder.AddColumn<string>(
                name: "collector_reps", table: "laboratory", type: "jsonb", nullable: false, defaultValue: "[]");
            migrationBuilder.Sql(
                "UPDATE laboratory SET collector_reps = to_jsonb(ARRAY[collector_rep_id]) WHERE collector_rep_id IS NOT NULL;");
            migrationBuilder.DropForeignKey(name: "fk_laboratory_representatives_collector_rep_id", table: "laboratory");
            migrationBuilder.DropIndex(name: "ix_laboratory_collector_rep_id", table: "laboratory");
            migrationBuilder.DropColumn(name: "collector_rep_id", table: "laboratory");

            // --- Status rename New -> Interactive (matches the reference platform) ---
            migrationBuilder.Sql("UPDATE laboratory SET status = 'Interactive' WHERE status = 'New';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE laboratory SET status = 'New' WHERE status = 'Interactive';");

            // Restore the single collector column from the first element of the jsonb list.
            migrationBuilder.AddColumn<Guid>(name: "collector_rep_id", table: "laboratory", type: "uuid", nullable: true);
            migrationBuilder.Sql(
                "UPDATE laboratory SET collector_rep_id = (collector_reps->>0)::uuid WHERE jsonb_array_length(collector_reps) > 0;");
            migrationBuilder.CreateIndex(name: "ix_laboratory_collector_rep_id", table: "laboratory", column: "collector_rep_id");
            migrationBuilder.AddForeignKey(
                name: "fk_laboratory_representatives_collector_rep_id", table: "laboratory",
                column: "collector_rep_id", principalTable: "representative", principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.DropColumn(name: "collector_reps", table: "laboratory");

            migrationBuilder.DropColumn(name: "area", table: "representative");
            migrationBuilder.DropColumn(name: "employment_type", table: "representative");
            migrationBuilder.DropColumn(name: "avg_monthly_samples", table: "laboratory");
            migrationBuilder.DropColumn(name: "license_date", table: "laboratory");
            migrationBuilder.DropColumn(name: "license_no", table: "laboratory");
            migrationBuilder.DropColumn(name: "preferred_channel", table: "laboratory");
        }
    }
}
