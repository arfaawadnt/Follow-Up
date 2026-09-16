using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FollowUp.Infrastructure.Migrations
{
    /// <summary>
    /// 2026-09-16 — penalty user type "LabRequest" (the lab asked for the wrong test; charged to the lab via the Rep Income
    /// sheet, never deducted from the area). SQL-only: ck_penalty_record_performed_by now also requires such a row to name
    /// nobody, while Rep still needs a representative and DataEntry / Technician a system user. No model change.
    /// </summary>
    public partial class PenaltyLabRequest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE penalty_record DROP CONSTRAINT IF EXISTS ck_penalty_record_performed_by;");
            migrationBuilder.Sql(@"
ALTER TABLE penalty_record ADD CONSTRAINT ck_penalty_record_performed_by CHECK (
    NOT (performed_by_user_id IS NOT NULL AND performed_by_rep_id IS NOT NULL)
    AND (performed_by_rep_id IS NULL OR penalty_user = 'Rep')
    AND (performed_by_user_id IS NULL OR penalty_user IN ('DataEntry','Technician')));");

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Back to the pre-LabRequest rule. By design this fails while any LabRequest row exists — a downgrade must first
            // retype or remove those penalties rather than silently invalidate them.
            migrationBuilder.Sql("ALTER TABLE penalty_record DROP CONSTRAINT IF EXISTS ck_penalty_record_performed_by;");
            migrationBuilder.Sql("ALTER TABLE penalty_record ADD CONSTRAINT ck_penalty_record_no_lab_request CHECK (penalty_user <> 'LabRequest');");
            migrationBuilder.Sql("ALTER TABLE penalty_record DROP CONSTRAINT ck_penalty_record_no_lab_request;");
            migrationBuilder.Sql(@"
ALTER TABLE penalty_record ADD CONSTRAINT ck_penalty_record_performed_by CHECK (
    NOT (performed_by_user_id IS NOT NULL AND performed_by_rep_id IS NOT NULL)
    AND (performed_by_rep_id IS NULL OR penalty_user = 'Rep')
    AND (performed_by_user_id IS NULL OR penalty_user <> 'Rep'));");

        }
    }
}
