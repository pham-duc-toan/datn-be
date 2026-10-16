using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crowd.Project.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BaoVeVaQuanHeLabeler : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "content_warning",
                table: "projects",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<List<string>>(
                name: "domains",
                table: "projects",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'::text[]");

            migrationBuilder.AddColumn<List<string>>(
                name: "languages",
                table: "projects",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'::text[]");

            migrationBuilder.AddColumn<bool>(
                name: "nda_required",
                table: "projects",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "nda_text",
                table: "projects",
                type: "character varying(20000)",
                maxLength: 20000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "nda_version",
                table: "projects",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "require_verified_identity",
                table: "projects",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "sensitive_content",
                table: "projects",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "blocked_by_owner_list",
                table: "project_members",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "labeler_ratings",
                columns: table => new
                {
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    labeler_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stars = table.Column<int>(type: "integer", nullable: false),
                    comment = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_labeler_ratings", x => new { x.project_id, x.labeler_id });
                    table.CheckConstraint("ck_labeler_ratings_sao", "stars BETWEEN 1 AND 5");
                    table.ForeignKey(
                        name: "FK_labeler_ratings_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "owner_labeler_lists",
                columns: table => new
                {
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    labeler_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_owner_labeler_lists", x => new { x.owner_id, x.labeler_id });
                });

            migrationBuilder.CreateTable(
                name: "project_consents",
                columns: table => new
                {
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    content_warning_accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    nda_version = table.Column<int>(type: "integer", nullable: true),
                    nda_text_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    nda_signed_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    nda_signed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ip = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    user_agent = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_project_consents", x => new { x.project_id, x.user_id });
                    table.ForeignKey(
                        name: "FK_project_consents_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "project_invitations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    invited_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_project_invitations", x => x.id);
                    table.ForeignKey(
                        name: "FK_project_invitations_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_labeler_ratings_labeler",
                table: "labeler_ratings",
                column: "labeler_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_invitations_email",
                table: "project_invitations",
                column: "email",
                filter: "status = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "ix_project_invitations_het_han",
                table: "project_invitations",
                column: "expires_at",
                filter: "status = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "ix_project_invitations_user",
                table: "project_invitations",
                column: "user_id",
                filter: "status = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "ux_project_invitations_cho",
                table: "project_invitations",
                columns: new[] { "project_id", "email" },
                unique: true,
                filter: "status = 'Pending'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "labeler_ratings");

            migrationBuilder.DropTable(
                name: "owner_labeler_lists");

            migrationBuilder.DropTable(
                name: "project_consents");

            migrationBuilder.DropTable(
                name: "project_invitations");

            migrationBuilder.DropColumn(
                name: "content_warning",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "domains",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "languages",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "nda_required",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "nda_text",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "nda_version",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "require_verified_identity",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "sensitive_content",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "blocked_by_owner_list",
                table: "project_members");
        }
    }
}
