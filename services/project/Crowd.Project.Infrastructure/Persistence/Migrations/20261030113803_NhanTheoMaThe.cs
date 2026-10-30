using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crowd.Project.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NhanTheoMaThe : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Nhan du an cu la chu tu do → ma the cua danh muc admin (cung quy tac Crowd.BuildingBlocks.Tags.MaThe).
            migrationBuilder.Sql(@"UPDATE projects SET domains = ARRAY(SELECT DISTINCT m FROM (SELECT trim(both '-' from regexp_replace(lower(translate(x, 'àáảãạăằắẳẵặâầấẩẫậèéẻẽẹêềếểễệìíỉĩịòóỏõọôồốổỗộơờớởỡợùúủũụưừứửữựỳýỷỹỵđÀÁẢÃẠĂẰẮẲẴẶÂẦẤẨẪẬÈÉẺẼẸÊỀẾỂỄỆÌÍỈĨỊÒÓỎÕỌÔỒỐỔỖỘƠỜỚỞỠỢÙÚỦŨỤƯỪỨỬỮỰỲÝỶỸỴĐ', 'aaaaaaaaaaaaaaaaaeeeeeeeeeeeiiiiiooooooooooooooooouuuuuuuuuuuyyyyydaaaaaaaaaaaaaaaaaeeeeeeeeeeeiiiiiooooooooooooooooouuuuuuuuuuuyyyyyd')), '[^a-z0-9]+', '-', 'g')) AS m FROM unnest(domains) AS x) t WHERE m <> '') WHERE cardinality(domains) > 0;");
            migrationBuilder.Sql(@"UPDATE projects SET languages = ARRAY(SELECT DISTINCT m FROM (SELECT CASE trim(both '-' from regexp_replace(lower(translate(x, 'àáảãạăằắẳẵặâầấẩẫậèéẻẽẹêềếểễệìíỉĩịòóỏõọôồốổỗộơờớởỡợùúủũụưừứửữựỳýỷỹỵđÀÁẢÃẠĂẰẮẲẴẶÂẦẤẨẪẬÈÉẺẼẸÊỀẾỂỄỆÌÍỈĨỊÒÓỎÕỌÔỒỐỔỖỘƠỜỚỞỠỢÙÚỦŨỤƯỪỨỬỮỰỲÝỶỸỴĐ', 'aaaaaaaaaaaaaaaaaeeeeeeeeeeeiiiiiooooooooooooooooouuuuuuuuuuuyyyyydaaaaaaaaaaaaaaaaaeeeeeeeeeeeiiiiiooooooooooooooooouuuuuuuuuuuyyyyyd')), '[^a-z0-9]+', '-', 'g')) WHEN 'tieng-viet' THEN 'vi' WHEN 'vietnamese' THEN 'vi' WHEN 'tieng-anh' THEN 'en' WHEN 'english' THEN 'en' ELSE trim(both '-' from regexp_replace(lower(translate(x, 'àáảãạăằắẳẵặâầấẩẫậèéẻẽẹêềếểễệìíỉĩịòóỏõọôồốổỗộơờớởỡợùúủũụưừứửữựỳýỷỹỵđÀÁẢÃẠĂẰẮẲẴẶÂẦẤẨẪẬÈÉẺẼẸÊỀẾỂỄỆÌÍỈĨỊÒÓỎÕỌÔỒỐỔỖỘƠỜỚỞỠỢÙÚỦŨỤƯỪỨỬỮỰỲÝỶỸỴĐ', 'aaaaaaaaaaaaaaaaaeeeeeeeeeeeiiiiiooooooooooooooooouuuuuuuuuuuyyyyydaaaaaaaaaaaaaaaaaeeeeeeeeeeeiiiiiooooooooooooooooouuuuuuuuuuuyyyyyd')), '[^a-z0-9]+', '-', 'g')) END AS m FROM unnest(languages) AS x) t WHERE m <> '') WHERE cardinality(languages) > 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Khong khoi phuc duoc chu goc (da bo dau).
        }
    }
}
