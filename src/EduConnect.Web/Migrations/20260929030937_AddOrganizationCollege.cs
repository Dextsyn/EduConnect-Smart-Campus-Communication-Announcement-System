using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduConnect.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganizationCollege : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CollegeID",
                table: "Organizations",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Organizations_CollegeID",
                table: "Organizations",
                column: "CollegeID");

            migrationBuilder.AddForeignKey(
                name: "FK_Organizations_Colleges_CollegeID",
                table: "Organizations",
                column: "CollegeID",
                principalTable: "Colleges",
                principalColumn: "CollegeID",
                onDelete: ReferentialAction.Restrict);

            // Organizations linked to what is now a college keep that link.
            migrationBuilder.Sql(@"
                UPDATE o SET CollegeID = c.CollegeID
                FROM Organizations o
                JOIN Colleges c ON c.LegacyTagID = o.DepartmentTagID
                WHERE o.CollegeID IS NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Organizations_Colleges_CollegeID",
                table: "Organizations");

            migrationBuilder.DropIndex(
                name: "IX_Organizations_CollegeID",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "CollegeID",
                table: "Organizations");
        }
    }
}
