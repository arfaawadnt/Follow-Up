using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FollowUp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RestrictLabCascadeDeletes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_complaint_laboratories_laboratory_id",
                table: "complaint");

            migrationBuilder.DropForeignKey(
                name: "fk_marketing_visit_laboratory_laboratory_id",
                table: "marketing_visit");

            migrationBuilder.DropForeignKey(
                name: "fk_outsource_sample_laboratory_laboratory_id",
                table: "outsource_sample");

            migrationBuilder.AddForeignKey(
                name: "fk_complaint_laboratories_laboratory_id",
                table: "complaint",
                column: "laboratory_id",
                principalTable: "laboratory",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_marketing_visit_laboratory_laboratory_id",
                table: "marketing_visit",
                column: "laboratory_id",
                principalTable: "laboratory",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_outsource_sample_laboratory_laboratory_id",
                table: "outsource_sample",
                column: "laboratory_id",
                principalTable: "laboratory",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_complaint_laboratories_laboratory_id",
                table: "complaint");

            migrationBuilder.DropForeignKey(
                name: "fk_marketing_visit_laboratory_laboratory_id",
                table: "marketing_visit");

            migrationBuilder.DropForeignKey(
                name: "fk_outsource_sample_laboratory_laboratory_id",
                table: "outsource_sample");

            migrationBuilder.AddForeignKey(
                name: "fk_complaint_laboratories_laboratory_id",
                table: "complaint",
                column: "laboratory_id",
                principalTable: "laboratory",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_marketing_visit_laboratory_laboratory_id",
                table: "marketing_visit",
                column: "laboratory_id",
                principalTable: "laboratory",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_outsource_sample_laboratory_laboratory_id",
                table: "outsource_sample",
                column: "laboratory_id",
                principalTable: "laboratory",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
