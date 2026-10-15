using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Futsal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LaunchQueryIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_payments_organization_id",
                table: "payments");

            migrationBuilder.DropIndex(
                name: "IX_bookings_organization_id",
                table: "bookings");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_family",
                table: "refresh_tokens",
                column: "family_id");

            migrationBuilder.CreateIndex(
                name: "ix_payments_org_booking_created",
                table: "payments",
                columns: new[] { "organization_id", "booking_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_notifications_due_throttle",
                table: "notifications",
                columns: new[] { "organization_id", "customer_id", "template", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_bookings_org_venue_start",
                table: "bookings",
                columns: new[] { "organization_id", "venue_id", "start_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_refresh_family",
                table: "refresh_tokens");

            migrationBuilder.DropIndex(
                name: "ix_payments_org_booking_created",
                table: "payments");

            migrationBuilder.DropIndex(
                name: "ix_notifications_due_throttle",
                table: "notifications");

            migrationBuilder.DropIndex(
                name: "ix_bookings_org_venue_start",
                table: "bookings");

            migrationBuilder.CreateIndex(
                name: "IX_payments_organization_id",
                table: "payments",
                column: "organization_id");

            migrationBuilder.CreateIndex(
                name: "IX_bookings_organization_id",
                table: "bookings",
                column: "organization_id");
        }
    }
}
