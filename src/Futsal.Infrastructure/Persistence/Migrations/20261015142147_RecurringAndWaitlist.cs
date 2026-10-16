using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Futsal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RecurringAndWaitlist : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "recurring_horizon_weeks",
                table: "venues",
                type: "integer",
                nullable: false,
                defaultValue: 8);

            migrationBuilder.AddColumn<int>(
                name: "waitlist_no_show_threshold",
                table: "venues",
                type: "integer",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.AddColumn<int>(
                name: "waitlist_offer_minutes",
                table: "venues",
                type: "integer",
                nullable: false,
                defaultValue: 15);

            migrationBuilder.AddColumn<bool>(
                name: "weekly_series_summary",
                table: "venues",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "prepayment_block_id",
                table: "payments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "series_date",
                table: "bookings",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "series_id",
                table: "bookings",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "recurring_booking_series",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    court_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    weekday = table.Column<short>(type: "smallint", nullable: false),
                    start_time_local = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    duration_slots = table.Column<int>(type: "integer", nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    price_mode = table.Column<string>(type: "text", nullable: false),
                    locked_price_per_occurrence = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    apply_regular_discount = table.Column<bool>(type: "boolean", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    venue_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recurring_booking_series", x => x.id);
                    table.ForeignKey(
                        name: "FK_recurring_booking_series_courts_court_id",
                        column: x => x.court_id,
                        principalTable: "courts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_recurring_booking_series_customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_recurring_booking_series_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_recurring_booking_series_venues_venue_id",
                        column: x => x.venue_id,
                        principalTable: "venues",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "waitlist_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    court_id = table.Column<Guid>(type: "uuid", nullable: true),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    from_time_local = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    to_time_local = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    slots_needed = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    position = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    offered_booking_id = table.Column<Guid>(type: "uuid", nullable: true),
                    offer_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    requires_approval = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    venue_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_waitlist_entries", x => x.id);
                    table.ForeignKey(
                        name: "FK_waitlist_entries_bookings_offered_booking_id",
                        column: x => x.offered_booking_id,
                        principalTable: "bookings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_waitlist_entries_courts_court_id",
                        column: x => x.court_id,
                        principalTable: "courts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_waitlist_entries_customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_waitlist_entries_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_waitlist_entries_venues_venue_id",
                        column: x => x.venue_id,
                        principalTable: "venues",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "recurring_series_exceptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    series_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    type = table.Column<string>(type: "text", nullable: false),
                    moved_to_start_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reason = table.Column<string>(type: "text", nullable: true),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    venue_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recurring_series_exceptions", x => x.id);
                    table.ForeignKey(
                        name: "FK_recurring_series_exceptions_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_recurring_series_exceptions_recurring_booking_series_series~",
                        column: x => x.series_id,
                        principalTable: "recurring_booking_series",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_recurring_series_exceptions_venues_venue_id",
                        column: x => x.venue_id,
                        principalTable: "venues",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "recurring_series_revisions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    series_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_date = table.Column<DateOnly>(type: "date", nullable: false),
                    court_id = table.Column<Guid>(type: "uuid", nullable: false),
                    start_time_local = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    duration_slots = table.Column<int>(type: "integer", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    venue_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recurring_series_revisions", x => x.id);
                    table.ForeignKey(
                        name: "FK_recurring_series_revisions_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_recurring_series_revisions_recurring_booking_series_series_~",
                        column: x => x.series_id,
                        principalTable: "recurring_booking_series",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_recurring_series_revisions_venues_venue_id",
                        column: x => x.venue_id,
                        principalTable: "venues",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "series_conflicts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    series_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    venue_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_series_conflicts", x => x.id);
                    table.ForeignKey(
                        name: "FK_series_conflicts_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_series_conflicts_recurring_booking_series_series_id",
                        column: x => x.series_id,
                        principalTable: "recurring_booking_series",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_series_conflicts_venues_venue_id",
                        column: x => x.venue_id,
                        principalTable: "venues",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "series_prepayments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    series_id = table.Column<Guid>(type: "uuid", nullable: false),
                    weeks = table.Column<int>(type: "integer", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    venue_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_series_prepayments", x => x.id);
                    table.ForeignKey(
                        name: "FK_series_prepayments_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_series_prepayments_recurring_booking_series_series_id",
                        column: x => x.series_id,
                        principalTable: "recurring_booking_series",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_series_prepayments_venues_venue_id",
                        column: x => x.venue_id,
                        principalTable: "venues",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_payments_prepayment_block_id",
                table: "payments",
                column: "prepayment_block_id");

            migrationBuilder.CreateIndex(
                name: "IX_bookings_series_id_series_date",
                table: "bookings",
                columns: new[] { "series_id", "series_date" },
                unique: true,
                filter: "series_id IS NOT NULL AND status NOT IN ('CANCELLED','EXPIRED')");

            migrationBuilder.CreateIndex(
                name: "IX_recurring_booking_series_court_id",
                table: "recurring_booking_series",
                column: "court_id");

            migrationBuilder.CreateIndex(
                name: "IX_recurring_booking_series_customer_id",
                table: "recurring_booking_series",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "IX_recurring_booking_series_organization_id",
                table: "recurring_booking_series",
                column: "organization_id");

            migrationBuilder.CreateIndex(
                name: "IX_recurring_booking_series_venue_id",
                table: "recurring_booking_series",
                column: "venue_id");

            migrationBuilder.CreateIndex(
                name: "IX_recurring_series_exceptions_organization_id",
                table: "recurring_series_exceptions",
                column: "organization_id");

            migrationBuilder.CreateIndex(
                name: "IX_recurring_series_exceptions_series_id_date",
                table: "recurring_series_exceptions",
                columns: new[] { "series_id", "date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_recurring_series_exceptions_venue_id",
                table: "recurring_series_exceptions",
                column: "venue_id");

            migrationBuilder.CreateIndex(
                name: "IX_recurring_series_revisions_organization_id",
                table: "recurring_series_revisions",
                column: "organization_id");

            migrationBuilder.CreateIndex(
                name: "IX_recurring_series_revisions_series_id_from_date",
                table: "recurring_series_revisions",
                columns: new[] { "series_id", "from_date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_recurring_series_revisions_venue_id",
                table: "recurring_series_revisions",
                column: "venue_id");

            migrationBuilder.CreateIndex(
                name: "IX_series_conflicts_organization_id",
                table: "series_conflicts",
                column: "organization_id");

            migrationBuilder.CreateIndex(
                name: "IX_series_conflicts_series_id_date",
                table: "series_conflicts",
                columns: new[] { "series_id", "date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_series_conflicts_venue_id",
                table: "series_conflicts",
                column: "venue_id");

            migrationBuilder.CreateIndex(
                name: "IX_series_prepayments_organization_id",
                table: "series_prepayments",
                column: "organization_id");

            migrationBuilder.CreateIndex(
                name: "IX_series_prepayments_series_id",
                table: "series_prepayments",
                column: "series_id");

            migrationBuilder.CreateIndex(
                name: "IX_series_prepayments_venue_id",
                table: "series_prepayments",
                column: "venue_id");

            migrationBuilder.CreateIndex(
                name: "IX_waitlist_entries_court_id",
                table: "waitlist_entries",
                column: "court_id");

            migrationBuilder.CreateIndex(
                name: "IX_waitlist_entries_customer_id",
                table: "waitlist_entries",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "IX_waitlist_entries_offered_booking_id",
                table: "waitlist_entries",
                column: "offered_booking_id");

            migrationBuilder.CreateIndex(
                name: "IX_waitlist_entries_organization_id",
                table: "waitlist_entries",
                column: "organization_id");

            migrationBuilder.CreateIndex(
                name: "IX_waitlist_entries_venue_id_date_status_position",
                table: "waitlist_entries",
                columns: new[] { "venue_id", "date", "status", "position" });

            migrationBuilder.AddForeignKey(
                name: "FK_bookings_recurring_booking_series_series_id",
                table: "bookings",
                column: "series_id",
                principalTable: "recurring_booking_series",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_payments_series_prepayments_prepayment_block_id",
                table: "payments",
                column: "prepayment_block_id",
                principalTable: "series_prepayments",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_bookings_recurring_booking_series_series_id",
                table: "bookings");

            migrationBuilder.DropForeignKey(
                name: "FK_payments_series_prepayments_prepayment_block_id",
                table: "payments");

            migrationBuilder.DropTable(
                name: "recurring_series_exceptions");

            migrationBuilder.DropTable(
                name: "recurring_series_revisions");

            migrationBuilder.DropTable(
                name: "series_conflicts");

            migrationBuilder.DropTable(
                name: "series_prepayments");

            migrationBuilder.DropTable(
                name: "waitlist_entries");

            migrationBuilder.DropTable(
                name: "recurring_booking_series");

            migrationBuilder.DropIndex(
                name: "IX_payments_prepayment_block_id",
                table: "payments");

            migrationBuilder.DropIndex(
                name: "IX_bookings_series_id_series_date",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "recurring_horizon_weeks",
                table: "venues");

            migrationBuilder.DropColumn(
                name: "waitlist_no_show_threshold",
                table: "venues");

            migrationBuilder.DropColumn(
                name: "waitlist_offer_minutes",
                table: "venues");

            migrationBuilder.DropColumn(
                name: "weekly_series_summary",
                table: "venues");

            migrationBuilder.DropColumn(
                name: "prepayment_block_id",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "series_date",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "series_id",
                table: "bookings");
        }
    }
}
