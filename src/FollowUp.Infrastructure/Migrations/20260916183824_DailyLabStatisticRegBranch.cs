using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FollowUp.Infrastructure.Migrations
{
    /// <summary>
    /// 2026-09-16 — daily_lab_statistic gains the registration branch code (branch, default "") and its unique key becomes
    /// (date, lab_code, branch) so the Lab Statistics page can filter by "Reg Branch". Existing rows keep branch "" until
    /// their window is re-synced from Oracle (Lab Statistics -> Sync from Oracle), which replaces them by branch-split rows.
    /// A plain (date, lab_code) index stays for the many "lab day total" readers.
    /// </summary>
    public partial class DailyLabStatisticRegBranch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_daily_lab_statistic_date_lab_code",
                table: "daily_lab_statistic");

            migrationBuilder.AddColumn<string>(
                name: "branch",
                table: "daily_lab_statistic",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "ix_daily_lab_statistic_date_lab_code",
                table: "daily_lab_statistic",
                columns: new[] { "date", "lab_code" });

            migrationBuilder.CreateIndex(
                name: "ix_daily_lab_statistic_date_lab_code_branch",
                table: "daily_lab_statistic",
                columns: new[] { "date", "lab_code", "branch" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_daily_lab_statistic_date_lab_code",
                table: "daily_lab_statistic");

            migrationBuilder.DropIndex(
                name: "ix_daily_lab_statistic_date_lab_code_branch",
                table: "daily_lab_statistic");

            migrationBuilder.DropColumn(
                name: "branch",
                table: "daily_lab_statistic");

            migrationBuilder.CreateIndex(
                name: "ix_daily_lab_statistic_date_lab_code",
                table: "daily_lab_statistic",
                columns: new[] { "date", "lab_code" },
                unique: true);
        }
    }
}
