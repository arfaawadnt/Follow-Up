using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FollowUp.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// 2026-09-18 — penalties leave the Deductions business (page, impact, reason): AutoPenalty mirror rows and manual
    /// Penalty-reason rows are deleted (penalty_record itself is untouched), deduction.penalty_record_id is dropped, the origin
    /// CHECK is narrowed to Manual / AutoDeal with Transportation / PercentageDeal. (No index build: it must not run inside the
    /// service-start window; see the note in Up.)
    /// </summary>
    public partial class PenaltiesOffDeductions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 2026-09-18 — penalties leave the deductions business: the AutoPenalty mirrors (fully derived from penalty_record,
            // which is untouched) and any manual 'Penalty'-reason rows are removed, the origin CHECK loses the AutoPenalty
            // clause, and the penalty link column goes. Penalties post to the rep statement (right = debit, wrong = credit).
            migrationBuilder.Sql(@"
DELETE FROM deduction WHERE origin = 'AutoPenalty' OR reason = 'Penalty';
ALTER TABLE deduction DROP CONSTRAINT IF EXISTS ck_deduction_origin;
ALTER TABLE deduction ADD CONSTRAINT ck_deduction_origin CHECK (
    origin IN ('Manual', 'AutoDeal')
    AND reason IN ('Transportation', 'PercentageDeal')
    AND (origin <> 'AutoDeal' OR (reason = 'PercentageDeal' AND period_from IS NOT NULL AND EXTRACT(DAY FROM period_from) = 1)));
");
            // NOTE: no index build here. detailed_registration is large and an index build inside the startup migration
            // outlived the Windows service-start timeout (30 s) on 2026-09-18. If the Penalty Report LDM check ever needs it,
            // build it out of band: CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_detailed_registration_acc_no ON detailed_registration (acc_no);

            migrationBuilder.DropForeignKey(
                name: "fk_deduction_penalty_records_penalty_record_id",
                table: "deduction");

            migrationBuilder.DropIndex(
                name: "ix_deduction_penalty_record_id",
                table: "deduction");

            migrationBuilder.DropColumn(
                name: "penalty_record_id",
                table: "deduction");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
ALTER TABLE deduction DROP CONSTRAINT IF EXISTS ck_deduction_origin;");

            migrationBuilder.AddColumn<Guid>(
                name: "penalty_record_id",
                table: "deduction",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_deduction_penalty_record_id",
                table: "deduction",
                column: "penalty_record_id",
                unique: true,
                filter: "penalty_record_id IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "fk_deduction_penalty_records_penalty_record_id",
                table: "deduction",
                column: "penalty_record_id",
                principalTable: "penalty_record",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql(@"
ALTER TABLE deduction ADD CONSTRAINT ck_deduction_origin CHECK (
    (origin = 'AutoPenalty') = (penalty_record_id IS NOT NULL)
    AND (origin <> 'AutoPenalty' OR reason = 'Penalty')
    AND (origin <> 'AutoDeal' OR (reason = 'PercentageDeal' AND period_from IS NOT NULL AND EXTRACT(DAY FROM period_from) = 1)));");
        }
    }
}
