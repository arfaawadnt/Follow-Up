using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FollowUp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStatsEmailSubscriptionScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Finding B-7: stats email reports must render under the creating admin's org scope, not company-wide.
            // Add the column nullable first so existing rows can be backfilled before it becomes required.
            migrationBuilder.AddColumn<string>(
                name: "scope",
                table: "stats_email_subscription",
                type: "jsonb",
                nullable: true);

            // Backfill each existing subscription's scope from its creator's current role scope. Both columns are
            // written by the same OrgScopeConverter, so the serialized jsonb is copied verbatim — no reparsing.
            migrationBuilder.Sql(@"
UPDATE stats_email_subscription s
SET scope = r.scope
FROM app_user u
JOIN role r ON r.id = u.role_id
WHERE lower(u.username) = lower(s.created_by) AND s.scope IS NULL;");

            // Safety net for any row whose creator can't be resolved: fall back to the built-in Admin role's
            // (global) scope so the NOT NULL constraint can be applied.
            migrationBuilder.Sql(@"
UPDATE stats_email_subscription s
SET scope = (SELECT scope FROM role WHERE name = 'Admin' LIMIT 1)
WHERE s.scope IS NULL AND EXISTS (SELECT 1 FROM role WHERE name = 'Admin');");

            migrationBuilder.AlterColumn<string>(
                name: "scope",
                table: "stats_email_subscription",
                type: "jsonb",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "jsonb",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "scope",
                table: "stats_email_subscription");
        }
    }
}
