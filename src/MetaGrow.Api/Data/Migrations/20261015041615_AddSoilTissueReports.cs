using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MetaGrow.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSoilTissueReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            SeedTissueDefinition(migrationBuilder);
            migrationBuilder.AddColumn<string>(
                name: "TissueMatchesJson",
                table: "SoilReportStates",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "TissueTemplatesJson",
                table: "SoilReportStates",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "[]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Removing the owned definition cascades to its rows; catalogue fields may be shared by maintained layouts.
            migrationBuilder.DeleteData("SoilReportDefinitions", "Key", "LabReportDefinitionTissue.xml");
            migrationBuilder.DropColumn(
                name: "TissueMatchesJson",
                table: "SoilReportStates");

            migrationBuilder.DropColumn(
                name: "TissueTemplatesJson",
                table: "SoilReportStates");
        }
    }
}
