using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MetaGrow.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class SyncModelChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SampleSurveyDeletionRequests_SurveyId",
                table: "SampleSurveyDeletionRequests");

            migrationBuilder.AddColumn<int>(
                name: "SurveyType",
                table: "SampleSurveyDeletionRequests",
                type: "int",
                nullable: false,
                // Every existing row predates the discriminator and is a Sample survey request.
                // MetaGrowSurveyType.Sample = 2.
                defaultValue: 2);

            migrationBuilder.CreateIndex(
                name: "IX_SampleSurveyDeletionRequests_SurveyType_SurveyId",
                table: "SampleSurveyDeletionRequests",
                columns: new[] { "SurveyType", "SurveyId" },
                unique: true,
                filter: "[Status] IN (0, 1)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SampleSurveyDeletionRequests_SurveyType_SurveyId",
                table: "SampleSurveyDeletionRequests");

            migrationBuilder.DropColumn(
                name: "SurveyType",
                table: "SampleSurveyDeletionRequests");

            migrationBuilder.CreateIndex(
                name: "IX_SampleSurveyDeletionRequests_SurveyId",
                table: "SampleSurveyDeletionRequests",
                column: "SurveyId",
                unique: true,
                filter: "[Status] IN (0, 1)");
        }
    }
}
