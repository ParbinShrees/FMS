using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Futsal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LaunchHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "family_id",
                table: "refresh_tokens",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "subscriptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    max_courts = table.Column<int>(type: "integer", nullable: false),
                    grace_days = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subscriptions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_subscriptions_organization_id",
                table: "subscriptions",
                column: "organization_id",
                unique: true);
            migrationBuilder.Sql("""
                INSERT INTO subscriptions(id,organization_id,plan,status,starts_at,ends_at,max_courts,grace_days)
                SELECT gen_random_uuid(),id,'Basic','TRIAL',now(),now()+interval '30 days',GREATEST(2,(SELECT count(*)::int FROM courts WHERE courts.organization_id=organizations.id AND is_active)),7 FROM organizations;
                WITH RECURSIVE families AS (
                  SELECT id,id AS family_id FROM refresh_tokens r WHERE NOT EXISTS(SELECT 1 FROM refresh_tokens parent WHERE parent.replaced_by=r.id)
                  UNION ALL SELECT child.id,f.family_id FROM families f JOIN refresh_tokens parent ON parent.id=f.id JOIN refresh_tokens child ON child.id=parent.replaced_by
                ) UPDATE refresh_tokens r SET family_id=f.family_id FROM families f WHERE f.id=r.id;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "subscriptions");

            migrationBuilder.DropColumn(
                name: "family_id",
                table: "refresh_tokens");
        }
    }
}
