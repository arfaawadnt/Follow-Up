using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FollowUp.Infrastructure.Migrations
{
    /// <summary>
    /// 2026-09-16 operator decisions:
    ///  1. The day-to-day "responsible" is per lab, not per area: rep type AreaResponsible becomes LabResponsible (id 5
    ///     kept, rows renamed), laboratory gains responsible_rep_id (Restrict FK), area loses area_responsible_id. Existing
    ///     area responsibles are carried to the labs of their area (by area name) so no assignment is lost.
    ///  2. A collection is the reps' act, not a lab's: collection loses laboratory_id and its rep_ids list becomes
    ///     rep_shares — [{"RepId": …, "Amount": …}] — the amount each rep handed in (Σ = cash + bank). Existing rows are
    ///     converted in place: a single rep takes the total; several reps split it equally (last one absorbs rounding).
    /// Hand-ordered so the data moves before the old columns go.
    /// </summary>
    public partial class LabResponsibleAndRepCollections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ---- 1a. Rep type rename + widened CHECK ---------------------------------------------------------------
            // The CHECK must go before the rename (the old list does not know the new name).
            migrationBuilder.Sql("ALTER TABLE representative DROP CONSTRAINT IF EXISTS ck_representative_type;");
            migrationBuilder.Sql("UPDATE representative SET type = 'LabResponsible' WHERE type = 'AreaResponsible';");
            migrationBuilder.Sql("ALTER TABLE representative ADD CONSTRAINT ck_representative_type " +
                "CHECK (type IN ('Collector','Marketing','Transfer','Scanning','LabResponsible','AreaManager'));");

            // ---- 1b. Lab responsible ---------------------------------------------------------------------------------
            migrationBuilder.AddColumn<Guid>(
                name: "responsible_rep_id",
                table: "laboratory",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_laboratory_responsible_rep_id",
                table: "laboratory",
                column: "responsible_rep_id");

            migrationBuilder.AddForeignKey(
                name: "fk_laboratory_representatives_responsible_rep_id",
                table: "laboratory",
                column: "responsible_rep_id",
                principalTable: "representative",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // Carry each area's responsible to the labs of that area (labs reference their area by name).
            migrationBuilder.Sql(@"
UPDATE laboratory l
   SET responsible_rep_id = a.area_responsible_id
  FROM area a
 WHERE a.area_responsible_id IS NOT NULL
   AND l.responsible_rep_id IS NULL
   AND l.area IS NOT NULL
   AND lower(trim(l.area)) = lower(trim(a.name));");

            // ---- 1c. Area loses its responsible ---------------------------------------------------------------------
            migrationBuilder.DropForeignKey(
                name: "fk_area_representatives_area_responsible_id",
                table: "area");

            migrationBuilder.DropIndex(
                name: "ix_area_area_responsible_id",
                table: "area");

            migrationBuilder.DropColumn(
                name: "area_responsible_id",
                table: "area");

            // ---- 2. Collection: per-rep shares, no lab -----------------------------------------------------------------
            migrationBuilder.RenameColumn(
                name: "rep_ids",
                table: "collection",
                newName: "rep_shares");

            // Old shape: ["guid", …]. New shape: [{"RepId":"guid","Amount":n}, …] (System.Text.Json default casing).
            // Only rows still in the old shape are converted (first element is a string), so a re-run is a no-op.
            migrationBuilder.Sql(@"
UPDATE collection c
   SET rep_shares = COALESCE((
        SELECT jsonb_agg(jsonb_build_object(
                   'RepId', r.id,
                   'Amount', CASE WHEN r.ord = n.cnt
                                  THEN (c.cash + c.bank) - round((c.cash + c.bank) / n.cnt, 2) * (n.cnt - 1)
                                  ELSE round((c.cash + c.bank) / n.cnt, 2) END)
               ORDER BY r.ord)
          FROM jsonb_array_elements_text(c.rep_shares) WITH ORDINALITY AS r(id, ord)
         CROSS JOIN (SELECT jsonb_array_length(c.rep_shares) AS cnt) n), '[]'::jsonb)
 WHERE jsonb_typeof(c.rep_shares) = 'array'
   AND jsonb_array_length(c.rep_shares) > 0
   AND jsonb_typeof(c.rep_shares -> 0) = 'string';");

            migrationBuilder.DropForeignKey(
                name: "fk_collection_laboratories_laboratory_id",
                table: "collection");

            migrationBuilder.DropIndex(
                name: "ix_collection_laboratory_id_date",
                table: "collection");

            migrationBuilder.DropColumn(
                name: "laboratory_id",
                table: "collection");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // ---- 2. Collection: back to a rep-id list. The lab link cannot be recovered (a collection no longer has a
            // lab), so laboratory_id comes back NULLABLE — the pre-change schema had it NOT NULL.
            migrationBuilder.Sql(@"
UPDATE collection c
   SET rep_shares = COALESCE((SELECT jsonb_agg(e -> 'RepId') FROM jsonb_array_elements(c.rep_shares) e), '[]'::jsonb)
 WHERE jsonb_typeof(c.rep_shares) = 'array'
   AND jsonb_array_length(c.rep_shares) > 0
   AND jsonb_typeof(c.rep_shares -> 0) = 'object';");

            migrationBuilder.RenameColumn(
                name: "rep_shares",
                table: "collection",
                newName: "rep_ids");

            migrationBuilder.AddColumn<Guid>(
                name: "laboratory_id",
                table: "collection",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_collection_laboratory_id_date",
                table: "collection",
                columns: new[] { "laboratory_id", "date" });

            migrationBuilder.AddForeignKey(
                name: "fk_collection_laboratories_laboratory_id",
                table: "collection",
                column: "laboratory_id",
                principalTable: "laboratory",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // ---- 1c. Area responsible back (unassigned; the per-lab assignment is not folded back into areas).
            migrationBuilder.AddColumn<Guid>(
                name: "area_responsible_id",
                table: "area",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_area_area_responsible_id",
                table: "area",
                column: "area_responsible_id");

            migrationBuilder.AddForeignKey(
                name: "fk_area_representatives_area_responsible_id",
                table: "area",
                column: "area_responsible_id",
                principalTable: "representative",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // ---- 1b. Lab responsible dropped.
            migrationBuilder.DropForeignKey(
                name: "fk_laboratory_representatives_responsible_rep_id",
                table: "laboratory");

            migrationBuilder.DropIndex(
                name: "ix_laboratory_responsible_rep_id",
                table: "laboratory");

            migrationBuilder.DropColumn(
                name: "responsible_rep_id",
                table: "laboratory");

            // ---- 1a. Rep type name back + previous CHECK.
            migrationBuilder.Sql("ALTER TABLE representative DROP CONSTRAINT IF EXISTS ck_representative_type;");
            migrationBuilder.Sql("UPDATE representative SET type = 'AreaResponsible' WHERE type = 'LabResponsible';");
            migrationBuilder.Sql("ALTER TABLE representative ADD CONSTRAINT ck_representative_type " +
                "CHECK (type IN ('Collector','Marketing','Transfer','Scanning','AreaResponsible','AreaManager'));");
        }
    }
}
