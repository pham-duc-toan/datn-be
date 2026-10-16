using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crowd.Link.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ChongLamDungGioiThieu : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "claim_ip_hash",
                table: "referrals",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "decided_at",
                table: "referrals",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "decided_by",
                table: "referrals",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "flag_reasons",
                table: "referrals",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "status",
                table: "referrals",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "active");

            migrationBuilder.CreateIndex(
                name: "ix_referrals_nghi_ngo",
                table: "referrals",
                column: "status",
                filter: "status = 'flagged'");

            migrationBuilder.CreateIndex(
                name: "ix_referrals_referrer_thoi_gian",
                table: "referrals",
                columns: new[] { "referrer_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_referrals_nghi_ngo",
                table: "referrals");

            migrationBuilder.DropIndex(
                name: "ix_referrals_referrer_thoi_gian",
                table: "referrals");

            migrationBuilder.DropColumn(
                name: "claim_ip_hash",
                table: "referrals");

            migrationBuilder.DropColumn(
                name: "decided_at",
                table: "referrals");

            migrationBuilder.DropColumn(
                name: "decided_by",
                table: "referrals");

            migrationBuilder.DropColumn(
                name: "flag_reasons",
                table: "referrals");

            migrationBuilder.DropColumn(
                name: "status",
                table: "referrals");
        }
    }
}
