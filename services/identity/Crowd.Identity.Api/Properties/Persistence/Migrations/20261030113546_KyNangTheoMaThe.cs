using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crowd.Identity.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class KyNangTheoMaThe : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Linh vuc / ngon ngu cu la chu tu do ("Y te", "Tieng Viet") → ma the cua danh muc admin ("y-te", "vi"):
            // bo dau, chu thuong, ky tu khac a-z 0-9 thanh '-', bo trung. Cung quy tac voi Crowd.BuildingBlocks.Tags.MaThe.
            migrationBuilder.Sql(@"UPDATE users SET skill_domains = ARRAY(SELECT DISTINCT m FROM (SELECT trim(both '-' from regexp_replace(lower(translate(x, 'àáảãạăằắẳẵặâầấẩẫậèéẻẽẹêềếểễệìíỉĩịòóỏõọôồốổỗộơờớởỡợùúủũụưừứửữựỳýỷỹỵđÀÁẢÃẠĂẰẮẲẴẶÂẦẤẨẪẬÈÉẺẼẸÊỀẾỂỄỆÌÍỈĨỊÒÓỎÕỌÔỒỐỔỖỘƠỜỚỞỠỢÙÚỦŨỤƯỪỨỬỮỰỲÝỶỸỴĐ', 'aaaaaaaaaaaaaaaaaeeeeeeeeeeeiiiiiooooooooooooooooouuuuuuuuuuuyyyyydaaaaaaaaaaaaaaaaaeeeeeeeeeeeiiiiiooooooooooooooooouuuuuuuuuuuyyyyyd')), '[^a-z0-9]+', '-', 'g')) AS m FROM unnest(skill_domains) AS x) t WHERE m <> '') WHERE cardinality(skill_domains) > 0;");
            migrationBuilder.Sql(@"UPDATE users SET skill_languages = ARRAY(SELECT DISTINCT m FROM (SELECT CASE trim(both '-' from regexp_replace(lower(translate(x, 'àáảãạăằắẳẵặâầấẩẫậèéẻẽẹêềếểễệìíỉĩịòóỏõọôồốổỗộơờớởỡợùúủũụưừứửữựỳýỷỹỵđÀÁẢÃẠĂẰẮẲẴẶÂẦẤẨẪẬÈÉẺẼẸÊỀẾỂỄỆÌÍỈĨỊÒÓỎÕỌÔỒỐỔỖỘƠỜỚỞỠỢÙÚỦŨỤƯỪỨỬỮỰỲÝỶỸỴĐ', 'aaaaaaaaaaaaaaaaaeeeeeeeeeeeiiiiiooooooooooooooooouuuuuuuuuuuyyyyydaaaaaaaaaaaaaaaaaeeeeeeeeeeeiiiiiooooooooooooooooouuuuuuuuuuuyyyyyd')), '[^a-z0-9]+', '-', 'g')) WHEN 'tieng-viet' THEN 'vi' WHEN 'vietnamese' THEN 'vi' WHEN 'tieng-anh' THEN 'en' WHEN 'english' THEN 'en' ELSE trim(both '-' from regexp_replace(lower(translate(x, 'àáảãạăằắẳẵặâầấẩẫậèéẻẽẹêềếểễệìíỉĩịòóỏõọôồốổỗộơờớởỡợùúủũụưừứửữựỳýỷỹỵđÀÁẢÃẠĂẰẮẲẴẶÂẦẤẨẪẬÈÉẺẼẸÊỀẾỂỄỆÌÍỈĨỊÒÓỎÕỌÔỒỐỔỖỘƠỜỚỞỠỢÙÚỦŨỤƯỪỨỬỮỰỲÝỶỸỴĐ', 'aaaaaaaaaaaaaaaaaeeeeeeeeeeeiiiiiooooooooooooooooouuuuuuuuuuuyyyyydaaaaaaaaaaaaaaaaaeeeeeeeeeeeiiiiiooooooooooooooooouuuuuuuuuuuyyyyyd')), '[^a-z0-9]+', '-', 'g')) END AS m FROM unnest(skill_languages) AS x) t WHERE m <> '') WHERE cardinality(skill_languages) > 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Khong khoi phuc duoc chu goc (da bo dau); ma the van doc duoc nhu chu thuong.
        }
    }
}
