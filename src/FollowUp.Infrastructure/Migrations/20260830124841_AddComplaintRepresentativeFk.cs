using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FollowUp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddComplaintRepresentativeFk : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // complaint.representative_id was an unconstrained Guid with no referential integrity (CMP-12). The
            // aggregate stores it as a raw Guid? (not a modeled relationship), so add the FK directly: ON DELETE
            // SET NULL keeps the complaint's history when an attributed representative is removed, dropping only
            // the stale link. LogComplaintHandler rejects an unknown id up front; this is the database guard.
            migrationBuilder.Sql(@"
ALTER TABLE complaint ADD CONSTRAINT fk_complaint_representative
  FOREIGN KEY (representative_id) REFERENCES representative (id) ON DELETE SET NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE complaint DROP CONSTRAINT IF EXISTS fk_complaint_representative;");
        }
    }
}
