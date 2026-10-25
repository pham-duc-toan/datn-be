using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crowd.Identity.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Username : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "username",
                table: "users",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            // Tai khoan da co: username tu sinh tu phan truoc "@" cua email (chi giu a-z 0-9 . _, toi da 24 ky tu,
            // thieu thi them "user"), trung thi them so 2, 3... — cung quy tac User.UsernameTuEmail. Nguoi dung doi
            // lai trong Ho so.
            migrationBuilder.Sql(@"
DO $$
DECLARE
    r RECORD;
    goc text;
    ten text;
    i int;
BEGIN
    FOR r IN SELECT id, email FROM users ORDER BY created_at, id LOOP
        goc := regexp_replace(lower(split_part(r.email, '@', 1)), '[^a-z0-9._]', '', 'g');
        goc := btrim(left(btrim(goc, '._'), 24), '._');
        IF length(goc) < 3 THEN
            goc := goc || 'user';
        END IF;
        ten := goc;
        i := 2;
        WHILE EXISTS (SELECT 1 FROM users WHERE username = ten) LOOP
            ten := goc || i;
            i := i + 1;
        END LOOP;
        UPDATE users SET username = ten WHERE id = r.id;
    END LOOP;
END $$;");

            migrationBuilder.CreateIndex(
                name: "ux_users_username",
                table: "users",
                column: "username",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_users_username",
                table: "users");

            migrationBuilder.DropColumn(
                name: "username",
                table: "users");
        }
    }
}
