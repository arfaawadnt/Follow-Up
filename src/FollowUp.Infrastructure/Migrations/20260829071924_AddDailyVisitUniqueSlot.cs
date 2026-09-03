using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FollowUp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDailyVisitUniqueSlot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_daily_visit_laboratory_id_visit_date_scheduled_time",
                table: "daily_visit",
                columns: new[] { "laboratory_id", "visit_date", "scheduled_time" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_daily_visit_laboratory_id_visit_date_scheduled_time",
                table: "daily_visit");
        }
    }
}
