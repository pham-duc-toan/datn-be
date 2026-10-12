using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crowd.Identity.Api.Properties.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IndexHetHanRefreshToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_het_han",
                table: "refresh_tokens",
                column: "expires_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_refresh_tokens_het_han",
                table: "refresh_tokens");
        }
    }
}
