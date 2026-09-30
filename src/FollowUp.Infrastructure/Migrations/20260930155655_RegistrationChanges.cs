using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FollowUp.Infrastructure.Migrations
{
    /// <summary>
    /// 2026-09-30 — registration_change: the LDM REG_LOG edits (one row per changed REG column: old / new value, who, when) joined to
    /// the registration (accession, patient, creation time, reg date, branch, resolved lab), synced nightly per modification-date
    /// window for the Auditing → Registration Changes page; stats_email_subscription.include_reg_changes adds the email section.
    /// New table + indexes, one boolean column with a default: instant at service start.
    /// </summary>
    public partial class RegistrationChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "include_reg_changes",
                table: "stats_email_subscription",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "registration_change",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    trans_id = table.Column<long>(type: "bigint", nullable: false),
                    reg_key = table.Column<long>(type: "bigint", nullable: false),
                    acc_no = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    patient_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    reg_created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    reg_date = table.Column<DateOnly>(type: "date", nullable: true),
                    reg_branch_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    lab_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    column = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    old_value = table.Column<string>(type: "text", nullable: true),
                    new_value = table.Column<string>(type: "text", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    modified_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    modified_date = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_registration_change", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_registration_change_lab_code_modified_date",
                table: "registration_change",
                columns: new[] { "lab_code", "modified_date" });

            migrationBuilder.CreateIndex(
                name: "ix_registration_change_modified_date",
                table: "registration_change",
                column: "modified_date");

            migrationBuilder.CreateIndex(
                name: "ix_registration_change_reg_date",
                table: "registration_change",
                column: "reg_date");

            migrationBuilder.CreateIndex(
                name: "ix_registration_change_trans_id",
                table: "registration_change",
                column: "trans_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "registration_change");

            migrationBuilder.DropColumn(
                name: "include_reg_changes",
                table: "stats_email_subscription");
        }
    }
}
