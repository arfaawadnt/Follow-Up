using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FollowUp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddComplaintStageCheck : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Second-line-of-defense CHECK for the complaint stage enumeration (CMP-11), mirroring the
            // ck_complaint_status constraint SchemaHardening already added for status.
            migrationBuilder.Sql(@"
ALTER TABLE complaint ADD CONSTRAINT ck_complaint_stage
  CHECK (stage IN ('Logged','Acknowledged','ValidityChecked','Investigation','BusinessOutcome','Resolution','RejectedInvalid'));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE complaint DROP CONSTRAINT IF EXISTS ck_complaint_stage;");
        }
    }
}
