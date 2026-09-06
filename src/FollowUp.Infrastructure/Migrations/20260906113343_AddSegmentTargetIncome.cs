using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FollowUp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSegmentTargetIncome : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "target_income_from",
                table: "ref_item",
                type: "numeric(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "target_income_to",
                table: "ref_item",
                type: "numeric(18,2)",
                nullable: true);

            // Segments are now configurable, income-banded reference data — drop the legacy A/B/C-only CHECK so labs
            // can be auto-assigned to any configured segment (e.g. VIP). Segment values stay validated at the
            // application layer (Laboratory.ReassignSegment / UpdateProfile).
            migrationBuilder.Sql("ALTER TABLE laboratory DROP CONSTRAINT IF EXISTS ck_laboratory_segment;");

            // Back-fill the default monthly target-income bands (EGP) on existing segment tiers. Idempotent, and a
            // no-op on a brand-new database (segments are created by the seeder, already carrying these bands).
            migrationBuilder.Sql("UPDATE ref_item SET target_income_from = 0,     target_income_to = 3000  WHERE type = 'Segment' AND code = 'C';");
            migrationBuilder.Sql("UPDATE ref_item SET target_income_from = 3001,  target_income_to = 6000  WHERE type = 'Segment' AND code = 'B';");
            migrationBuilder.Sql("UPDATE ref_item SET target_income_from = 6001,  target_income_to = 10000 WHERE type = 'Segment' AND code = 'A';");
            migrationBuilder.Sql("UPDATE ref_item SET target_income_from = 10001, target_income_to = NULL  WHERE type = 'Segment' AND code = 'VIP';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Restore the legacy A/B/C-only CHECK (mirrors the original schema-hardening constraint). Note: this will
            // fail if any lab has since been assigned a segment outside A/B/C (e.g. VIP) — reassign those first.
            migrationBuilder.Sql("ALTER TABLE laboratory ADD CONSTRAINT ck_laboratory_segment CHECK (segment IN ('A','B','C'));");

            migrationBuilder.DropColumn(
                name: "target_income_from",
                table: "ref_item");

            migrationBuilder.DropColumn(
                name: "target_income_to",
                table: "ref_item");
        }
    }
}
