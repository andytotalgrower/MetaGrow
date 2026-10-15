using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MetaGrow.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSoilReportNotSampled : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NotSampledJson",
                table: "SoilReportStates",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "[]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NotSampledJson",
                table: "SoilReportStates");
        }
    }
}
