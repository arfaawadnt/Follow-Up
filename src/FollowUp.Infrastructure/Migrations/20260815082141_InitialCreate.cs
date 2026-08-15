using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FollowUp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "app_setting",
                columns: table => new
                {
                    key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    value = table.Column<string>(type: "text", nullable: true),
                    is_secret = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, defaultValue: "system"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_app_setting", x => x.key);
                });

            migrationBuilder.CreateTable(
                name: "audit_entry",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actor = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    entity = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    entity_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    before_json = table.Column<string>(type: "jsonb", nullable: true),
                    after_json = table.Column<string>(type: "jsonb", nullable: true),
                    correlation_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_entry", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "city",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    governorate = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, defaultValue: "system"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_city", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "compensation_config",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    commission_rate_percent = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    bonus_threshold_percent = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    bonus_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    loyalty_tiers = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, defaultValue: "system"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_compensation_config", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "daily_lab_statistic",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    lab_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    registrations = table.Column<int>(type: "integer", nullable: false),
                    test_count = table.Column<int>(type: "integer", nullable: false),
                    income = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_daily_lab_statistic", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "electronic_signature",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    module = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    record_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    record_version = table.Column<long>(type: "bigint", nullable: false),
                    signer_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    signer_username = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    auth_level = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    meaning = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    content_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    signed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    signer_ip = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_electronic_signature", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "notification_delivery_log",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    recipient = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    event_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    queued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    last_error = table.Column<string>(type: "text", nullable: true),
                    last_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notification_delivery_log", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "notification_template",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    subject_en = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    subject_ar = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    body_en = table.Column<string>(type: "text", nullable: false),
                    body_ar = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, defaultValue: "system"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notification_template", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "oracle_config",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    interval_hours = table.Column<int>(type: "integer", nullable: false),
                    connection_string = table.Column<string>(type: "text", nullable: true),
                    queries = table.Column<string>(type: "jsonb", nullable: false),
                    last_sync_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_status = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_oracle_config", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ref_item",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name_en = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    name_ar = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, defaultValue: "system"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ref_item", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "representative",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    full_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    goal_duration = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    goal_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    metric = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    salary = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    target = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    phone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    branch = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    governorate = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, defaultValue: "system"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_representative", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "role",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    default_language = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    default_theme = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    scope = table.Column<string>(type: "jsonb", nullable: false),
                    is_built_in = table.Column<bool>(type: "boolean", nullable: false),
                    privileges = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, defaultValue: "system"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_role", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sample_tracking",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    area = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    count = table.Column<int>(type: "integer", nullable: false),
                    data_entry_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    data_entry_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    review_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    review_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    sort_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    sort_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, defaultValue: "system"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sample_tracking", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "test_group",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name_en = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    name_ar = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, defaultValue: "system"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_test_group", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "test_statistic",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    test_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_test_statistic", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "area",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    city_id = table.Column<Guid>(type: "uuid", nullable: false),
                    transportation_required = table.Column<bool>(type: "boolean", nullable: false),
                    transfer_reps = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, defaultValue: "system"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_area", x => x.id);
                    table.ForeignKey(
                        name: "fk_area_cities_city_id",
                        column: x => x.city_id,
                        principalTable: "city",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "laboratory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    segment = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    branch = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    governorate = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    area = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    payer = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    contract_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    schedule = table.Column<string>(type: "jsonb", nullable: false),
                    latitude = table.Column<double>(type: "double precision", nullable: true),
                    longitude = table.Column<double>(type: "double precision", nullable: true),
                    collector_rep_id = table.Column<Guid>(type: "uuid", nullable: true),
                    marketing_rep_id = table.Column<Guid>(type: "uuid", nullable: true),
                    monthly_target = table.Column<int>(type: "integer", nullable: false),
                    loyalty_points = table.Column<int>(type: "integer", nullable: false),
                    loyalty_tier = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, defaultValue: "system"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_laboratory", x => x.id);
                    table.ForeignKey(
                        name: "fk_laboratory_representatives_collector_rep_id",
                        column: x => x.collector_rep_id,
                        principalTable: "representative",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_laboratory_representatives_marketing_rep_id",
                        column: x => x.marketing_rep_id,
                        principalTable: "representative",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "rep_commission",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    representative_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period = table.Column<int>(type: "integer", nullable: false),
                    target = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    achieved = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    base_salary = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    commission = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    bonus = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    computed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rep_commission", x => x.id);
                    table.ForeignKey(
                        name: "fk_rep_commission_representatives_representative_id",
                        column: x => x.representative_id,
                        principalTable: "representative",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "app_user",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    username = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    password_algorithm = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    password_iterations = table.Column<int>(type: "integer", nullable: false),
                    password_salt = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    password_hash = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    phone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    language = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    representative_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    failed_login_count = table.Column<int>(type: "integer", nullable: false),
                    locked_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, defaultValue: "system"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_app_user", x => x.id);
                    table.ForeignKey(
                        name: "fk_app_user_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "role",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "test_setup",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name_en = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    name_ar = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, defaultValue: "system"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_test_setup", x => x.id);
                    table.ForeignKey(
                        name: "fk_test_setup_test_group_group_id",
                        column: x => x.group_id,
                        principalTable: "test_group",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "complaint",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    laboratory_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    via_channel = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    assigned_team = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    details = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    stage = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolved_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, defaultValue: "system"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_complaint", x => x.id);
                    table.ForeignKey(
                        name: "fk_complaint_laboratories_laboratory_id",
                        column: x => x.laboratory_id,
                        principalTable: "laboratory",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "contact_person",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    role = table.Column<int>(type: "integer", nullable: false),
                    phone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    birthday = table.Column<DateOnly>(type: "date", nullable: true),
                    laboratory_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contact_person", x => x.id);
                    table.ForeignKey(
                        name: "fk_contact_person_laboratory_laboratory_id",
                        column: x => x.laboratory_id,
                        principalTable: "laboratory",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "daily_visit",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    laboratory_id = table.Column<Guid>(type: "uuid", nullable: false),
                    collector_rep_id = table.Column<Guid>(type: "uuid", nullable: true),
                    visit_date = table.Column<DateOnly>(type: "date", nullable: false),
                    scheduled_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    sample_count = table.Column<int>(type: "integer", nullable: true),
                    checked_in_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    checked_in_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    driver_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    driver_mobile = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    car_plate = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    transfer_rep_id = table.Column<Guid>(type: "uuid", nullable: true),
                    transfer_confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    admin_checked = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, defaultValue: "system"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_daily_visit", x => x.id);
                    table.ForeignKey(
                        name: "fk_daily_visit_laboratories_laboratory_id",
                        column: x => x.laboratory_id,
                        principalTable: "laboratory",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_daily_visit_representatives_collector_rep_id",
                        column: x => x.collector_rep_id,
                        principalTable: "representative",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_daily_visit_representatives_transfer_rep_id",
                        column: x => x.transfer_rep_id,
                        principalTable: "representative",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "lab_loyalty_ledger",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    laboratory_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period = table.Column<int>(type: "integer", nullable: false),
                    target = table.Column<int>(type: "integer", nullable: false),
                    achieved = table.Column<int>(type: "integer", nullable: false),
                    points = table.Column<int>(type: "integer", nullable: false),
                    tier = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    computed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lab_loyalty_ledger", x => x.id);
                    table.ForeignKey(
                        name: "fk_lab_loyalty_ledger_laboratories_laboratory_id",
                        column: x => x.laboratory_id,
                        principalTable: "laboratory",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "marketing_visit",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    laboratory_id = table.Column<Guid>(type: "uuid", nullable: false),
                    representative_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purpose = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    scheduled_date = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    outcome = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancellation_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, defaultValue: "system"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_marketing_visit", x => x.id);
                    table.ForeignKey(
                        name: "fk_marketing_visit_laboratory_laboratory_id",
                        column: x => x.laboratory_id,
                        principalTable: "laboratory",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_marketing_visit_representatives_representative_id",
                        column: x => x.representative_id,
                        principalTable: "representative",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "monthly_sample",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    laboratory_id = table.Column<Guid>(type: "uuid", nullable: false),
                    collector_rep_id = table.Column<Guid>(type: "uuid", nullable: true),
                    period = table.Column<int>(type: "integer", nullable: false),
                    sample_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_monthly_sample", x => x.id);
                    table.ForeignKey(
                        name: "fk_monthly_sample_laboratory_laboratory_id",
                        column: x => x.laboratory_id,
                        principalTable: "laboratory",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_monthly_sample_representatives_collector_rep_id",
                        column: x => x.collector_rep_id,
                        principalTable: "representative",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "outsource_sample",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    laboratory_id = table.Column<Guid>(type: "uuid", nullable: false),
                    visit_date = table.Column<DateOnly>(type: "date", nullable: false),
                    destination_lab = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, defaultValue: "system"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outsource_sample", x => x.id);
                    table.ForeignKey(
                        name: "fk_outsource_sample_laboratory_laboratory_id",
                        column: x => x.laboratory_id,
                        principalTable: "laboratory",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "visit_history",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    original_visit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    laboratory_id = table.Column<Guid>(type: "uuid", nullable: false),
                    collector_rep_id = table.Column<Guid>(type: "uuid", nullable: true),
                    visit_date = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    sample_count = table.Column<int>(type: "integer", nullable: true),
                    admin_checked = table.Column<bool>(type: "boolean", nullable: false),
                    archived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_visit_history", x => x.id);
                    table.ForeignKey(
                        name: "fk_visit_history_laboratory_laboratory_id",
                        column: x => x.laboratory_id,
                        principalTable: "laboratory",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "notification_preference",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    system = table.Column<bool>(type: "boolean", nullable: false),
                    mail = table.Column<bool>(type: "boolean", nullable: false),
                    whats_app = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notification_preference", x => x.id);
                    table.ForeignKey(
                        name: "fk_notification_preference_app_user_user_id",
                        column: x => x.user_id,
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "system_notification",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipient_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    body = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_system_notification", x => x.id);
                    table.ForeignKey(
                        name: "fk_system_notification_app_user_recipient_user_id",
                        column: x => x.recipient_user_id,
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_session",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ip = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    user_agent = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_session", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_session_app_user_user_id",
                        column: x => x.user_id,
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_app_user_representative_id",
                table: "app_user",
                column: "representative_id",
                unique: true,
                filter: "representative_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_app_user_role_id",
                table: "app_user",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ix_app_user_username",
                table: "app_user",
                column: "username",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_area_city_id",
                table: "area",
                column: "city_id");

            migrationBuilder.CreateIndex(
                name: "ix_audit_entry_entity_entity_id",
                table: "audit_entry",
                columns: new[] { "entity", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_entry_occurred_at",
                table: "audit_entry",
                column: "occurred_at");

            migrationBuilder.CreateIndex(
                name: "ix_city_governorate_name",
                table: "city",
                columns: new[] { "governorate", "name" });

            migrationBuilder.CreateIndex(
                name: "ix_complaint_laboratory_id",
                table: "complaint",
                column: "laboratory_id");

            migrationBuilder.CreateIndex(
                name: "ix_complaint_number",
                table: "complaint",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_complaint_status",
                table: "complaint",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_contact_person_laboratory_id",
                table: "contact_person",
                column: "laboratory_id");

            migrationBuilder.CreateIndex(
                name: "ix_daily_lab_statistic_date_lab_code",
                table: "daily_lab_statistic",
                columns: new[] { "date", "lab_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_daily_visit_collector_rep_id",
                table: "daily_visit",
                column: "collector_rep_id");

            migrationBuilder.CreateIndex(
                name: "ix_daily_visit_laboratory_id_visit_date",
                table: "daily_visit",
                columns: new[] { "laboratory_id", "visit_date" });

            migrationBuilder.CreateIndex(
                name: "ix_daily_visit_status",
                table: "daily_visit",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_daily_visit_transfer_rep_id",
                table: "daily_visit",
                column: "transfer_rep_id");

            migrationBuilder.CreateIndex(
                name: "ix_daily_visit_visit_date",
                table: "daily_visit",
                column: "visit_date");

            migrationBuilder.CreateIndex(
                name: "ix_electronic_signature_module_record_id",
                table: "electronic_signature",
                columns: new[] { "module", "record_id" });

            migrationBuilder.CreateIndex(
                name: "ix_lab_loyalty_ledger_laboratory_id_period",
                table: "lab_loyalty_ledger",
                columns: new[] { "laboratory_id", "period" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_laboratory_code",
                table: "laboratory",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_laboratory_collector_rep_id",
                table: "laboratory",
                column: "collector_rep_id");

            migrationBuilder.CreateIndex(
                name: "ix_laboratory_governorate",
                table: "laboratory",
                column: "governorate");

            migrationBuilder.CreateIndex(
                name: "ix_laboratory_marketing_rep_id",
                table: "laboratory",
                column: "marketing_rep_id");

            migrationBuilder.CreateIndex(
                name: "ix_laboratory_status",
                table: "laboratory",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_marketing_visit_laboratory_id",
                table: "marketing_visit",
                column: "laboratory_id");

            migrationBuilder.CreateIndex(
                name: "ix_marketing_visit_representative_id",
                table: "marketing_visit",
                column: "representative_id");

            migrationBuilder.CreateIndex(
                name: "ix_marketing_visit_status_scheduled_date",
                table: "marketing_visit",
                columns: new[] { "status", "scheduled_date" });

            migrationBuilder.CreateIndex(
                name: "ix_monthly_sample_collector_rep_id",
                table: "monthly_sample",
                column: "collector_rep_id");

            migrationBuilder.CreateIndex(
                name: "ix_monthly_sample_laboratory_id_period",
                table: "monthly_sample",
                columns: new[] { "laboratory_id", "period" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_notification_delivery_log_status",
                table: "notification_delivery_log",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_notification_preference_user_id_event_key",
                table: "notification_preference",
                columns: new[] { "user_id", "event_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_notification_template_event_key",
                table: "notification_template",
                column: "event_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outsource_sample_laboratory_id",
                table: "outsource_sample",
                column: "laboratory_id");

            migrationBuilder.CreateIndex(
                name: "ix_outsource_sample_visit_date_laboratory_id",
                table: "outsource_sample",
                columns: new[] { "visit_date", "laboratory_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ref_item_type_code",
                table: "ref_item",
                columns: new[] { "type", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_rep_commission_representative_id_period",
                table: "rep_commission",
                columns: new[] { "representative_id", "period" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_role_name",
                table: "role",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sample_tracking_area_date",
                table: "sample_tracking",
                columns: new[] { "area", "date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_system_notification_recipient_user_id_read_at",
                table: "system_notification",
                columns: new[] { "recipient_user_id", "read_at" });

            migrationBuilder.CreateIndex(
                name: "ix_test_group_code",
                table: "test_group",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_test_setup_code",
                table: "test_setup",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_test_setup_group_id",
                table: "test_setup",
                column: "group_id");

            migrationBuilder.CreateIndex(
                name: "ix_test_statistic_date_test_code",
                table: "test_statistic",
                columns: new[] { "date", "test_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_session_token_hash",
                table: "user_session",
                column: "token_hash");

            migrationBuilder.CreateIndex(
                name: "ix_user_session_user_id_revoked_at",
                table: "user_session",
                columns: new[] { "user_id", "revoked_at" });

            migrationBuilder.CreateIndex(
                name: "ix_visit_history_laboratory_id_visit_date",
                table: "visit_history",
                columns: new[] { "laboratory_id", "visit_date" });

            migrationBuilder.CreateIndex(
                name: "ix_visit_history_visit_date",
                table: "visit_history",
                column: "visit_date");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "app_setting");

            migrationBuilder.DropTable(
                name: "area");

            migrationBuilder.DropTable(
                name: "audit_entry");

            migrationBuilder.DropTable(
                name: "compensation_config");

            migrationBuilder.DropTable(
                name: "complaint");

            migrationBuilder.DropTable(
                name: "contact_person");

            migrationBuilder.DropTable(
                name: "daily_lab_statistic");

            migrationBuilder.DropTable(
                name: "daily_visit");

            migrationBuilder.DropTable(
                name: "electronic_signature");

            migrationBuilder.DropTable(
                name: "lab_loyalty_ledger");

            migrationBuilder.DropTable(
                name: "marketing_visit");

            migrationBuilder.DropTable(
                name: "monthly_sample");

            migrationBuilder.DropTable(
                name: "notification_delivery_log");

            migrationBuilder.DropTable(
                name: "notification_preference");

            migrationBuilder.DropTable(
                name: "notification_template");

            migrationBuilder.DropTable(
                name: "oracle_config");

            migrationBuilder.DropTable(
                name: "outsource_sample");

            migrationBuilder.DropTable(
                name: "ref_item");

            migrationBuilder.DropTable(
                name: "rep_commission");

            migrationBuilder.DropTable(
                name: "sample_tracking");

            migrationBuilder.DropTable(
                name: "system_notification");

            migrationBuilder.DropTable(
                name: "test_setup");

            migrationBuilder.DropTable(
                name: "test_statistic");

            migrationBuilder.DropTable(
                name: "user_session");

            migrationBuilder.DropTable(
                name: "visit_history");

            migrationBuilder.DropTable(
                name: "city");

            migrationBuilder.DropTable(
                name: "test_group");

            migrationBuilder.DropTable(
                name: "app_user");

            migrationBuilder.DropTable(
                name: "laboratory");

            migrationBuilder.DropTable(
                name: "role");

            migrationBuilder.DropTable(
                name: "representative");
        }
    }
}
