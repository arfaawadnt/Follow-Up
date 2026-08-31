using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FollowUp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAppUserIsBuiltIn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_built_in",
                table: "app_user",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Backfill the existing seeded admin so it is protected on databases that predate the flag (IDN-6).
            migrationBuilder.Sql("UPDATE app_user SET is_built_in = true WHERE username = 'admin';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_built_in",
                table: "app_user");
        }
    }
}
