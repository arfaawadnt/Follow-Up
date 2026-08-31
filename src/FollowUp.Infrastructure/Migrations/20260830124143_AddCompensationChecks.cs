using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FollowUp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCompensationChecks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Second-line-of-defense CHECKs for the compensation tables (CPN-13); SchemaHardening added none.
            // Money/points/targets/rates cannot be negative, and the loyalty formula needs at least one tier
            // (the domain guards these in CompensationConfig/RepCommission/LabLoyaltyLedger — this mirrors them
            // at the database, matching the ck_* constraints on the enumeration columns).
            migrationBuilder.Sql(@"
ALTER TABLE lab_loyalty_ledger ADD CONSTRAINT ck_lab_loyalty_ledger_nonneg
  CHECK (target >= 0 AND achieved >= 0 AND points >= 0);
ALTER TABLE rep_commission ADD CONSTRAINT ck_rep_commission_nonneg
  CHECK (target >= 0 AND achieved >= 0 AND base_salary >= 0 AND commission >= 0 AND bonus >= 0);
ALTER TABLE compensation_config ADD CONSTRAINT ck_compensation_config_nonneg
  CHECK (commission_rate_percent >= 0 AND bonus_threshold_percent >= 0 AND bonus_amount >= 0);
ALTER TABLE compensation_config ADD CONSTRAINT ck_compensation_config_tiers
  CHECK (jsonb_array_length(loyalty_tiers) >= 1);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
ALTER TABLE lab_loyalty_ledger DROP CONSTRAINT IF EXISTS ck_lab_loyalty_ledger_nonneg;
ALTER TABLE rep_commission DROP CONSTRAINT IF EXISTS ck_rep_commission_nonneg;
ALTER TABLE compensation_config DROP CONSTRAINT IF EXISTS ck_compensation_config_nonneg;
ALTER TABLE compensation_config DROP CONSTRAINT IF EXISTS ck_compensation_config_tiers;");
        }
    }
}
