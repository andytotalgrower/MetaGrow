using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MetaGrow.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSoilReportDefinitions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SoilReportDefinitionAudits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DefinitionKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Version = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChangedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ChangedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ChangedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    BeforeJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AfterJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SoilReportDefinitionAudits", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SoilReportDefinitionFields",
                columns: table => new
                {
                    Key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Label = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SoilReportDefinitionFields", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "SoilReportDefinitions",
                columns: table => new
                {
                    Key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Version = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SoilReportDefinitions", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "SoilReportDefinitionSections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    DefinitionKey = table.Column<string>(type: "nvarchar(100)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Colour = table.Column<string>(type: "nvarchar(7)", maxLength: 7, nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false),
                    SampleSurveyVisible = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SoilReportDefinitionSections", x => new { x.DefinitionKey, x.Id });
                    table.ForeignKey(
                        name: "FK_SoilReportDefinitionSections_SoilReportDefinitions_DefinitionKey",
                        column: x => x.DefinitionKey,
                        principalTable: "SoilReportDefinitions",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SoilReportDefinitionItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    DefinitionKey = table.Column<string>(type: "nvarchar(100)", nullable: false),
                    SectionId = table.Column<int>(type: "int", nullable: false),
                    Field = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Label = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false),
                    NumberFormat = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ShowIfZero = table.Column<bool>(type: "bit", nullable: false),
                    BackgroundColour = table.Column<string>(type: "nvarchar(7)", maxLength: 7, nullable: true),
                    LegacyItemId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SoilReportDefinitionItems", x => new { x.DefinitionKey, x.SectionId, x.Id });
                    table.ForeignKey(
                        name: "FK_SoilReportDefinitionItems_SoilReportDefinitionSections_DefinitionKey_SectionId",
                        columns: x => new { x.DefinitionKey, x.SectionId },
                        principalTable: "SoilReportDefinitionSections",
                        principalColumns: new[] { "DefinitionKey", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SoilReportDefinitionAudits_DefinitionKey_ChangedUtc",
                table: "SoilReportDefinitionAudits",
                columns: new[] { "DefinitionKey", "ChangedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SoilReportDefinitionItems_DefinitionKey_Field",
                table: "SoilReportDefinitionItems",
                columns: new[] { "DefinitionKey", "Field" },
                unique: true);

            // One-time conversion; later deployments never overwrite maintained definitions.
            SeedDefinitions(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SoilReportDefinitionAudits");

            migrationBuilder.DropTable(
                name: "SoilReportDefinitionFields");

            migrationBuilder.DropTable(
                name: "SoilReportDefinitionItems");

            migrationBuilder.DropTable(
                name: "SoilReportDefinitionSections");

            migrationBuilder.DropTable(
                name: "SoilReportDefinitions");
        }
    }
}
