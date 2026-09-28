using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduConnect.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddCollegeHierarchy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Colleges",
                columns: table => new
                {
                    CollegeID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ShortName = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    LegacyTagID = table.Column<int>(type: "int", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    RetiredAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Colleges", x => x.CollegeID);
                    table.ForeignKey(
                        name: "FK_Colleges_DepartmentTags_LegacyTagID",
                        column: x => x.LegacyTagID,
                        principalTable: "DepartmentTags",
                        principalColumn: "TagID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Departments",
                columns: table => new
                {
                    DepartmentID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CollegeID = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    ShortName = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    IsImplicit = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    RetiredAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Departments", x => x.DepartmentID);
                    table.ForeignKey(
                        name: "FK_Departments_Colleges_CollegeID",
                        column: x => x.CollegeID,
                        principalTable: "Colleges",
                        principalColumn: "CollegeID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Programs",
                columns: table => new
                {
                    ProgramID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DepartmentID = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    ShortName = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    RetiredAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Programs", x => x.ProgramID);
                    table.ForeignKey(
                        name: "FK_Programs_Departments_DepartmentID",
                        column: x => x.DepartmentID,
                        principalTable: "Departments",
                        principalColumn: "DepartmentID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Colleges_LegacyTagID",
                table: "Colleges",
                column: "LegacyTagID");

            migrationBuilder.CreateIndex(
                name: "IX_Colleges_Name",
                table: "Colleges",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Departments_CollegeID_Name",
                table: "Departments",
                columns: new[] { "CollegeID", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Programs_DepartmentID_Name",
                table: "Programs",
                columns: new[] { "DepartmentID", "Name" },
                unique: true);

            // ─── Seed: colleges, departments, programs ───────────────
            // Keyed on names; idempotent. LegacyTagID links each college
            // to the Academic DepartmentTags row it replaces (by ShortName).
            migrationBuilder.Sql(@"
                INSERT INTO Colleges (Name, ShortName, LegacyTagID, IsActive, CreatedAt)
                SELECT v.Name, v.ShortName, t.TagID, 1, SYSDATETIME()
                FROM (VALUES
                    (N'College of Architecture',                          N'COA'),
                    (N'College of Business Administration',               N'CBA'),
                    (N'College of Computing and Information Technology',  N'CCIT'),
                    (N'College of Engineering',                           N'COE'),
                    (N'College of Law',                                   N'LAW'),
                    (N'College of Education & Liberal Arts',              N'CELA'),
                    (N'College of Nursing',                               N'CON'),
                    (N'College of Pharmacy',                              N'COP'),
                    (N'College of Science',                               N'COS')
                ) v(Name, ShortName)
                LEFT JOIN DepartmentTags t
                    ON t.ShortName = v.ShortName
                   AND t.TagTypeID = (SELECT TagTypeID FROM TagTypes WHERE TypeName = 'Academic')
                WHERE NOT EXISTS (SELECT 1 FROM Colleges c WHERE c.Name = v.Name);
            ");

            migrationBuilder.Sql(@"
                INSERT INTO Departments (CollegeID, Name, ShortName, IsImplicit, IsActive, CreatedAt)
                SELECT c.CollegeID, v.Name, v.ShortName, v.IsImplicit, 1, SYSDATETIME()
                FROM (VALUES
                    (N'College of Architecture', N'College of Architecture', NULL, 1),
                    (N'College of Law',          N'College of Law',          NULL, 1),
                    (N'College of Nursing',      N'College of Nursing',      NULL, 1),
                    (N'College of Pharmacy',     N'College of Pharmacy',     NULL, 1),

                    (N'College of Business Administration', N'Department of Accountancy',                       NULL, 0),
                    (N'College of Business Administration', N'Department of Finance and Economics',             NULL, 0),
                    (N'College of Business Administration', N'Department of Management and Marketing',          NULL, 0),
                    (N'College of Business Administration', N'Department of Customs & Supply Chain Management', NULL, 0),
                    (N'College of Business Administration', N'Department of Hospitality and Tourism',           NULL, 0),

                    (N'College of Computing and Information Technology', N'Department of Computer Science', N'CS', 0),
                    (N'College of Computing and Information Technology', N'Department of Information Technology & Information Systems', N'IT&IS', 0),

                    (N'College of Engineering', N'Department of Chemical Engineering',           NULL,   0),
                    (N'College of Engineering', N'Department of Civil Engineering',              NULL,   0),
                    (N'College of Engineering', N'Department of Computer Engineering',           NULL,   0),
                    (N'College of Engineering', N'Department of Electronics Engineering',        N'ECE', 0),
                    (N'College of Engineering', N'Department of Industrial Engineering',         NULL,   0),
                    (N'College of Engineering', N'Department of Mechanical Engineering',         NULL,   0),
                    (N'College of Engineering', N'Department of Mining Engineering and Geology', NULL,   0),

                    (N'College of Education & Liberal Arts', N'Department of Teacher Education',                    NULL, 0),
                    (N'College of Education & Liberal Arts', N'Department of Physical Education',                   NULL, 0),
                    (N'College of Education & Liberal Arts', N'Department of Political Science and Social Studies', NULL, 0),
                    (N'College of Education & Liberal Arts', N'Department of Media and Communication',              NULL, 0),

                    (N'College of Science', N'Department of Biology',    NULL, 0),
                    (N'College of Science', N'Department of Chemistry',  NULL, 0),
                    (N'College of Science', N'Department of Psychology', NULL, 0)
                ) v(CollegeName, Name, ShortName, IsImplicit)
                JOIN Colleges c ON c.Name = v.CollegeName
                WHERE NOT EXISTS (
                    SELECT 1 FROM Departments d
                    WHERE d.CollegeID = c.CollegeID AND d.Name = v.Name);
            ");

            migrationBuilder.Sql(@"
                INSERT INTO Programs (DepartmentID, Name, ShortName, IsActive, CreatedAt)
                SELECT d.DepartmentID, v.Name, v.ShortName, 1, SYSDATETIME()
                FROM (VALUES
                    (N'College of Architecture', N'College of Architecture', N'Bachelor of Science in Architecture', N'BSArch'),
                    (N'College of Law',          N'College of Law',          N'Juris Doctor',                        N'JD'),
                    (N'College of Nursing',      N'College of Nursing',      N'BS in Nursing',                       N'BSN'),
                    (N'College of Pharmacy',     N'College of Pharmacy',     N'BS in Pharmacy',                      N'BSPharm'),

                    (N'College of Business Administration', N'Department of Accountancy',                       N'BS in Accountancy',                                           N'BSA'),
                    (N'College of Business Administration', N'Department of Finance and Economics',             N'BS in Business Administration Major in Financial Management', N'BSBA-FM'),
                    (N'College of Business Administration', N'Department of Management and Marketing',          N'BSBA Major in Marketing Management',                          N'BSBA-MM'),
                    (N'College of Business Administration', N'Department of Management and Marketing',          N'BSBA Major in Operations Management',                         N'BSBA-OM'),
                    (N'College of Business Administration', N'Department of Customs & Supply Chain Management', N'BS in Customs Administration',                                N'BSCA'),
                    (N'College of Business Administration', N'Department of Customs & Supply Chain Management', N'BS in Supply Chain Management',                               N'BSSCM'),
                    (N'College of Business Administration', N'Department of Hospitality and Tourism',           N'BS in Hospitality Management',                                N'BSHM'),

                    (N'College of Computing and Information Technology', N'Department of Computer Science', N'BS in Computer Science',                              N'BSCS'),
                    (N'College of Computing and Information Technology', N'Department of Computer Science', N'BS in Computer Science and Information Engineering', N'BSCSIE'),
                    (N'College of Computing and Information Technology', N'Department of Information Technology & Information Systems', N'BS in Information Systems',   N'BSIS'),
                    (N'College of Computing and Information Technology', N'Department of Information Technology & Information Systems', N'BS in Information Technology', N'BSIT'),

                    (N'College of Engineering', N'Department of Chemical Engineering',           N'BS in Chemical Engineering',        N'BSChE'),
                    (N'College of Engineering', N'Department of Chemical Engineering',           N'BS in Chemical Process Technology', N'BSCPT'),
                    (N'College of Engineering', N'Department of Civil Engineering',              N'BS in Civil Engineering',           N'BSCE'),
                    (N'College of Engineering', N'Department of Computer Engineering',           N'BS in Computer Engineering',        N'BSCpE'),
                    (N'College of Engineering', N'Department of Electronics Engineering',        N'BS in Electrical Engineering',      N'BSEE'),
                    (N'College of Engineering', N'Department of Electronics Engineering',        N'BS in Electronics Engineering',     N'BSECE'),
                    (N'College of Engineering', N'Department of Industrial Engineering',         N'BS in Industrial Engineering',      N'BSIE'),
                    (N'College of Engineering', N'Department of Mechanical Engineering',         N'BS in Mechanical Engineering',      N'BSME'),
                    (N'College of Engineering', N'Department of Mining Engineering and Geology', N'BS in Mining Engineering',          N'BSEM'),
                    (N'College of Engineering', N'Department of Mining Engineering and Geology', N'BS in Geology',                     N'BSGeo'),
                    (N'College of Engineering', N'Department of Mining Engineering and Geology', N'BS in Petroleum Engineering',       N'BSPetE'),

                    (N'College of Education & Liberal Arts', N'Department of Teacher Education',                    N'Bachelor of Elementary Education',                                       N'BEEd'),
                    (N'College of Education & Liberal Arts', N'Department of Teacher Education',                    N'Bachelor of Secondary Education Major in English',                       N'BSEd-Eng'),
                    (N'College of Education & Liberal Arts', N'Department of Physical Education',                   N'Bachelor of Physical Education',                                         N'BPEd'),
                    (N'College of Education & Liberal Arts', N'Department of Physical Education',                   N'Bachelor of Physical Education Major in Sports and Wellness Management', N'BPEd-SWM'),
                    (N'College of Education & Liberal Arts', N'Department of Political Science and Social Studies', N'BA in Political Science',                                                N'ABPolSci'),
                    (N'College of Education & Liberal Arts', N'Department of Political Science and Social Studies', N'BA in Philosophy',                                                       N'ABPhilo'),
                    (N'College of Education & Liberal Arts', N'Department of Media and Communication',              N'BA in Communication',                                                    N'ABComm'),

                    (N'College of Science', N'Department of Biology',    N'BS in Biology',    N'BSBio'),
                    (N'College of Science', N'Department of Chemistry',  N'BS in Chemistry',  N'BSChem'),
                    (N'College of Science', N'Department of Psychology', N'BS in Psychology', N'BSPsych')
                ) v(CollegeName, DepartmentName, Name, ShortName)
                JOIN Colleges c    ON c.Name = v.CollegeName
                JOIN Departments d ON d.CollegeID = c.CollegeID AND d.Name = v.DepartmentName
                WHERE NOT EXISTS (
                    SELECT 1 FROM Programs p
                    WHERE p.DepartmentID = d.DepartmentID AND p.Name = v.Name);
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Programs");

            migrationBuilder.DropTable(
                name: "Departments");

            migrationBuilder.DropTable(
                name: "Colleges");
        }
    }
}
