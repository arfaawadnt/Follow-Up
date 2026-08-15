using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FollowUp.Infrastructure.Migrations;

/// <summary>
/// Second line of defense at the database (architect DB rules): CHECK constraints for every status/type
/// enumeration, and the append-only immutability of the audit trail (SRS FR-20 — triggers refuse
/// UPDATE/DELETE/TRUNCATE, binding even the table owner; the bounded retention purge is the only exception).
/// </summary>
public partial class SchemaHardening : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // --- Enumeration CHECK constraints ---
        migrationBuilder.Sql(@"
ALTER TABLE laboratory ADD CONSTRAINT ck_laboratory_status
  CHECK (status IN ('New','Scanned','Active','Inactive','Pending','Suspended','Stopped','Churned'));
ALTER TABLE laboratory ADD CONSTRAINT ck_laboratory_segment CHECK (segment IN ('A','B','C'));
ALTER TABLE representative ADD CONSTRAINT ck_representative_type
  CHECK (type IN ('Collector','Marketing','Transfer','Scanning'));
ALTER TABLE daily_visit ADD CONSTRAINT ck_daily_visit_status
  CHECK (status IN ('Pending','Visited','Missed','Received'));
ALTER TABLE outsource_sample ADD CONSTRAINT ck_outsource_status
  CHECK (status IN ('Collected','Sent','Received'));
ALTER TABLE complaint ADD CONSTRAINT ck_complaint_status
  CHECK (status IN ('Open','InProgress','Resolved'));
ALTER TABLE marketing_visit ADD CONSTRAINT ck_marketing_status
  CHECK (status IN ('Scheduled','Completed','Cancelled'));
ALTER TABLE electronic_signature ADD CONSTRAINT ck_signature_meaning
  CHECK (meaning IN ('Authorship','Review','Approval','Verification','Execution'));
");

        // --- Append-only audit trail (immutable at the DB) ---
        // A session GUC 'followup.allow_audit_purge' must be set to 'on' for the bounded retention purge to
        // delete rows; every other UPDATE/DELETE/TRUNCATE is refused, even for the table owner.
        migrationBuilder.Sql(@"
CREATE OR REPLACE FUNCTION followup_audit_immutable() RETURNS trigger AS $$
BEGIN
    IF (TG_OP = 'DELETE') THEN
        IF current_setting('followup.allow_audit_purge', true) = 'on' THEN
            RETURN OLD;
        END IF;
        RAISE EXCEPTION 'audit_entry is append-only: DELETE is not permitted';
    END IF;
    RAISE EXCEPTION 'audit_entry is append-only: % is not permitted', TG_OP;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_audit_no_update BEFORE UPDATE ON audit_entry
    FOR EACH ROW EXECUTE FUNCTION followup_audit_immutable();
CREATE TRIGGER trg_audit_no_delete BEFORE DELETE ON audit_entry
    FOR EACH ROW EXECUTE FUNCTION followup_audit_immutable();

CREATE OR REPLACE FUNCTION followup_audit_no_truncate() RETURNS trigger AS $$
BEGIN
    RAISE EXCEPTION 'audit_entry is append-only: TRUNCATE is not permitted';
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_audit_no_truncate BEFORE TRUNCATE ON audit_entry
    FOR EACH STATEMENT EXECUTE FUNCTION followup_audit_no_truncate();
");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
DROP TRIGGER IF EXISTS trg_audit_no_update ON audit_entry;
DROP TRIGGER IF EXISTS trg_audit_no_delete ON audit_entry;
DROP TRIGGER IF EXISTS trg_audit_no_truncate ON audit_entry;
DROP FUNCTION IF EXISTS followup_audit_immutable();
DROP FUNCTION IF EXISTS followup_audit_no_truncate();
");
        migrationBuilder.Sql(@"
ALTER TABLE laboratory DROP CONSTRAINT IF EXISTS ck_laboratory_status;
ALTER TABLE laboratory DROP CONSTRAINT IF EXISTS ck_laboratory_segment;
ALTER TABLE representative DROP CONSTRAINT IF EXISTS ck_representative_type;
ALTER TABLE daily_visit DROP CONSTRAINT IF EXISTS ck_daily_visit_status;
ALTER TABLE outsource_sample DROP CONSTRAINT IF EXISTS ck_outsource_status;
ALTER TABLE complaint DROP CONSTRAINT IF EXISTS ck_complaint_status;
ALTER TABLE marketing_visit DROP CONSTRAINT IF EXISTS ck_marketing_status;
ALTER TABLE electronic_signature DROP CONSTRAINT IF EXISTS ck_signature_meaning;
");
    }
}
