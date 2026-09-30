using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FollowUp.Infrastructure.Migrations
{
    /// <summary>
    /// 2026-09-30 — rep_income_revision: the reviewer's actual figures beside one Rep Income sheet line (rep × lab × date), for the
    /// Rep Income Revision page. Small new table + unique index (rep, lab, date) + CHECK mirroring the domain rule: instant.
    /// </summary>
    public partial class RepIncomeRevision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "rep_income_revision",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    representative_id = table.Column<Guid>(type: "uuid", nullable: false),
                    laboratory_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    actual_income = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    actual_paid = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    actual_delayed_payment = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, defaultValue: "system"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rep_income_revision", x => x.id);
                    table.ForeignKey(
                        name: "fk_rep_income_revision_laboratory_laboratory_id",
                        column: x => x.laboratory_id,
                        principalTable: "laboratory",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_rep_income_revision_representatives_representative_id",
                        column: x => x.representative_id,
                        principalTable: "representative",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_rep_income_revision_laboratory_id_date",
                table: "rep_income_revision",
                columns: new[] { "laboratory_id", "date" });

            migrationBuilder.CreateIndex(
                name: "ix_rep_income_revision_representative_id_laboratory_id_date",
                table: "rep_income_revision",
                columns: new[] { "representative_id", "laboratory_id", "date" },
                unique: true);

            // Shape rule mirrored from the domain (RepIncomeRevision.Update): nothing negative, paid within the income.
            migrationBuilder.Sql("ALTER TABLE rep_income_revision ADD CONSTRAINT ck_rep_income_revision_amounts CHECK (actual_income >= 0 AND actual_paid >= 0 AND actual_delayed_payment >= 0 AND actual_paid <= actual_income);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "rep_income_revision");
        }
    }
}
