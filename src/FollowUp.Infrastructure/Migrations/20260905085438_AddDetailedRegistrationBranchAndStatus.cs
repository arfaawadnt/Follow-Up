using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FollowUp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDetailedRegistrationBranchAndStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "reg_branch_code",
                table: "detailed_registration",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "sample_status",
                table: "detailed_registration",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "test_status",
                table: "detailed_registration",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "reg_branch_code",
                table: "detailed_registration");

            migrationBuilder.DropColumn(
                name: "sample_status",
                table: "detailed_registration");

            migrationBuilder.DropColumn(
                name: "test_status",
                table: "detailed_registration");
        }
    }
}
