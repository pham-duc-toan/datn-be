using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crowd.Payment.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CongNapVnPayMoMo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "checkout_url",
                table: "payment_intents",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "failure_code",
                table: "payment_intents",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_checked_at",
                table: "payment_intents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_intents_doi_soat",
                table: "payment_intents",
                column: "created_at",
                filter: "status = 'Pending' AND provider <> 'manual_transfer'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_intents_doi_soat",
                table: "payment_intents");

            migrationBuilder.DropColumn(
                name: "checkout_url",
                table: "payment_intents");

            migrationBuilder.DropColumn(
                name: "failure_code",
                table: "payment_intents");

            migrationBuilder.DropColumn(
                name: "last_checked_at",
                table: "payment_intents");
        }
    }
}
