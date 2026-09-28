using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduConnect.Web.Migrations
{
    /// <inheritdoc />
    public partial class RemoveLegacyCollegeTags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // CLAS and COED are replaced by the College of Education &
            // Liberal Arts and the College of Science in the new
            // hierarchy; PE becomes a department. Every FK into
            // DepartmentTags is Restrict, so if anything still references
            // one of these rows this fails loudly instead of guessing.
            // Keyed on ShortName; TagID differs between databases.
            migrationBuilder.Sql(
                "DELETE FROM DepartmentTags WHERE ShortName IN ('CLAS', 'COED', 'PE');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                INSERT INTO DepartmentTags (TagName, ShortName, TagTypeID, Description, ColorHex, IsActive, CreatedAt)
                SELECT v.TagName, v.ShortName, tt.TagTypeID, v.Description, v.ColorHex, 1, SYSDATETIME()
                FROM (VALUES
                    ('College of Liberal Arts & Sciences', 'CLAS', 'Liberal Arts announcements', '#0369A1'),
                    ('College of Education', 'COED', 'Education department announcements', '#78350F'),
                    ('Physical Education Department', 'PE', 'PE department announcements', '#15803D')
                ) v(TagName, ShortName, Description, ColorHex)
                CROSS JOIN (SELECT TagTypeID FROM TagTypes WHERE TypeName = 'Academic') tt
                WHERE NOT EXISTS (SELECT 1 FROM DepartmentTags d WHERE d.ShortName = v.ShortName);
            ");
        }
    }
}
