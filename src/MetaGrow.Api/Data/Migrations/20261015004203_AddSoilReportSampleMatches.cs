using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MetaGrow.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSoilReportSampleMatches : Migration
    {
        /// <summary>Keep report-only sample choices separate from totalgs; existing reports start with automatic matching.</summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SampleMatchesJson",
                table: "SoilReportStates",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "[]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SampleMatchesJson",
                table: "SoilReportStates");
        }
    }
}
