using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FollowUp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DeductionAutomation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_adjusted",
                table: "deduction",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "origin",
                table: "deduction",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Manual");

            migrationBuilder.AddColumn<Guid>(
                name: "penalty_record_id",
                table: "deduction",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "system_note",
                table: "deduction",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_deduction_penalty_record_id",
                table: "deduction",
                column: "penalty_record_id",
                unique: true,
                filter: "penalty_record_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_deduction_auto_deal_area_month",
                table: "deduction",
                columns: new[] { "area_id", "period_from" },
                unique: true,
                filter: "origin = 'AutoDeal'");

            migrationBuilder.AddForeignKey(
                name: "fk_deduction_penalty_records_penalty_record_id",
                table: "deduction",
                column: "penalty_record_id",
                principalTable: "penalty_record",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // Mirrors the Deduction invariants (raw SQL like the other accounting CHECKs; not part of the EF snapshot):
            //   AutoPenalty rows mirror exactly one penalty and carry the Penalty reason;
            //   AutoDeal rows carry the PercentageDeal reason and a period anchored on the 1st;
            //   manual rows never link a penalty.
            migrationBuilder.Sql(@"
ALTER TABLE deduction ADD CONSTRAINT ck_deduction_origin CHECK (
    (origin = 'AutoPenalty') = (penalty_record_id IS NOT NULL)
    AND (origin <> 'AutoPenalty' OR reason = 'Penalty')
    AND (origin <> 'AutoDeal' OR (reason = 'PercentageDeal' AND period_from IS NOT NULL AND EXTRACT(DAY FROM period_from) = 1)));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE deduction DROP CONSTRAINT IF EXISTS ck_deduction_origin;");

            migrationBuilder.DropForeignKey(
                name: "fk_deduction_penalty_records_penalty_record_id",
                table: "deduction");

            migrationBuilder.DropIndex(
                name: "ix_deduction_penalty_record_id",
                table: "deduction");

            migrationBuilder.DropIndex(
                name: "ux_deduction_auto_deal_area_month",
                table: "deduction");

            migrationBuilder.DropColumn(
                name: "is_adjusted",
                table: "deduction");

            migrationBuilder.DropColumn(
                name: "origin",
                table: "deduction");

            migrationBuilder.DropColumn(
                name: "penalty_record_id",
                table: "deduction");

            migrationBuilder.DropColumn(
                name: "system_note",
                table: "deduction");
        }
    }
}
