using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FollowUp.Infrastructure.Migrations
{
    /// <summary>
    /// 2026-09-19 — a DataEntry penalty also names its reviewer (charged to both on the Penalty Report); a collection carries
    /// the out-source income (statement credit = cash + bank − out-source) and the bank transfer's reference number.
    /// </summary>
    public partial class PenaltyReviewerCollectionOutsource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "reviewed_by_user_id",
                table: "penalty_record",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "outsource_income",
                table: "collection",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValueSql: "0");

            migrationBuilder.AddColumn<string>(
                name: "reference_number",
                table: "collection",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_penalty_record_reviewed_by_user_id",
                table: "penalty_record",
                column: "reviewed_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_collection_reference_number",
                table: "collection",
                column: "reference_number");

            migrationBuilder.AddForeignKey(
                name: "fk_penalty_record_app_user_reviewed_by_user_id",
                table: "penalty_record",
                column: "reviewed_by_user_id",
                principalTable: "app_user",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // Shape rules mirrored from the domain (2026-09-19). Small tables, plain CHECKs: cheap inside the 30 s service-start window.
            migrationBuilder.Sql(@"
ALTER TABLE penalty_record ADD CONSTRAINT ck_penalty_record_reviewed_by CHECK (reviewed_by_user_id IS NULL OR penalty_user = 'DataEntry');
ALTER TABLE collection ADD CONSTRAINT ck_collection_outsource_within_total CHECK (outsource_income >= 0 AND outsource_income <= cash + bank);
ALTER TABLE collection ADD CONSTRAINT ck_collection_reference_iff_bank CHECK (reference_number IS NULL OR bank > 0);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
ALTER TABLE collection DROP CONSTRAINT IF EXISTS ck_collection_reference_iff_bank;
ALTER TABLE collection DROP CONSTRAINT IF EXISTS ck_collection_outsource_within_total;
ALTER TABLE penalty_record DROP CONSTRAINT IF EXISTS ck_penalty_record_reviewed_by;");

            migrationBuilder.DropForeignKey(
                name: "fk_penalty_record_app_user_reviewed_by_user_id",
                table: "penalty_record");

            migrationBuilder.DropIndex(
                name: "ix_penalty_record_reviewed_by_user_id",
                table: "penalty_record");

            migrationBuilder.DropIndex(
                name: "ix_collection_reference_number",
                table: "collection");

            migrationBuilder.DropColumn(
                name: "reviewed_by_user_id",
                table: "penalty_record");

            migrationBuilder.DropColumn(
                name: "outsource_income",
                table: "collection");

            migrationBuilder.DropColumn(
                name: "reference_number",
                table: "collection");
        }
    }
}
