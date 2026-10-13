using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Futsal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BookingEngine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:booking_source", "ONLINE,STAFF,RECURRING")
                .Annotation("Npgsql:Enum:booking_status", "HELD,CONFIRMED,COMPLETED,CANCELLED,EXPIRED,NO_SHOW")
                .Annotation("Npgsql:Enum:member_role", "OWNER,MANAGER,CASHIER")
                .Annotation("Npgsql:Enum:payment_method", "CASH,ESEWA,KHALTI,FONEPAY,BANK,OTHER")
                .Annotation("Npgsql:Enum:payment_status", "PENDING,COMPLETED,FAILED,NEEDS_REFUND,REFUNDED")
                .Annotation("Npgsql:PostgresExtension:btree_gist", ",,")
                .Annotation("Npgsql:PostgresExtension:citext", ",,")
                .Annotation("Npgsql:PostgresExtension:pgcrypto", ",,")
                .OldAnnotation("Npgsql:Enum:member_role", "OWNER,MANAGER,CASHIER")
                .OldAnnotation("Npgsql:PostgresExtension:btree_gist", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:citext", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:pgcrypto", ",,");

            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS btree_gist;");

            migrationBuilder.AddColumn<string>(
                name: "advance_type",
                table: "venues",
                type: "text",
                nullable: false,
                defaultValue: "PERCENT");

            migrationBuilder.AddColumn<decimal>(
                name: "advance_value",
                table: "venues",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                defaultValue: 30m);

            migrationBuilder.AddColumn<int>(
                name: "free_cancel_hours",
                table: "venues",
                type: "integer",
                nullable: false,
                defaultValue: 6);

            migrationBuilder.AddColumn<int>(
                name: "hold_minutes",
                table: "venues",
                type: "integer",
                nullable: false,
                defaultValue: 10);

            migrationBuilder.AddColumn<bool>(
                name: "is_active",
                table: "venues",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "timezone",
                table: "venues",
                type: "text",
                nullable: false,
                defaultValue: "Asia/Kathmandu");

            migrationBuilder.AddColumn<int>(
                name: "slot_minutes",
                table: "courts",
                type: "integer",
                nullable: false,
                defaultValue: 60);

            migrationBuilder.CreateTable(
                name: "blocked_periods",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    court_id = table.Column<Guid>(type: "uuid", nullable: false),
                    start_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    end_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_blocked_periods", x => x.id);
                    table.CheckConstraint("ck_blocked_periods_time", "end_at > start_at");
                    table.ForeignKey(
                        name: "FK_blocked_periods_courts_court_id",
                        column: x => x.court_id,
                        principalTable: "courts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "court_hours",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    court_id = table.Column<Guid>(type: "uuid", nullable: false),
                    weekday = table.Column<short>(type: "smallint", nullable: false),
                    open_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    close_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_court_hours", x => x.id);
                    table.CheckConstraint("ck_court_hours_weekday", "weekday between 0 and 6");
                    table.ForeignKey(
                        name: "FK_court_hours_courts_court_id",
                        column: x => x.court_id,
                        principalTable: "courts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "customers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    full_name = table.Column<string>(type: "text", nullable: false),
                    phone = table.Column<string>(type: "text", nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true),
                    is_regular = table.Column<bool>(type: "boolean", nullable: false),
                    discount_percent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    no_show_count = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customers", x => x.id);
                    table.CheckConstraint("ck_customers_discount", "discount_percent >= 0 AND discount_percent <= 100");
                    table.ForeignKey(
                        name: "FK_customers_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_customers_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "idempotency_keys",
                columns: table => new
                {
                    key = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    endpoint = table.Column<string>(type: "text", nullable: false),
                    response_status = table.Column<int>(type: "integer", nullable: true),
                    response_body = table.Column<string>(type: "jsonb", nullable: true),
                    request_hash = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_idempotency_keys", x => new { x.key, x.user_id, x.endpoint });
                });

            migrationBuilder.CreateTable(
                name: "pricing_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    court_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    weekdays = table.Column<short[]>(type: "smallint[]", nullable: false),
                    from_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    to_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    price_per_slot = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: true),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pricing_rules", x => x.id);
                    table.ForeignKey(
                        name: "FK_pricing_rules_courts_court_id",
                        column: x => x.court_id,
                        principalTable: "courts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "bookings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    venue_id = table.Column<Guid>(type: "uuid", nullable: false),
                    court_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    booking_code = table.Column<string>(type: "text", nullable: false),
                    start_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    end_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<int>(type: "booking_status", nullable: false),
                    source = table.Column<int>(type: "booking_source", nullable: false),
                    hold_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    total_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    discount_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false, defaultValue: 0m),
                    advance_required = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false, defaultValue: 0m),
                    paid_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false, defaultValue: 0m),
                    notes = table.Column<string>(type: "text", nullable: true),
                    cancel_reason = table.Column<string>(type: "text", nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bookings", x => x.id);
                    table.CheckConstraint("ck_bookings_paid", "paid_amount >= 0");
                    table.CheckConstraint("ck_bookings_time", "end_at > start_at");
                    table.ForeignKey(
                        name: "FK_bookings_courts_court_id",
                        column: x => x.court_id,
                        principalTable: "courts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_bookings_customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_bookings_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_bookings_users_created_by",
                        column: x => x.created_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_bookings_venues_venue_id",
                        column: x => x.venue_id,
                        principalTable: "venues",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: true),
                    method = table.Column<int>(type: "payment_method", nullable: false),
                    status = table.Column<int>(type: "payment_status", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    gateway_txn_uuid = table.Column<string>(type: "text", nullable: true),
                    gateway_ref = table.Column<string>(type: "text", nullable: true),
                    raw_response = table.Column<string>(type: "jsonb", nullable: true),
                    received_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payments", x => x.id);
                    table.CheckConstraint("ck_payments_nonzero", "amount <> 0");
                    table.ForeignKey(
                        name: "FK_payments_bookings_booking_id",
                        column: x => x.booking_id,
                        principalTable: "bookings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_payments_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_payments_users_received_by",
                        column: x => x.received_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_blocked_periods_court_id",
                table: "blocked_periods",
                column: "court_id");

            migrationBuilder.CreateIndex(
                name: "IX_bookings_booking_code",
                table: "bookings",
                column: "booking_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_bookings_court_id",
                table: "bookings",
                column: "court_id");

            migrationBuilder.CreateIndex(
                name: "IX_bookings_created_by",
                table: "bookings",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_bookings_customer",
                table: "bookings",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_bookings_hold",
                table: "bookings",
                column: "hold_expires_at",
                filter: "status = 'HELD'");

            migrationBuilder.CreateIndex(
                name: "IX_bookings_organization_id",
                table: "bookings",
                column: "organization_id");

            migrationBuilder.CreateIndex(
                name: "ix_bookings_venue_start",
                table: "bookings",
                columns: new[] { "venue_id", "start_at" });

            migrationBuilder.CreateIndex(
                name: "IX_court_hours_court_id_weekday",
                table: "court_hours",
                columns: new[] { "court_id", "weekday" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_customers_organization_id_phone",
                table: "customers",
                columns: new[] { "organization_id", "phone" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_customers_user_id",
                table: "customers",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_payments_booking_id",
                table: "payments",
                column: "booking_id");

            migrationBuilder.CreateIndex(
                name: "IX_payments_gateway_txn_uuid",
                table: "payments",
                column: "gateway_txn_uuid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_payments_organization_id",
                table: "payments",
                column: "organization_id");

            migrationBuilder.CreateIndex(
                name: "IX_payments_received_by",
                table: "payments",
                column: "received_by");

            migrationBuilder.CreateIndex(
                name: "IX_pricing_rules_court_id",
                table: "pricing_rules",
                column: "court_id");

            migrationBuilder.Sql("ALTER TABLE bookings ADD CONSTRAINT bookings_no_overlap EXCLUDE USING gist (court_id WITH =, tstzrange(start_at, end_at, '[)') WITH &&) WHERE (status NOT IN ('CANCELLED','EXPIRED'));");
            migrationBuilder.Sql("ALTER TABLE blocked_periods ADD CONSTRAINT blocked_periods_no_overlap EXCLUDE USING gist (court_id WITH =, tstzrange(start_at, end_at, '[)') WITH &&);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE bookings DROP CONSTRAINT IF EXISTS bookings_no_overlap;");
            migrationBuilder.Sql("ALTER TABLE blocked_periods DROP CONSTRAINT IF EXISTS blocked_periods_no_overlap;");
            migrationBuilder.DropTable(
                name: "blocked_periods");

            migrationBuilder.DropTable(
                name: "court_hours");

            migrationBuilder.DropTable(
                name: "idempotency_keys");

            migrationBuilder.DropTable(
                name: "payments");

            migrationBuilder.DropTable(
                name: "pricing_rules");

            migrationBuilder.DropTable(
                name: "bookings");

            migrationBuilder.DropTable(
                name: "customers");

            migrationBuilder.DropColumn(
                name: "advance_type",
                table: "venues");

            migrationBuilder.DropColumn(
                name: "advance_value",
                table: "venues");

            migrationBuilder.DropColumn(
                name: "free_cancel_hours",
                table: "venues");

            migrationBuilder.DropColumn(
                name: "hold_minutes",
                table: "venues");

            migrationBuilder.DropColumn(
                name: "is_active",
                table: "venues");

            migrationBuilder.DropColumn(
                name: "timezone",
                table: "venues");

            migrationBuilder.DropColumn(
                name: "slot_minutes",
                table: "courts");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:member_role", "OWNER,MANAGER,CASHIER")
                .Annotation("Npgsql:PostgresExtension:btree_gist", ",,")
                .Annotation("Npgsql:PostgresExtension:citext", ",,")
                .Annotation("Npgsql:PostgresExtension:pgcrypto", ",,")
                .OldAnnotation("Npgsql:Enum:booking_source", "ONLINE,STAFF,RECURRING")
                .OldAnnotation("Npgsql:Enum:booking_status", "HELD,CONFIRMED,COMPLETED,CANCELLED,EXPIRED,NO_SHOW")
                .OldAnnotation("Npgsql:Enum:member_role", "OWNER,MANAGER,CASHIER")
                .OldAnnotation("Npgsql:Enum:payment_method", "CASH,ESEWA,KHALTI,FONEPAY,BANK,OTHER")
                .OldAnnotation("Npgsql:Enum:payment_status", "PENDING,COMPLETED,FAILED,NEEDS_REFUND,REFUNDED")
                .OldAnnotation("Npgsql:PostgresExtension:btree_gist", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:citext", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:pgcrypto", ",,");
        }
    }
}
