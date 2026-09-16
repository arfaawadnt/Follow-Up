using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace FollowUp.Infrastructure.Migrations
{
    /// <summary>
    /// 2026-09-16 — Rep Statement "real income" becomes a per-lab daily sheet of the Lab Responsible: new table
    /// rep_lab_income (rep × lab × date, unique; samples, total_required, paid, delayed_payment, notes; Restrict FKs;
    /// CHECK ck_rep_lab_income_non_negative). visit_history gains total_required so archived visits keep the check-in
    /// figure the sheet shows next to the rep's entry (null on older rows).
    /// </summary>
    public partial class RepLabIncomeSheet : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "total_required",
                table: "visit_history",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "rep_lab_income",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    serial = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    representative_id = table.Column<Guid>(type: "uuid", nullable: false),
                    laboratory_id = table.Column<Guid>(type: "uuid", nullable: false),
                    samples = table.Column<int>(type: "integer", nullable: false),
                    total_required = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    paid = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    delayed_payment = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, defaultValue: "system"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rep_lab_income", x => x.id);
                    table.ForeignKey(
                        name: "fk_rep_lab_income_laboratory_laboratory_id",
                        column: x => x.laboratory_id,
                        principalTable: "laboratory",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_rep_lab_income_representatives_representative_id",
                        column: x => x.representative_id,
                        principalTable: "representative",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_rep_lab_income_laboratory_id_date",
                table: "rep_lab_income",
                columns: new[] { "laboratory_id", "date" });

            migrationBuilder.CreateIndex(
                name: "ix_rep_lab_income_representative_id_laboratory_id_date",
                table: "rep_lab_income",
                columns: new[] { "representative_id", "laboratory_id", "date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_rep_lab_income_serial",
                table: "rep_lab_income",
                column: "serial",
                unique: true);

            // Invariants doubled in the DB (like the other ledgers): counts/money non-negative, paid never above the total required.
            migrationBuilder.Sql(@"
ALTER TABLE rep_lab_income ADD CONSTRAINT ck_rep_lab_income_non_negative
    CHECK (samples >= 0 AND total_required >= 0 AND paid >= 0 AND delayed_payment >= 0 AND paid <= total_required);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE rep_lab_income DROP CONSTRAINT IF EXISTS ck_rep_lab_income_non_negative;");
            migrationBuilder.DropTable(
                name: "rep_lab_income");

            migrationBuilder.DropColumn(
                name: "total_required",
                table: "visit_history");
        }
    }
}
