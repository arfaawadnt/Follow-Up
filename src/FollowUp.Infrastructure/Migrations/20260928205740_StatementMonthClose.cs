using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FollowUp.Infrastructure.Migrations
{
    /// <summary>
    /// 2026-09-28 — statement_month_close: one row per Lab Responsible × month closed on the Rep Statement (stored closing balance,
    /// who / when, notes). Small new table + unique index (rep, year, month) + CHECK on the month: instant at service start.
    /// </summary>
    public partial class StatementMonthClose : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "statement_month_close",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    representative_id = table.Column<Guid>(type: "uuid", nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    month = table.Column<int>(type: "integer", nullable: false),
                    closing_balance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    closed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    closed_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, defaultValue: "system"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_statement_month_close", x => x.id);
                    table.ForeignKey(
                        name: "fk_statement_month_close_representative_representative_id",
                        column: x => x.representative_id,
                        principalTable: "representative",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_statement_month_close_representative_id_year_month",
                table: "statement_month_close",
                columns: new[] { "representative_id", "year", "month" },
                unique: true);

            // Shape rule mirrored from the domain (month 1..12); the unique index above keeps one close per rep and month.
            migrationBuilder.Sql("ALTER TABLE statement_month_close ADD CONSTRAINT ck_statement_month_close_month CHECK (month BETWEEN 1 AND 12 AND year BETWEEN 2000 AND 2100);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "statement_month_close");
        }
    }
}
