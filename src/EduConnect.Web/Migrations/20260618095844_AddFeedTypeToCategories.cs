using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduConnect.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddFeedTypeToCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FeedType",
                table: "AnnouncementCategories",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "NonAcademic");

            migrationBuilder.Sql(
                "UPDATE AnnouncementCategories SET FeedType = 'Academic' WHERE CategoryName = 'Academic'");
            migrationBuilder.Sql(
                "UPDATE AnnouncementCategories SET FeedType = 'Emergency' WHERE CategoryName = 'Emergency'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FeedType",
                table: "AnnouncementCategories");
        }
    }
}
