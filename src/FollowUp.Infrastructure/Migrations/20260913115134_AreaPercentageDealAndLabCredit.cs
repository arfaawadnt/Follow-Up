using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FollowUp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AreaPercentageDealAndLabCredit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "credit",
                table: "laboratory",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "percentage",
                table: "area",
                type: "numeric(5,2)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "percentage_deal",
                table: "area",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Belt-and-suspenders for the invariants Area.SetPercentageDeal enforces in the domain (the same convention
            // as the segment-band and outsource CHECKs): a percentage is present exactly when the deal is on, and it is a
            // 0–100 value. A stale percentage can therefore never linger on a disabled deal, so the Accounting
            // "Deductions" report can safely multiply by it whenever percentage_deal is true.
            migrationBuilder.Sql(@"
ALTER TABLE area ADD CONSTRAINT ck_area_percentage_range
    CHECK (percentage IS NULL OR (percentage >= 0 AND percentage <= 100));
ALTER TABLE area ADD CONSTRAINT ck_area_percentage_deal
    CHECK ((percentage_deal AND percentage IS NOT NULL) OR ((NOT percentage_deal) AND percentage IS NULL));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop the CHECKs explicitly before the columns they reference (PostgreSQL would cascade them, but be
            // deliberate so the reverse is symmetric and readable).
            migrationBuilder.Sql(@"
ALTER TABLE area DROP CONSTRAINT IF EXISTS ck_area_percentage_deal;
ALTER TABLE area DROP CONSTRAINT IF EXISTS ck_area_percentage_range;");

            migrationBuilder.DropColumn(
                name: "credit",
                table: "laboratory");

            migrationBuilder.DropColumn(
                name: "percentage",
                table: "area");

            migrationBuilder.DropColumn(
                name: "percentage_deal",
                table: "area");
        }
    }
}
