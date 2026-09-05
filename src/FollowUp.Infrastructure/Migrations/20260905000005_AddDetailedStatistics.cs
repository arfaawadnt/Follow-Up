using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FollowUp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDetailedStatistics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "detailed_registration",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    lab_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    acc_no = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    patient_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    test_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    test_type = table.Column<int>(type: "integer", nullable: false),
                    test_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    patient_fee = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    insurance_fee = table.Column<decimal>(type: "numeric(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_detailed_registration", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_detailed_registration_date_lab_code",
                table: "detailed_registration",
                columns: new[] { "date", "lab_code" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "detailed_registration");
        }
    }
}
