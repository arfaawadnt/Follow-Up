using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace FollowUp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountingModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "collection",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    serial = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    laboratory_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    rep_ids = table.Column<string>(type: "jsonb", nullable: false),
                    cash = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    bank = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    iban = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    done_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, defaultValue: "system"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_collection", x => x.id);
                    table.ForeignKey(
                        name: "fk_collection_laboratories_laboratory_id",
                        column: x => x.laboratory_id,
                        principalTable: "laboratory",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "deduction",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    serial = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    area_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    value = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    period_from = table.Column<DateOnly>(type: "date", nullable: true),
                    period_to = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, defaultValue: "system"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_deduction", x => x.id);
                    table.ForeignKey(
                        name: "fk_deduction_area_area_id",
                        column: x => x.area_id,
                        principalTable: "area",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "penalty_record",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    serial = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    laboratory_id = table.Column<Guid>(type: "uuid", nullable: false),
                    acc_no = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    patient_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    wrong_test_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    wrong_test_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    wrong_value = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    right_test_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    right_test_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    right_value = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    penalty_user = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, defaultValue: "system"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_penalty_record", x => x.id);
                    table.ForeignKey(
                        name: "fk_penalty_record_laboratory_laboratory_id",
                        column: x => x.laboratory_id,
                        principalTable: "laboratory",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "rep_income_entry",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    serial = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    representative_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, defaultValue: "system"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rep_income_entry", x => x.id);
                    table.ForeignKey(
                        name: "fk_rep_income_entry_representatives_representative_id",
                        column: x => x.representative_id,
                        principalTable: "representative",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "treasury",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    branches = table.Column<string>(type: "jsonb", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, defaultValue: "system"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_treasury", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "treasury_reason",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, defaultValue: "system"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_treasury_reason", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "treasury_entry",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    serial = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    treasury_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    debit = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    credit = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    reason_id = table.Column<Guid>(type: "uuid", nullable: false),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, defaultValue: "system"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_treasury_entry", x => x.id);
                    table.ForeignKey(
                        name: "fk_treasury_entry_treasury_reasons_reason_id",
                        column: x => x.reason_id,
                        principalTable: "treasury_reason",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_treasury_entry_treasury_treasury_id",
                        column: x => x.treasury_id,
                        principalTable: "treasury",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_collection_date",
                table: "collection",
                column: "date");

            migrationBuilder.CreateIndex(
                name: "ix_collection_laboratory_id_date",
                table: "collection",
                columns: new[] { "laboratory_id", "date" });

            migrationBuilder.CreateIndex(
                name: "ix_collection_serial",
                table: "collection",
                column: "serial",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_deduction_area_id_date",
                table: "deduction",
                columns: new[] { "area_id", "date" });

            migrationBuilder.CreateIndex(
                name: "ix_deduction_serial",
                table: "deduction",
                column: "serial",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_penalty_record_date",
                table: "penalty_record",
                column: "date");

            migrationBuilder.CreateIndex(
                name: "ix_penalty_record_laboratory_id_date",
                table: "penalty_record",
                columns: new[] { "laboratory_id", "date" });

            migrationBuilder.CreateIndex(
                name: "ix_penalty_record_serial",
                table: "penalty_record",
                column: "serial",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_rep_income_entry_representative_id_date",
                table: "rep_income_entry",
                columns: new[] { "representative_id", "date" });

            migrationBuilder.CreateIndex(
                name: "ix_rep_income_entry_serial",
                table: "rep_income_entry",
                column: "serial",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_treasury_name",
                table: "treasury",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_treasury_entry_reason_id",
                table: "treasury_entry",
                column: "reason_id");

            migrationBuilder.CreateIndex(
                name: "ix_treasury_entry_serial",
                table: "treasury_entry",
                column: "serial",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_treasury_entry_treasury_id_date",
                table: "treasury_entry",
                columns: new[] { "treasury_id", "date" });

            migrationBuilder.CreateIndex(
                name: "ix_treasury_reason_name",
                table: "treasury_reason",
                column: "name",
                unique: true);

            // Belt-and-suspenders for the Accounting domain invariants (same convention as the segment-band, outsource and
            // area-percentage CHECKs): the ledgers must hold even against a write that bypasses the domain, because the
            // Deductions and Rep Statement reports sum these columns directly.
            migrationBuilder.Sql(@"
ALTER TABLE treasury_entry ADD CONSTRAINT ck_treasury_entry_non_negative CHECK (debit >= 0 AND credit >= 0);
ALTER TABLE treasury_entry ADD CONSTRAINT ck_treasury_entry_one_sided
    CHECK ((debit > 0 AND credit = 0) OR (credit > 0 AND debit = 0));
ALTER TABLE penalty_record ADD CONSTRAINT ck_penalty_record_non_negative CHECK (wrong_value >= 0 AND right_value >= 0);
ALTER TABLE deduction ADD CONSTRAINT ck_deduction_non_negative CHECK (""value"" >= 0);
ALTER TABLE deduction ADD CONSTRAINT ck_deduction_period
    CHECK ((period_from IS NULL AND period_to IS NULL)
        OR (period_from IS NOT NULL AND period_to IS NOT NULL AND period_to >= period_from));
ALTER TABLE collection ADD CONSTRAINT ck_collection_non_negative CHECK (cash >= 0 AND bank >= 0);
ALTER TABLE collection ADD CONSTRAINT ck_collection_has_amount CHECK (cash + bank > 0);
ALTER TABLE collection ADD CONSTRAINT ck_collection_iban_iff_bank
    CHECK ((bank > 0 AND iban IS NOT NULL) OR (bank = 0 AND iban IS NULL));
ALTER TABLE rep_income_entry ADD CONSTRAINT ck_rep_income_entry_positive CHECK (amount > 0);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop the CHECKs explicitly before their tables (PostgreSQL would cascade; be deliberate and symmetric).
            migrationBuilder.Sql(@"
ALTER TABLE rep_income_entry DROP CONSTRAINT IF EXISTS ck_rep_income_entry_positive;
ALTER TABLE collection DROP CONSTRAINT IF EXISTS ck_collection_iban_iff_bank;
ALTER TABLE collection DROP CONSTRAINT IF EXISTS ck_collection_has_amount;
ALTER TABLE collection DROP CONSTRAINT IF EXISTS ck_collection_non_negative;
ALTER TABLE deduction DROP CONSTRAINT IF EXISTS ck_deduction_period;
ALTER TABLE deduction DROP CONSTRAINT IF EXISTS ck_deduction_non_negative;
ALTER TABLE penalty_record DROP CONSTRAINT IF EXISTS ck_penalty_record_non_negative;
ALTER TABLE treasury_entry DROP CONSTRAINT IF EXISTS ck_treasury_entry_one_sided;
ALTER TABLE treasury_entry DROP CONSTRAINT IF EXISTS ck_treasury_entry_non_negative;");

            migrationBuilder.DropTable(
                name: "collection");

            migrationBuilder.DropTable(
                name: "deduction");

            migrationBuilder.DropTable(
                name: "penalty_record");

            migrationBuilder.DropTable(
                name: "rep_income_entry");

            migrationBuilder.DropTable(
                name: "treasury_entry");

            migrationBuilder.DropTable(
                name: "treasury_reason");

            migrationBuilder.DropTable(
                name: "treasury");
        }
    }
}
