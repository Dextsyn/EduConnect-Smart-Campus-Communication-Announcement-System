using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduConnect.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddModerationWords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ModerationWords",
                columns: table => new
                {
                    ModerationWordID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Word = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Language = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    IsAllowed = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModerationWords", x => x.ModerationWordID);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ModerationWords_Word",
                table: "ModerationWords",
                column: "Word",
                unique: true);

            // Only the always-allowed words are seeded here. The words to
            // star out are private (Moderation:SeedWords) because this repo
            // is public; ModerationWordImporter loads them at startup.
            var seededAt = new DateTime(2026, 10, 26);
            var allowed = new[]
            {
                ("class", "English"), ("classes", "English"), ("assignment", "English"),
                ("assess", "English"), ("assessment", "English"), ("passing", "English"),
                ("scunthorpe", "English"),
                ("pukyutan", "Tagalog"), ("gagamba", "Tagalog"), ("tangkad", "Tagalog"),
                ("pakialam", "Tagalog"), ("titser", "Tagalog")
            };
            foreach (var (word, language) in allowed)
            {
                migrationBuilder.InsertData(
                    table: "ModerationWords",
                    columns: new[] { "Word", "Language", "IsAllowed", "CreatedAt" },
                    values: new object[] { word, language, true, seededAt });
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ModerationWords");
        }
    }
}
