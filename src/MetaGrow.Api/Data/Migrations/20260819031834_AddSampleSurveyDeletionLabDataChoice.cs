using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MetaGrow.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSampleSurveyDeletionLabDataChoice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "DeleteLinkedLabResults",
                table: "SampleSurveyDeletionRequests",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "LinkedLegacyResultCount",
                table: "SampleSurveyDeletionRequests",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "LinkedQuickSoilResultCount",
                table: "SampleSurveyDeletionRequests",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "LinkedSapResultCount",
                table: "SampleSurveyDeletionRequests",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "LinkedSoilResultCount",
                table: "SampleSurveyDeletionRequests",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "LinkedTissueResultCount",
                table: "SampleSurveyDeletionRequests",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeleteLinkedLabResults",
                table: "SampleSurveyDeletionRequests");

            migrationBuilder.DropColumn(
                name: "LinkedLegacyResultCount",
                table: "SampleSurveyDeletionRequests");

            migrationBuilder.DropColumn(
                name: "LinkedQuickSoilResultCount",
                table: "SampleSurveyDeletionRequests");

            migrationBuilder.DropColumn(
                name: "LinkedSapResultCount",
                table: "SampleSurveyDeletionRequests");

            migrationBuilder.DropColumn(
                name: "LinkedSoilResultCount",
                table: "SampleSurveyDeletionRequests");

            migrationBuilder.DropColumn(
                name: "LinkedTissueResultCount",
                table: "SampleSurveyDeletionRequests");
        }
    }
}
