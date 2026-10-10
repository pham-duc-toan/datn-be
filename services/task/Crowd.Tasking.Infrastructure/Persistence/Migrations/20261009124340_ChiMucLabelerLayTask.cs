using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crowd.Tasking.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ChiMucLabelerLayTask : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_assignments_labeler_active",
                table: "assignments",
                columns: new[] { "labeler_id", "task_id" },
                filter: "state IN ('Leased','Submitted')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_assignments_labeler_active",
                table: "assignments");
        }
    }
}
