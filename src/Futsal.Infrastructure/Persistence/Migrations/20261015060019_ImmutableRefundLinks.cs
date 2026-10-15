using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Futsal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ImmutableRefundLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "reversal_of",
                table: "payments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_payment_reversals",
                table: "payments",
                column: "reversal_of");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_payment_reversals",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "reversal_of",
                table: "payments");
        }
    }
}
