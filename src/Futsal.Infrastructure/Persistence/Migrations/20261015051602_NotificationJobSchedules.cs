using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Futsal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NotificationJobSchedules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "enabled_at",
                table: "notification_settings",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.Sql("""
                UPDATE notification_settings SET enabled_at=now();
                INSERT INTO notification_settings(id,organization_id,venue_id,enabled_at,confirmation_enabled,payment_enabled,cancellation_enabled,reminder_enabled,due_enabled,include_bs_date,reminder_hours,daily_limit,monthly_limit,language)
                SELECT gen_random_uuid(),organization_id,id,now(),true,true,true,true,false,false,2,100,2000,'en' FROM venues ON CONFLICT(venue_id) DO NOTHING;
                """);
            migrationBuilder.Sql("""
                INSERT INTO notification_templates(id,organization_id,venue_id,name,language,text) SELECT gen_random_uuid(),organization_id,id,'confirmation','en','Booking {code} confirmed at {venue}, {court}, {date} {time}.' FROM venues ON CONFLICT(venue_id,name,language) DO NOTHING;
                """);
            migrationBuilder.Sql("""
                INSERT INTO notification_templates(id,organization_id,venue_id,name,language,text) SELECT gen_random_uuid(),organization_id,id,'payment','en','Payment Rs {amount} received for {code} at {venue}.' FROM venues ON CONFLICT(venue_id,name,language) DO NOTHING;
                """);
            migrationBuilder.Sql("""
                INSERT INTO notification_templates(id,organization_id,venue_id,name,language,text) SELECT gen_random_uuid(),organization_id,id,'reminder','en','Reminder: {court} at {venue}, {date} {time}. Booking {code}.' FROM venues ON CONFLICT(venue_id,name,language) DO NOTHING;
                """);
            migrationBuilder.Sql("""
                INSERT INTO notification_templates(id,organization_id,venue_id,name,language,text) SELECT gen_random_uuid(),organization_id,id,'cancellation','en','Booking {code} at {venue} has been cancelled.' FROM venues ON CONFLICT(venue_id,name,language) DO NOTHING;
                """);
            migrationBuilder.Sql("""
                INSERT INTO notification_templates(id,organization_id,venue_id,name,language,text) SELECT gen_random_uuid(),organization_id,id,'due','en','Outstanding balance at {venue}: Rs {amount}. Please contact the venue.' FROM venues ON CONFLICT(venue_id,name,language) DO NOTHING;
                """);
            migrationBuilder.Sql("""
                INSERT INTO notification_templates(id,organization_id,venue_id,name,language,text) SELECT gen_random_uuid(),organization_id,id,'otp','en','FutsalPro verification code: {code}. Expires in 5 minutes.' FROM venues ON CONFLICT(venue_id,name,language) DO NOTHING;
                """);
            migrationBuilder.Sql("""
                INSERT INTO notification_templates(id,organization_id,venue_id,name,language,text) SELECT gen_random_uuid(),organization_id,id,'confirmation','ne','{venue} मा {date} {time} को {court} बुकिङ {code} पुष्टि भयो।' FROM venues ON CONFLICT(venue_id,name,language) DO NOTHING;
                """);
            migrationBuilder.Sql("""
                INSERT INTO notification_templates(id,organization_id,venue_id,name,language,text) SELECT gen_random_uuid(),organization_id,id,'payment','ne','{venue} मा {code} को रु {amount} भुक्तानी प्राप्त भयो।' FROM venues ON CONFLICT(venue_id,name,language) DO NOTHING;
                """);
            migrationBuilder.Sql("""
                INSERT INTO notification_templates(id,organization_id,venue_id,name,language,text) SELECT gen_random_uuid(),organization_id,id,'reminder','ne','सम्झना: {venue}, {court}, {date} {time}। बुकिङ {code}।' FROM venues ON CONFLICT(venue_id,name,language) DO NOTHING;
                """);
            migrationBuilder.Sql("""
                INSERT INTO notification_templates(id,organization_id,venue_id,name,language,text) SELECT gen_random_uuid(),organization_id,id,'cancellation','ne','{venue} को बुकिङ {code} रद्द भयो।' FROM venues ON CONFLICT(venue_id,name,language) DO NOTHING;
                """);
            migrationBuilder.Sql("""
                INSERT INTO notification_templates(id,organization_id,venue_id,name,language,text) SELECT gen_random_uuid(),organization_id,id,'due','ne','{venue} मा बाँकी रु {amount} छ। कृपया सम्पर्क गर्नुहोस्।' FROM venues ON CONFLICT(venue_id,name,language) DO NOTHING;
                """);
            migrationBuilder.Sql("""
                INSERT INTO notification_templates(id,organization_id,venue_id,name,language,text) SELECT gen_random_uuid(),organization_id,id,'otp','ne','FutsalPro प्रमाणीकरण कोड: {code}। ५ मिनेटमा समाप्त हुन्छ।' FROM venues ON CONFLICT(venue_id,name,language) DO NOTHING;
                """);
            migrationBuilder.CreateTable(
                name: "notification_job_runs",
                columns: table => new
                {
                    name = table.Column<string>(type: "text", nullable: false),
                    next_run = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_job_runs", x => x.name);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "notification_job_runs");

            migrationBuilder.DropColumn(
                name: "enabled_at",
                table: "notification_settings");
        }
    }
}
