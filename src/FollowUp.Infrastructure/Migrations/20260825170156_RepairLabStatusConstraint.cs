using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FollowUp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RepairLabStatusConstraint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Idempotent repair: replace ck_laboratory_status with the reference status set (incl. Interactive).
            migrationBuilder.Sql("UPDATE laboratory SET status = 'Interactive' WHERE status = 'New';");
            migrationBuilder.Sql("ALTER TABLE laboratory DROP CONSTRAINT IF EXISTS ck_laboratory_status;");
            migrationBuilder.Sql("ALTER TABLE laboratory ADD CONSTRAINT ck_laboratory_status " +
                "CHECK (status IN ('Scanned','Interactive','Active','Inactive','Stopped','Pending','Suspended','Churned'));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE laboratory DROP CONSTRAINT IF EXISTS ck_laboratory_status;");
            migrationBuilder.Sql("UPDATE laboratory SET status = 'New' WHERE status = 'Interactive';");
            migrationBuilder.Sql("ALTER TABLE laboratory ADD CONSTRAINT ck_laboratory_status " +
                "CHECK (status IN ('New','Scanned','Active','Inactive','Pending','Suspended','Stopped','Churned'));");
        }
    }
}
