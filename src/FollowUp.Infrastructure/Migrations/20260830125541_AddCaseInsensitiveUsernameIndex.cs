using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FollowUp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCaseInsensitiveUsernameIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Replace the case-sensitive unique index with a functional one on lower(username) so uniqueness is
            // enforced case-insensitively, matching the ToLower lookup (IDN-7). "Admin" and "admin" can no longer
            // coexist, and the index serves the WHERE lower(username) = ... query.
            migrationBuilder.DropIndex(
                name: "ix_app_user_username",
                table: "app_user");
            migrationBuilder.Sql("CREATE UNIQUE INDEX ix_app_user_username_lower ON app_user (lower(username));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_app_user_username_lower;");
            migrationBuilder.CreateIndex(
                name: "ix_app_user_username",
                table: "app_user",
                column: "username",
                unique: true);
        }
    }
}
