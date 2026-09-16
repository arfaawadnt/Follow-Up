using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FollowUp.Infrastructure.Migrations
{
    /// <summary>
    /// 2026-09-16 — a LabRequest penalty may name only one test (the lab asked for a test it should not have, or missed
    /// one) and is charged for BOTH values: the four test columns become nullable and ck_penalty_record_tests keeps the
    /// shape honest (code ⇔ name, a missing test carries a zero value, staff penalties stay two-sided, at least one test).
    /// </summary>
    public partial class PenaltyLabRequestTests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "wrong_test_name",
                table: "penalty_record",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "wrong_test_code",
                table: "penalty_record",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32);

            migrationBuilder.AlterColumn<string>(
                name: "right_test_name",
                table: "penalty_record",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "right_test_code",
                table: "penalty_record",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32);

            migrationBuilder.Sql(@"
ALTER TABLE penalty_record ADD CONSTRAINT ck_penalty_record_tests CHECK (
    ((wrong_test_code IS NULL) = (wrong_test_name IS NULL))
    AND ((right_test_code IS NULL) = (right_test_name IS NULL))
    AND (wrong_test_code IS NOT NULL OR wrong_value = 0)
    AND (right_test_code IS NOT NULL OR right_value = 0)
    AND (wrong_test_code IS NOT NULL OR right_test_code IS NOT NULL)
    AND (penalty_user = 'LabRequest' OR (wrong_test_code IS NOT NULL AND right_test_code IS NOT NULL)));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE penalty_record DROP CONSTRAINT IF EXISTS ck_penalty_record_tests;");
            migrationBuilder.AlterColumn<string>(
                name: "wrong_test_name",
                table: "penalty_record",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "wrong_test_code",
                table: "penalty_record",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "right_test_name",
                table: "penalty_record",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "right_test_code",
                table: "penalty_record",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32,
                oldNullable: true);
        }
    }
}
