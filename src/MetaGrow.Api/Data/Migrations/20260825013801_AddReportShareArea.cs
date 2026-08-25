using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MetaGrow.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReportShareArea : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ReportShares_SurveyId_CreatedUtc",
                table: "ReportShares");

            migrationBuilder.AddColumn<string>(
                name: "ReportArea",
                table: "ReportShares",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "multicrop");

            migrationBuilder.CreateIndex(
                name: "IX_ReportShares_ReportArea_SurveyId_CreatedUtc",
                table: "ReportShares",
                columns: new[] { "ReportArea", "SurveyId", "CreatedUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ReportShares_ReportArea_SurveyId_CreatedUtc",
                table: "ReportShares");

            migrationBuilder.DropColumn(
                name: "ReportArea",
                table: "ReportShares");

            migrationBuilder.CreateIndex(
                name: "IX_ReportShares_SurveyId_CreatedUtc",
                table: "ReportShares",
                columns: new[] { "SurveyId", "CreatedUtc" });
        }
    }
}
