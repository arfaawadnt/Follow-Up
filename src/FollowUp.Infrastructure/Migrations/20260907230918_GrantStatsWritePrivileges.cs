using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FollowUp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class GrantStatsWritePrivileges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Finding M-7: the lab/area stats import+sync commands now require the new AddLabStats/AddAreaStats
            // write privileges instead of the View* privileges. Grant them to the existing built-in
            // OperationsManager role so its operators keep their manual-sync ability (Admin gets them via the
            // seeder's All-backfill). Custom read-only roles intentionally lose the destructive-sync ability.
            migrationBuilder.Sql(@"
UPDATE role
SET privileges = privileges || '[""AddLabStats"",""AddAreaStats""]'::jsonb
WHERE name = 'OperationsManager' AND NOT (privileges @> '[""AddLabStats""]'::jsonb);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
UPDATE role
SET privileges = (privileges - 'AddLabStats') - 'AddAreaStats'
WHERE name = 'OperationsManager';");
        }
    }
}
