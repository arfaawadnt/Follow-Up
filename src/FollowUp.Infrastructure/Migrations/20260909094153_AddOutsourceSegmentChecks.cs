using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FollowUp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOutsourceSegmentChecks : Migration
    {
        // Finding BIZ-008: the outsource/segment tables added since the baseline had no DB-level CHECK constraints,
        // so a bad write (or a future code path bypassing the domain) could persist invalid data. These mirror the
        // domain invariants exactly (OutsourceSample.Create requires a positive quantity; a Segment income band is
        // non-negative and its upper bound is >= its lower bound), so existing valid rows already satisfy them.
        // Added as raw SQL to match the project's existing check-constraint style (SchemaHardening).
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE outsource_sample ADD CONSTRAINT ck_outsource_sample_quantity CHECK (quantity > 0);");
            migrationBuilder.Sql(
                "ALTER TABLE ref_item ADD CONSTRAINT ck_ref_item_target_income_nonneg " +
                "CHECK ((target_income_from IS NULL OR target_income_from >= 0) " +
                "AND (target_income_to IS NULL OR target_income_to >= 0));");
            migrationBuilder.Sql(
                "ALTER TABLE ref_item ADD CONSTRAINT ck_ref_item_target_income_order " +
                "CHECK (target_income_from IS NULL OR target_income_to IS NULL OR target_income_to >= target_income_from);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE outsource_sample DROP CONSTRAINT IF EXISTS ck_outsource_sample_quantity;");
            migrationBuilder.Sql("ALTER TABLE ref_item DROP CONSTRAINT IF EXISTS ck_ref_item_target_income_nonneg;");
            migrationBuilder.Sql("ALTER TABLE ref_item DROP CONSTRAINT IF EXISTS ck_ref_item_target_income_order;");
        }
    }
}
