using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FollowUp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTestStatisticBranch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_test_statistic_date_test_code_test_type",
                table: "test_statistic");

            migrationBuilder.AddColumn<string>(
                name: "branch",
                table: "test_statistic",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "ix_test_statistic_date_test_code_test_type_branch",
                table: "test_statistic",
                columns: new[] { "date", "test_code", "test_type", "branch" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_test_statistic_date_test_code_test_type_branch",
                table: "test_statistic");

            migrationBuilder.DropColumn(
                name: "branch",
                table: "test_statistic");

            migrationBuilder.CreateIndex(
                name: "ix_test_statistic_date_test_code_test_type",
                table: "test_statistic",
                columns: new[] { "date", "test_code", "test_type" },
                unique: true);
        }
    }
}
