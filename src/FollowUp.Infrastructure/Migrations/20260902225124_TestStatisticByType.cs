using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FollowUp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TestStatisticByType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_test_statistic_date_test_code",
                table: "test_statistic");

            migrationBuilder.AddColumn<int>(
                name: "test_type",
                table: "test_statistic",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "ix_test_statistic_date_test_code_test_type",
                table: "test_statistic",
                columns: new[] { "date", "test_code", "test_type" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_test_statistic_date_test_code_test_type",
                table: "test_statistic");

            migrationBuilder.DropColumn(
                name: "test_type",
                table: "test_statistic");

            migrationBuilder.CreateIndex(
                name: "ix_test_statistic_date_test_code",
                table: "test_statistic",
                columns: new[] { "date", "test_code" },
                unique: true);
        }
    }
}
