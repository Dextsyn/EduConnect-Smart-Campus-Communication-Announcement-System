using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduConnect.Web.Migrations
{
    /// <inheritdoc />
    public partial class RemoveUnusedDepartments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Graduate School and College of Maritime Studies are not Adamson
            // departments we actually run — CME was never deliberately added.
            // Neither is referenced by anything: no members, announcements,
            // organizations or study groups.
            //
            // Keyed on ShortName rather than TagID because TagID is an
            // identity column and differs between databases.
            migrationBuilder.DeleteData(
                table: "DepartmentTags",
                keyColumn: "ShortName",
                keyValue: "GS");

            migrationBuilder.DeleteData(
                table: "DepartmentTags",
                keyColumn: "ShortName",
                keyValue: "CME");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "DepartmentTags",
                columns: new[] {
                    "TagName", "ShortName", "TagTypeID",
                    "Description", "ColorHex",
                    "IsActive", "CreatedAt"
                },
                values: new object[,] {
                    {
                        "Graduate School", "GS", 1,
                        "Graduate school announcements", "#7E22CE",
                        true, DateTime.Now
                    },
                    {
                        "College of Maritime Studies", "CME", 1,
                        "Maritime department announcements", "#0EA5E9",
                        true, DateTime.Now
                    }
                });
        }
    }
}
