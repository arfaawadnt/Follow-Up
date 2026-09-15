using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FollowUp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PenaltyRecordPerformedBy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "performed_by_rep_id",
                table: "penalty_record",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "performed_by_user_id",
                table: "penalty_record",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_penalty_record_performed_by_rep_id",
                table: "penalty_record",
                column: "performed_by_rep_id");

            migrationBuilder.CreateIndex(
                name: "ix_penalty_record_performed_by_user_id",
                table: "penalty_record",
                column: "performed_by_user_id");

            migrationBuilder.AddForeignKey(
                name: "fk_penalty_record_app_user_performed_by_user_id",
                table: "penalty_record",
                column: "performed_by_user_id",
                principalTable: "app_user",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_penalty_record_representatives_performed_by_rep_id",
                table: "penalty_record",
                column: "performed_by_rep_id",
                principalTable: "representative",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // Mirrors PenaltyRecord.Update: a Rep penalty names a representative, a DataEntry / Technician penalty
            // names a system user, never both. Rows recorded before this migration carry neither link, so "both
            // null" stays legal (legacy); the domain requires exactly one for every new or edited record.
            // Raw SQL, like the other accounting CHECKs (no EF model drift — CHECKs are not part of the snapshot).
            migrationBuilder.Sql(@"
ALTER TABLE penalty_record ADD CONSTRAINT ck_penalty_record_performed_by CHECK (
    NOT (performed_by_user_id IS NOT NULL AND performed_by_rep_id IS NOT NULL)
    AND (performed_by_rep_id IS NULL OR penalty_user = 'Rep')
    AND (performed_by_user_id IS NULL OR penalty_user <> 'Rep'));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE penalty_record DROP CONSTRAINT IF EXISTS ck_penalty_record_performed_by;");

            migrationBuilder.DropForeignKey(
                name: "fk_penalty_record_app_user_performed_by_user_id",
                table: "penalty_record");

            migrationBuilder.DropForeignKey(
                name: "fk_penalty_record_representatives_performed_by_rep_id",
                table: "penalty_record");

            migrationBuilder.DropIndex(
                name: "ix_penalty_record_performed_by_rep_id",
                table: "penalty_record");

            migrationBuilder.DropIndex(
                name: "ix_penalty_record_performed_by_user_id",
                table: "penalty_record");

            migrationBuilder.DropColumn(
                name: "performed_by_rep_id",
                table: "penalty_record");

            migrationBuilder.DropColumn(
                name: "performed_by_user_id",
                table: "penalty_record");
        }
    }
}
