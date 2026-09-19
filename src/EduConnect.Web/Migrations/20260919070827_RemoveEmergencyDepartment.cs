using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduConnect.Web.Migrations
{
    /// <inheritdoc />
    public partial class RemoveEmergencyDepartment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Adamson has no Emergency department — the row seeded by
            // V2_AddNewFeatures was never a real college or office, and
            // nothing references it (no members, announcements, orgs or
            // study groups). Urgency is expressed by Announcement.IsEmergency;
            // campus-wide reach is expressed by the School Wide ("ALL") tag.
            //
            // Keyed on ShortName rather than TagID because TagID is an
            // identity column and differs between databases.
            migrationBuilder.DeleteData(
                table: "DepartmentTags",
                keyColumn: "ShortName",
                keyValue: "EMRG");
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
                values: new object[] {
                    "Emergency", "EMRG", 3,
                    "Urgent campus wide emergency alerts",
                    "#DC2626", true, DateTime.Now
                });
        }
    }
}
