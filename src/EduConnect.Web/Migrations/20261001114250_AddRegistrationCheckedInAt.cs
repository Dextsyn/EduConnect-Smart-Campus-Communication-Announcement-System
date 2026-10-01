using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduConnect.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddRegistrationCheckedInAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CheckedInAt",
                table: "EventRegistrations",
                type: "datetime2",
                nullable: true);

            // No backfill: UpdatedAt is not a trustworthy scan time (rows
            // have been bulk-touched), so earlier attendance has no time.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CheckedInAt",
                table: "EventRegistrations");
        }
    }
}
